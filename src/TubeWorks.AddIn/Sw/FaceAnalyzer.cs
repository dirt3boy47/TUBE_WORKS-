using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>The end of a circular hollow tube, detected from a planar annular face.</summary>
    internal sealed class TubeEnd
    {
        public Vec3 Centre;
        /// <summary>Unit normal that points out of the material (the direction in which the tube can continue).</summary>
        public Vec3 OutwardNormal;
        public double OuterDiameter;
        public double InnerDiameter;
        public double WallThickness => (OuterDiameter - InnerDiameter) / 2;
        public IBody2 Body;
        public TubeFrame Frame => new TubeFrame(Centre, OutwardNormal);
        public TubeProfile Profile => TubeProfile.FromOdId(OuterDiameter, InnerDiameter);
    }

    internal static class FaceAnalyzer
    {
        /// <summary>Recognise a tube end face. Returns null and a message when the face is not a compatible end.</summary>
        public static TubeEnd Analyze(IFace2 face, out string error)
        {
            error = null;
            if (face == null) { error = "No face is selected."; return null; }
            var surf = face.GetSurface() as ISurface;
            if (surf == null || !surf.IsPlane()) { error = "Select the flat end face of a tube."; return null; }

            var edges = SwUtil.Arr<IEdge>((object)face.GetEdges());
            var circles = edges.Select(e => e.GetCurve() as ICurve).ToList();
            if (circles.Count != 2 || circles.Any(c => c == null || !c.IsCircle()))
            {
                error = circles.Count == 1
                    ? "This end face has only one circular edge (a solid bar). Select the end of a hollow tube."
                    : "The end face must be an annulus bounded by two circles. Only circular hollow tube is supported in this release.";
                return null;
            }

            // ICurve.CircleParams: centre (3), axis (3), radius (1). The centre and radius are checked below
            // against a point evaluated on the curve, so a wrong assumption fails safely.
            var p0 = SwUtil.D((object)circles[0].CircleParams);
            var p1 = SwUtil.D((object)circles[1].CircleParams);
            if (p0 == null || p1 == null || p0.Length < 7 || p1.Length < 7) { error = "Could not read the circle geometry of the end face."; return null; }
            Vec3 c0 = SwUtil.V(p0), c1 = SwUtil.V(p1);
            double r0 = p0[6], r1 = p1[6];
            if (!CheckCircle(circles[0], c0, r0) || !CheckCircle(circles[1], c1, r1)) { error = "Circle geometry check failed (unexpected CircleParams layout)."; return null; }
            double tol = Math.Max(1e-7, 1e-6 * Math.Max(r0, r1));
            if (!c0.AlmostEquals(c1, tol)) { error = "The inside and outside circles of the end face are not concentric."; return null; }

            var n = SwUtil.D((object)face.Normal);
            if (n == null) { error = "Could not read the face normal."; return null; }
            Vec3 normal = SwUtil.V(n).Normalized();

            // Make sure the normal points out of the material. The outer wall continues tangentially behind the end
            // face, so a point on the outer radius just behind the face lies on the wall, and one just in front does not.
            double ro = Math.Max(r0, r1);
            var outerEdge = r0 > r1 ? edges[0] : edges[1];
            var wall = SwUtil.Arr<IFace2>((object)outerEdge.GetTwoAdjacentFaces2()).FirstOrDefault(f => !IsPlanar(f));
            if (wall != null)
            {
                Vec3 radial = (Math.Abs(normal.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY).Cross(normal).Normalized() * ro;
                double delta = 0.1 * ro;
                double dFront = DistanceToFace(wall, c0 + radial + normal * delta);
                double dBack = DistanceToFace(wall, c0 + radial - normal * delta);
                if (dFront < dBack) normal = -normal;
            }

            return new TubeEnd
            {
                Centre = c0,
                OutwardNormal = normal,
                OuterDiameter = 2 * Math.Max(r0, r1),
                InnerDiameter = 2 * Math.Min(r0, r1),
                Body = face.GetBody() as IBody2,
            };
        }

        private static bool IsPlanar(IFace2 f) => (f.GetSurface() as ISurface)?.IsPlane() == true;

        private static double DistanceToFace(IFace2 f, Vec3 p)
        {
            var c = SwUtil.D((object)f.GetClosestPointOn(p.X, p.Y, p.Z));
            return c == null || c.Length < 3 ? double.MaxValue : SwUtil.V(c).DistanceTo(p);
        }

        private static bool CheckCircle(ICurve c, Vec3 centre, double radius)
        {
            try
            {
                if (!c.GetEndParams(out double t0, out double t1, out bool _, out bool _)) return true;
                var e = SwUtil.D((object)c.Evaluate2((t0 + t1) / 2, 0));
                if (e == null || e.Length < 3) return true;
                return Math.Abs(SwUtil.V(e).DistanceTo(centre) - radius) <= Math.Max(1e-7, radius * 1e-5);
            }
            catch { return true; }
        }
    }
}
