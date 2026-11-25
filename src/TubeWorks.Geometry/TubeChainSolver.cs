using System;
using System.Collections.Generic;
using System.Linq;

namespace TubeWorks.Geometry
{
    /// <summary>One feature as read from the FeatureManager tree, in tree order.</summary>
    public sealed class ChainInput
    {
        public TubeFeatureData Data { get; set; }
        public string FeatureName { get; set; }
        public bool Suppressed { get; set; }
    }

    /// <summary>Start position of a root feature that sits on a tube that TubeWorks did not create.</summary>
    public sealed class ForeignAnchor
    {
        public TubeFrame Frame { get; set; }
        public TubeProfile Profile { get; set; }
    }

    /// <summary>Computed geometry of one feature.</summary>
    public sealed class FeatureSolution
    {
        public TubeFeatureData Data { get; set; }
        public string FeatureName { get; set; }
        public bool Suppressed { get; set; }
        public TubeFrame StartFrame { get; set; }
        public TubeFrame EndFrame { get; set; }
        public TubeProfile Profile { get; set; }
        public List<PathPrimitive> Primitives { get; } = new List<PathPrimitive>();
        /// <summary>Null when the feature solved correctly.</summary>
        public string Error { get; set; }
        public bool IsValid => Error == null && !Suppressed;
        public double CentrelineLength => Primitives.Sum(p => p.Length);
    }

    /// <summary>A connected tube from one free end to the other.</summary>
    public sealed class TubePath
    {
        public string RootId { get; set; }
        public bool RootIsBaseTube { get; set; }
        public TubeProfile Profile { get; set; }
        public List<PathPrimitive> Primitives { get; } = new List<PathPrimitive>();
        public List<string> FeatureIds { get; } = new List<string>();
        public double DevelopedLength => Primitives.Sum(p => p.Length);
    }

    public sealed class ChainResult
    {
        public List<FeatureSolution> Features { get; } = new List<FeatureSolution>();
        public List<TubePath> Paths { get; } = new List<TubePath>();
        public FeatureSolution Find(string id) => Features.FirstOrDefault(f => f.Data.Id == id);
    }

    /// <summary>
    /// Computes every TubeWorks feature's geometry from its stored parameters. The computation is pure:
    /// the same parameters always give the same geometry. SOLIDWORKS face ids are never used to link
    /// features; each feature stores the Id of its parent feature.
    /// </summary>
    public static class TubeChainSolver
    {
        public static ChainResult Solve(IReadOnlyList<ChainInput> inputs, IReadOnlyDictionary<string, ForeignAnchor> foreignAnchors = null)
        {
            var result = new ChainResult();
            var byId = new Dictionary<string, FeatureSolution>();
            var usedEnds = new HashSet<string>();

            foreach (var input in inputs)
            {
                var d = input.Data;
                if (d.Kind == TubeFeatureKind.FlatPattern) continue;
                var fs = new FeatureSolution { Data = d, FeatureName = input.FeatureName, Suppressed = input.Suppressed };
                result.Features.Add(fs);
                if (byId.ContainsKey(d.Id)) { fs.Error = "Duplicate feature id. The feature was probably copied. Delete and create it again."; continue; }
                byId[d.Id] = fs;
                try { SolveOne(fs, byId, usedEnds, foreignAnchors); }
                catch (Exception ex) { fs.Error = ex.Message; }
            }

            BuildPaths(result);
            return result;
        }

