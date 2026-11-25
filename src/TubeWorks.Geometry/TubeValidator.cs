using System;
using System.Collections.Generic;
using System.Linq;

namespace TubeWorks.Geometry
{
    public enum IssueSeverity { Warning = 0, Error = 1 }

    public sealed class ValidationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string FeatureId { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public override string ToString() => $"{Severity}: {Message}";
    }

    /// <summary>
    /// Manufacturing limits. Every limit is optional: a null value means "not defined", and then that
    /// check is not done. TubeWorks does not invent limits; they come from the operator's library or tooling data.
    /// </summary>
    public sealed class ManufacturingRules
    {
        public double? MinBendRadius { get; set; }
        public double? MinStraightBetweenBends { get; set; }
        public double? MinClampLength { get; set; }
        /// <summary>Largest bend angle the machine can make. Default 180°, the usual limit of rotary-draw benders; set null to disable.</summary>
        public double? MaxBendAngle { get; set; } = Math.PI;
    }

    public static class TubeValidator
    {
        public static List<ValidationIssue> Validate(ChainResult chain, ManufacturingRules rules)
        {
            rules = rules ?? new ManufacturingRules();
            var issues = new List<ValidationIssue>();

            foreach (var f in chain.Features)
            {
                if (f.Error != null)
                    issues.Add(new ValidationIssue { Severity = IssueSeverity.Error, FeatureId = f.Data.Id, Code = "GEOMETRY", Message = $"{f.FeatureName}: {f.Error}" });
            }

            foreach (var path in chain.Paths)
            {
                var prims = path.Primitives.Where(p => p.Length > 1e-12).ToList();
                var arcs = prims.OfType<ArcPrimitive>().ToList();
                string name(string id) => chain.Find(id)?.FeatureName ?? id;

                foreach (var a in arcs)
                {
                    if (rules.MinBendRadius.HasValue && a.Radius < rules.MinBendRadius.Value - 1e-9)
                        issues.Add(W(a.FeatureId, "MIN_RADIUS", $"{name(a.FeatureId)}: centreline radius {a.Radius * 1000:0.###} mm is less than the minimum {rules.MinBendRadius.Value * 1000:0.###} mm for this tube."));
                    if (rules.MaxBendAngle.HasValue && a.Angle > rules.MaxBendAngle.Value + 1e-9)
                        issues.Add(W(a.FeatureId, "MAX_ANGLE", $"{name(a.FeatureId)}: bend angle {Angles.ToDeg(a.Angle):0.##}° is more than the machine limit {Angles.ToDeg(rules.MaxBendAngle.Value):0.##}°."));
                }

                // Straight between consecutive bends, and clamping length at the two ends.
                var table = BendTable.Build(prims);
                for (int i = 1; i < table.Rows.Count; i++)
                {
                    var r = table.Rows[i];
                    if (r.Feed < 1e-9)
                        issues.Add(W(r.FeatureId, "NO_STRAIGHT", $"Bend {r.Number} starts directly at the end of bend {r.Number - 1} (no straight between them). This needs compound-bend tooling."));
                    else if (rules.MinStraightBetweenBends.HasValue && r.Feed < rules.MinStraightBetweenBends.Value - 1e-9)
                        issues.Add(W(r.FeatureId, "SHORT_STRAIGHT", $"Straight before bend {r.Number} is {r.Feed * 1000:0.###} mm. The minimum is {rules.MinStraightBetweenBends.Value * 1000:0.###} mm."));
                }
                if (rules.MinClampLength.HasValue && table.Rows.Count > 0)
                {
                    if (table.Rows[0].Feed < rules.MinClampLength.Value - 1e-9 && table.FinalStraight < rules.MinClampLength.Value - 1e-9)
                        issues.Add(W(path.RootId, "CLAMP", $"Neither end straight ({table.Rows[0].Feed * 1000:0.###} mm, {table.FinalStraight * 1000:0.###} mm) is as long as the clamping length {rules.MinClampLength.Value * 1000:0.###} mm."));
                }

                var si = FindSelfIntersection(prims, path.Profile.OuterDiameter);
                if (si != null)
                    issues.Add(new ValidationIssue
                    {
                        Severity = IssueSeverity.Error, FeatureId = si.Value.featureId, Code = "SELF_INTERSECTION",
                        Message = $"The tube intersects itself near {name(si.Value.featureId)}: centreline points are {si.Value.distance * 1000:0.###} mm apart, less than the outside diameter. (Sampled check.)",
                    });
            }
            return issues;
        }

        private static ValidationIssue W(string id, string code, string msg) =>
            new ValidationIssue { Severity = IssueSeverity.Warning, FeatureId = id, Code = code, Message = msg };

        /// <summary>
        /// Sampled check. Two centreline points that are more than pi*OD/2 apart along the path must be at least OD apart
        /// in space. (Inside one bend of radius OD/2, points pi*OD/2 apart along the path are exactly OD apart, so closer
        /// pairs along the path are never reported.) Sample spacing is OD/8, so the reported distance can be
        /// slightly larger than the true minimum.
        /// </summary>
        public static (string featureId, double distance)? FindSelfIntersection(IReadOnlyList<PathPrimitive> prims, double od)
        {
            double step = od / 8;
            var pts = new List<(Vec3 p, double s, string id)>();
            double s0 = 0;
            foreach (var pr in prims)
            {
                foreach (var (p, s) in pr.Sample(step)) pts.Add((p, s0 + s, pr.FeatureId));
                s0 += pr.Length;
            }
            double minSep = Math.PI * od / 2 + step;
            double tol = od * 1e-6;
            (string, double)? worst = null;
            double worstD = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    if (pts[j].s - pts[i].s <= minSep) continue;
                    double d = pts[i].p.DistanceTo(pts[j].p);
                    if (d < od - tol && d < worstD) { worstD = d; worst = (pts[j].id, d); }
                }
            return worst;
        }
    }
}
