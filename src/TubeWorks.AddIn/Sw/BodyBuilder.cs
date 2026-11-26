using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>
    /// Creates temporary (non-feature) bodies with the SOLIDWORKS modeler, for macro feature output and previews.
    /// Every body is checked against the exact analytic volume (Pappus: volume = centreline length x annulus area),
    /// so a wrong assumption about an API argument layout gives a clear error, never silently wrong geometry.
    /// </summary>
    internal sealed class BodyBuilder
    {
        private readonly IModeler _modeler;
        private readonly IMathUtility _math;
        public bool VerifyGeometry { get; set; } = true;

        public BodyBuilder(ISldWorks app)
        {
            _modeler = (IModeler)app.GetModeler();
            _math = (IMathUtility)app.GetMathUtility();
        }

        public static double AnnulusArea(TubeProfile p) => Math.PI * (p.OuterRadius * p.OuterRadius - p.InnerRadius * p.InnerRadius);

        /// <summary>Hollow tube body along the primitives. Returns null and an error message on failure.</summary>
        public IBody2 Build(IReadOnlyList<PathPrimitive> prims, TubeProfile profile, out string error)
        {
            error = null;
            IBody2 result = null;
            double expected = 0;
            foreach (var p in prims)
            {
                if (p.Length < 1e-9) continue;
                IBody2 piece = p is StraightPrimitive s ? HollowCylinder(s, profile, out error)
                             : p is ArcPrimitive a ? HollowTorusSegment(a, profile, out error)
                             : null;
                if (piece == null) { error = error ?? "Unsupported centreline primitive."; return null; }
                expected += p.Length * AnnulusArea(profile);
                result = result == null ? piece : Union(result, piece, out error);
                if (result == null) return null;
            }
            if (result == null) { error = "The segment has zero length."; return null; }
            if (VerifyGeometry && !CheckVolume(result, expected, "tube segment", out error)) return null;
            return result;
        }

        public IBody2 Union(IBody2 target, IBody2 tool, out string error)
        {
            error = null;
            int err;
            var res = SwUtil.Arr<IBody2>((object)target.Operations2((int)swBodyOperationType_e.SWBODYADD, tool, out err));
            if (res.Length != 1)
            {
                error = $"Merging the tube segment failed (modeler error {err}, {res.Length} bodies). The segments may not touch.";
                return null;
            }
            return res[0];
        }

        // ------------------------------------------------------------------ straight

        private IBody2 HollowCylinder(StraightPrimitive s, TubeProfile p, out string error)
        {
            var outer = SolidCylinder(s.StartPoint, s.Direction, p.OuterRadius, s.Length, out error);
            if (outer == null) return null;
            // The inner tool is longer than the segment, so the cut never leaves coplanar slivers.
            double ext = Math.Max(1e-5, p.WallThickness);
            var inner = SolidCylinder(s.StartPoint - s.Direction * ext, s.Direction, p.InnerRadius, s.Length + 2 * ext, out error);
            if (inner == null) return null;
            int err;
            var res = SwUtil.Arr<IBody2>((object)outer.Operations2((int)swBodyOperationType_e.SWBODYCUT, inner, out err));
            if (res.Length != 1) { error = $"Could not create the bore of the straight segment (modeler error {err})."; return null; }
            return res[0];
        }

        /// <summary>
        /// IModeler.CreateBodyFromCyl. Assumed array layout: base centre (3), axis (3), radius, height.
        /// The centroid is checked; if the API treated the point as the mid-point, the body is created again
        /// with a corrected point.
        /// </summary>
        private IBody2 SolidCylinder(Vec3 basePoint, Vec3 dir, double r, double h, out string error)
        {
            error = null;
            Vec3 expectedCentroid = basePoint + dir * (h / 2);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                // Second attempt: the API used the point as the mid-point, so pass the mid-point.
                Vec3 pt = attempt == 0 ? basePoint : expectedCentroid;
                var arr = new[] { pt.X, pt.Y, pt.Z, dir.X, dir.Y, dir.Z, r, h };
                var body = _modeler.CreateBodyFromCyl(arr) as IBody2;
                if (body == null) { error = "IModeler.CreateBodyFromCyl returned no body."; return null; }
                if (!VerifyGeometry) return body;
                var mp = SwUtil.D((object)body.GetMassProperties(1.0));
                if (mp == null || mp.Length < 4) { error = "Could not read mass properties of a temporary body."; return null; }
                double vol = Math.PI * r * r * h;
                if (Math.Abs(mp[3] - vol) > vol * 1e-3) { error = Diag("cylinder volume", vol, mp[3]); return null; }
                Vec3 c = SwUtil.V(mp);
                if (c.AlmostEquals(expectedCentroid, Math.Max(1e-7, h * 1e-6))) return body;
                if (attempt == 0 && c.AlmostEquals(basePoint, Math.Max(1e-7, h * 1e-6))) continue; // mid-point semantics
                error = $"Cylinder centroid check failed: expected {expectedCentroid}, got {c}.";
                return null;
            }
            error = "Cylinder centroid check failed.";
            return null;
        }

        // ------------------------------------------------------------------ bend

        /// <summary>
        /// Torus segment: outer toroidal sheet (IModeler.CreateToroidalSurface + CreateSheetFromSurface), thickened
        /// inward by the wall thickness (IModeler.ThickenSheet). The surface parameter of the major angle and
        /// the thicken side are found by probing (ISurface.ReverseEvaluate, volume check), not assumed.
        /// </summary>
        private IBody2 HollowTorusSegment(ArcPrimitive a, TubeProfile p, out string error)
        {
            error = null;
            Vec3 refDir = (a.StartPoint - a.Center).Normalized();
            Vec3 axis = a.PlaneNormal;

            for (int flip = 0; flip < 2; flip++)
            {
                var surf = _modeler.CreateToroidalSurface(a.Center.ToArray(), axis.ToArray(), refDir.ToArray(), a.Radius, p.OuterRadius) as ISurface;
                if (surf == null) { error = "IModeler.CreateToroidalSurface returned no surface."; return null; }
                var pd = surf.Parameterization2();
                if (pd == null) { error = "Could not read the parameter range of the toroidal surface."; return null; }

                // Probe: outer points at major angle 0 and at a small angle, on the outside of the torus tube.
                Vec3 outward0 = refDir;
                Vec3 q0 = a.Center + outward0 * (a.Radius + p.OuterRadius);
                double small = Math.Min(0.1, a.Angle / 4);
                Vec3 outwardS = outward0.RotateAbout(a.PlaneNormal, small);
                Vec3 qs = a.Center + outwardS * (a.Radius + p.OuterRadius);
                var uv0 = SwUtil.D((object)surf.ReverseEvaluate(q0.X, q0.Y, q0.Z));
                var uvS = SwUtil.D((object)surf.ReverseEvaluate(qs.X, qs.Y, qs.Z));
                if (uv0 == null || uvS == null || uv0.Length < 2 || uvS.Length < 2) { error = "Could not evaluate the toroidal surface parameters."; return null; }

                double du = Angles.WrapPi(uvS[0] - uv0[0]), dv = Angles.WrapPi(uvS[1] - uv0[1]);
                bool majorIsU = Math.Abs(du) > Math.Abs(dv);
                double dMajor = majorIsU ? du : dv;
                if (Math.Abs(Math.Abs(dMajor) - small) > small * 0.05)
                {
                    // Not a plain angle parameter in either direction.
                    error = "Unexpected toroidal surface parameterisation.";
                    return null;
                }
                if (dMajor < 0)
                {
                    if (flip == 0) { axis = -axis; continue; } // the parameter runs the other way: build with the opposite axis
                    error = "Could not orient the toroidal surface.";
                    return null;
                }

                double m0 = majorIsU ? uv0[0] : uv0[1];
                double majMin = majorIsU ? pd.UMin : pd.VMin, majMax = majorIsU ? pd.UMax : pd.VMax;
                double minMin = majorIsU ? pd.VMin : pd.UMin, minMax = majorIsU ? pd.VMax : pd.UMax;
                // Keep the range inside the period when possible.
                if (m0 + a.Angle > majMax + 1e-9 && m0 - 2 * Math.PI >= majMin - 1e-9) m0 -= 2 * Math.PI;
                double u0 = majorIsU ? m0 : minMin, u1 = majorIsU ? m0 + a.Angle : minMax;
                double v0 = majorIsU ? minMin : m0, v1 = majorIsU ? minMax : m0 + a.Angle;
                // UvRange layout: (uMin, uMax, vMin, vMax) is assumed; (uMin, vMin, uMax, vMax) is tried second.
                // The volume check decides, so a wrong layout cannot give a wrong body.
                double expected = a.Length * AnnulusArea(p);
                foreach (var range in new[] { new[] { u0, u1, v0, v1 }, new[] { u0, v0, u1, v1 } })
                {
                    IBody2 sheet;
                    try { sheet = _modeler.CreateSheetFromSurface(surf, range) as IBody2; } catch { sheet = null; }
                    if (sheet == null) continue;
                    foreach (int side in new[] { (int)swThickenDirection_e.swThickenDirection_Side1, (int)swThickenDirection_e.swThickenDirection_Side2 })
                    {
                        IBody2 solid;
                        try { solid = _modeler.ThickenSheet((Body2)(sheet.Copy() as IBody2 ?? sheet), p.WallThickness, side); } catch { solid = null; }
                        if (solid == null) continue;
                        var mp = SwUtil.D((object)solid.GetMassProperties(1.0));
                        if (mp != null && mp.Length >= 4 && Math.Abs(mp[3] - expected) <= expected * 2e-3) return solid;
                    }
                }
                error = "Could not create the bend wall: no combination of sheet range and thicken side gave the exact volume " +
                        "(centreline length x annulus area). Report this with your SOLIDWORKS version.";
                return null;
            }
            error = "Could not create the bend body.";
            return null;
        }

        // ------------------------------------------------------------------ checks

        public bool CheckVolume(IBody2 body, double expected, string what, out string error)
        {
            error = null;
            var mp = SwUtil.D((object)body.GetMassProperties(1.0));
            if (mp == null || mp.Length < 4) { error = "Could not read mass properties of the " + what + "."; return false; }
            if (Math.Abs(mp[3] - expected) > expected * 2e-3) { error = Diag(what + " volume", expected, mp[3]); return false; }
            return true;
        }

        private static string Diag(string what, double expected, double actual) =>
            string.Format(CultureInfo.InvariantCulture,
                "Geometry check failed: {0} expected {1:G6} m³, modeler gave {2:G6} m³. The body was not used. " +
                "Report this with your SOLIDWORKS version. (The check can be switched off in %APPDATA%\\TubeWorks\\settings.xml, VerifyGeometry.)",
                what, expected, actual);

        /// <summary>
        /// Mark rings on the outside face of a straight tube body at the given axial positions (bend start and finish
        /// lines of the blank), by imprinting planar sheets (IModeler.ImprintingFaces). Returns the number of rings made.
        /// The body geometry does not change; only its outside face is split.
        /// </summary>
        public int ImprintRings(IBody2 body, Vec3 origin, Vec3 axis, IEnumerable<double> positions, double outerRadius)
        {
            var targets = SwUtil.Arr<IFace2>((object)body.GetFaces()).Where(f =>
            {
                var s = f.GetSurface() as ISurface;
                var cp = s != null && s.IsCylinder() ? SwUtil.D((object)s.CylinderParams) : null;
                return cp != null && cp.Length >= 7 && Math.Abs(cp[6] - outerRadius) <= Math.Max(1e-7, outerRadius * 1e-6);
            }).Cast<object>().ToArray();
            if (targets.Length == 0) return 0;
            var refDir = new TubeFrame(origin, axis).ReferenceX;
            double a = outerRadius * 3;
            var tools = new List<object>();
            foreach (double s in positions)
            {
                Vec3 root = origin + axis * s;
                var surf = _modeler.CreatePlanarSurface2(root.ToArray(), axis.ToArray(), refDir.ToArray()) as ISurface;
                if (surf == null) continue;
                IBody2 sheet = null;
                foreach (var range in new[] { new[] { -a, a, -a, a }, new[] { -a, -a, a, a } })
                {
                    try { sheet = _modeler.CreateSheetFromSurface(surf, range) as IBody2; } catch { sheet = null; }
                    if (sheet != null) break;
                }
                if (sheet != null) tools.AddRange(SwUtil.Arr<IFace2>((object)sheet.GetFaces()));
            }
            if (tools.Count == 0) return 0;
            try
            {
                bool ok = _modeler.ImprintingFaces(targets, tools.ToArray(), 0, out object te, out object _, out object _, out object _);
                return ok ? SwUtil.Arr<IEdge>(te).Length : 0;
            }
            catch { return 0; }
        }

        /// <summary>Wire body of the centreline, for preview only.</summary>
        public IBody2 Centreline(IReadOnlyList<PathPrimitive> prims)
        {
            var curves = new List<object>();
            foreach (var p in prims)
            {
                if (p.Length < 1e-9) continue;
                if (p is StraightPrimitive s)
                {
                    var line = _modeler.CreateLine(s.StartPoint.ToArray(), s.Direction.ToArray()) as ICurve;
                    var t = line?.CreateTrimmedCurve2(s.StartPoint.X, s.StartPoint.Y, s.StartPoint.Z, s.EndPoint.X, s.EndPoint.Y, s.EndPoint.Z) as ICurve;
                    if (t != null) curves.Add(t);
                }
                else if (p is ArcPrimitive a)
                {
                    // Split arcs larger than 180° so that the start and end points define the arc unambiguously.
                    int n = (int)Math.Ceiling(a.Angle / (Math.PI * 0.9));
                    for (int i = 0; i < n; i++)
                    {
                        Vec3 ps = a.PointAtAngle(a.Angle * i / n), pe = a.PointAtAngle(a.Angle * (i + 1) / n);
                        var arc = _modeler.CreateArc(a.Center.ToArray(), a.PlaneNormal.ToArray(), a.Radius, ps.ToArray(), pe.ToArray()) as ICurve;
                        var t = arc?.CreateTrimmedCurve2(ps.X, ps.Y, ps.Z, pe.X, pe.Y, pe.Z) as ICurve;
                        if (t != null) curves.Add(t);
                    }
                }
            }
            if (curves.Count == 0) return null;
            try { return _modeler.CreateWireBody(curves.ToArray(), (int)swCreateWireBodyOptions_e.swCreateWireBodyByDefault); }
            catch { return null; }
        }
    }
}