        private static void SolveOne(FeatureSolution fs, Dictionary<string, FeatureSolution> byId, HashSet<string> usedEnds,
            IReadOnlyDictionary<string, ForeignAnchor> anchors)
        {
            var d = fs.Data;
            TubeFrame start;

            if (d.Kind == TubeFeatureKind.BaseTube)
            {
                if (!d.IsRoot) { fs.Error = "A Base Tube cannot have a parent."; return; }
                fs.Profile = d.GetProfile();
                if (d.Axis.IsZero()) { fs.Error = "Tube axis direction is zero."; return; }
                start = new TubeFrame(d.Origin, d.Axis);
            }
            else if (d.IsRoot)
            {
                if (anchors != null && anchors.TryGetValue(d.Id, out var a) && a != null)
                {
                    start = a.Frame; fs.Profile = a.Profile;
                }
                else
                {
                    // Anchor captured when the feature was last edited.
                    if (d.Axis.IsZero() || d.OuterDiameter <= 0) { fs.Error = "The start face of this feature is not available. Edit the feature and select the tube end face again."; return; }
                    start = new TubeFrame(d.Origin, d.Axis); fs.Profile = d.GetProfile();
                }
            }
            else
            {
                if (!byId.TryGetValue(d.ParentId, out var parent))
                {
                    fs.Error = "The parent tube feature is missing (deleted, or below this feature in the tree). Edit this feature and select a tube end face.";
                    return;
                }
                if (parent.Suppressed) { fs.Error = $"The parent feature '{parent.FeatureName}' is suppressed."; return; }
                if (parent.Error != null) { fs.Error = $"The parent feature '{parent.FeatureName}' has an error."; return; }
                if (d.ParentEnd == ParentEnd.Start && parent.Data.Kind != TubeFeatureKind.BaseTube)
                { fs.Error = "Only a Base Tube has a usable start face."; return; }
                string endKey = parent.Data.Id + "/" + (int)d.ParentEnd;
                if (!fs.Suppressed && !usedEnds.Add(endKey))
                { fs.Error = $"That end of '{parent.FeatureName}' already continues into another feature. A tube cannot branch."; return; }
                start = d.ParentEnd == ParentEnd.Start ? parent.StartFrame.Reversed() : parent.EndFrame;
                fs.Profile = parent.Profile;
            }

            fs.StartFrame = start;

            switch (d.Kind)
            {
                case TubeFeatureKind.BaseTube:
                    if (!(d.Length > 0)) { fs.Error = "Tube length must be greater than zero."; return; }
                    fs.Primitives.Add(new StraightPrimitive(start.Origin, start.Tangent, d.Length));
                    break;

                case TubeFeatureKind.Straight:
                    double len = StraightLength(d, start, out string err);
                    if (err != null) { fs.Error = err; return; }
                    fs.Primitives.Add(new StraightPrimitive(start.Origin, start.Tangent, len));
                    break;

                case TubeFeatureKind.Bend:
                    string be = ValidateBendInput(d, fs.Profile);
                    if (be != null) { fs.Error = be; return; }
                    fs.Primitives.AddRange(BendMath.BuildBend(start, d.BendRadius, d.BendAngle, d.PlaneRotation, d.ReverseDirection, d.StraightAfter));
                    break;

                default:
                    fs.Error = "Unknown feature type.";
                    return;
            }

            foreach (var p in fs.Primitives) p.FeatureId = d.Id;
            var last = fs.Primitives[fs.Primitives.Count - 1];
            fs.EndFrame = new TubeFrame(last.EndPoint, last.EndTangent);
        }

        /// <summary>Errors that make the geometry impossible. Manufacturing limits are checked separately by <see cref="TubeValidator"/>.</summary>
        public static string ValidateBendInput(TubeFeatureData d, TubeProfile profile)
        {
            if (!(d.BendRadius > 0)) return "Bend radius must be greater than zero.";
            if (profile != null && !(d.BendRadius > profile.OuterRadius))
                return $"Centreline bend radius ({d.BendRadius * 1000:0.###} mm) must be greater than half the outside diameter ({profile.OuterRadius * 1000:0.###} mm). Otherwise the inside of the bend intersects itself.";
            if (!(d.BendAngle > 0)) return "Bend angle must be greater than 0°.";
            if (d.BendAngle >= 2 * Math.PI) return "Bend angle must be less than 360°.";
            if (d.StraightAfter < 0) return "Straight length after the bend cannot be negative.";
            return null;
        }

        public static double StraightLength(TubeFeatureData d, TubeFrame start, out string error)
        {
            error = null;
            double len;
            switch (d.EndCondition)
            {
                case StraightEndCondition.UpToPoint:
                    len = (d.UpToPoint - start.Origin).Dot(start.Tangent);
                    if (!(len > BendSolver.LengthTol)) { error = "The selected point is not in front of the tube end."; return 0; }
                    return len;
                case StraightEndCondition.UpToPlane:
                    double den = start.Tangent.Dot(d.UpToPlaneNormal.Normalized());
                    if (Math.Abs(den) < 1e-9) { error = "The selected plane is parallel to the tube axis."; return 0; }
                    len = (d.UpToPoint - start.Origin).Dot(d.UpToPlaneNormal.Normalized()) / den;
                    if (!(len > BendSolver.LengthTol)) { error = "The selected plane is not in front of the tube end."; return 0; }
                    return len;
                default:
                    if (!(d.Length > 0)) { error = "Extension length must be greater than zero."; return 0; }
                    return d.Length;
            }
        }

        private static void BuildPaths(ChainResult result)
        {
            var valid = result.Features.Where(f => f.IsValid).ToList();
            var childOf = new Dictionary<string, FeatureSolution>();
            foreach (var f in valid)
                if (!f.Data.IsRoot) childOf[f.Data.ParentId + "/" + (int)f.Data.ParentEnd] = f;

            foreach (var root in valid.Where(f => f.Data.IsRoot))
            {
                var path = new TubePath { RootId = root.Data.Id, RootIsBaseTube = root.Data.Kind == TubeFeatureKind.BaseTube, Profile = root.Profile };

                // Upstream side (only a Base Tube has one): walk it, then reverse it.
                var back = Walk(root, ParentEnd.Start, childOf);
                for (int i = back.Count - 1; i >= 0; i--)
                {
                    path.FeatureIds.Add(back[i].Data.Id);
                    for (int j = back[i].Primitives.Count - 1; j >= 0; j--) path.Primitives.Add(back[i].Primitives[j].Reversed());
                }

                path.FeatureIds.Add(root.Data.Id);
                path.Primitives.AddRange(root.Primitives);
                foreach (var f in Walk(root, ParentEnd.End, childOf))
                {
                    path.FeatureIds.Add(f.Data.Id);
                    path.Primitives.AddRange(f.Primitives);
                }
                result.Paths.Add(path);
            }
        }

        private static List<FeatureSolution> Walk(FeatureSolution root, ParentEnd firstEnd, Dictionary<string, FeatureSolution> childOf)
        {
            var list = new List<FeatureSolution>();
            var guard = new HashSet<string>();
            childOf.TryGetValue(root.Data.Id + "/" + (int)firstEnd, out var cur);
            while (cur != null && guard.Add(cur.Data.Id))
            {
                list.Add(cur);
                childOf.TryGetValue(cur.Data.Id + "/" + (int)ParentEnd.End, out cur);
            }
            return list;
        }

        /// <summary>Find the feature end whose centre matches a point, for example the centre of a selected end face.</summary>
        public static bool TryMatchEnd(ChainResult chain, Vec3 faceCentre, Vec3 outwardNormal, double tol, out FeatureSolution feature, out ParentEnd end)
        {
            feature = null; end = ParentEnd.End;
            foreach (var f in chain.Features.Where(x => x.IsValid))
            {
                if (f.EndFrame.Origin.AlmostEquals(faceCentre, tol) && f.EndFrame.Tangent.Dot(outwardNormal) > 0.999)
                { feature = f; end = ParentEnd.End; return true; }
                if (f.Data.Kind == TubeFeatureKind.BaseTube && f.StartFrame.Origin.AlmostEquals(faceCentre, tol) && f.StartFrame.Tangent.Dot(outwardNormal) < -0.999)
                { feature = f; end = ParentEnd.Start; return true; }
            }
            return false;
        }
    }
}
