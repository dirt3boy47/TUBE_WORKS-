using SolidWorks.Interop.sldworks;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>Reads points, directions and planes from selected reference geometry (captured values, not associative).</summary>
    internal static class RefGeometry
    {
        public static bool TryPlane(object obj, out Vec3 point, out Vec3 normal)
        {
            point = Vec3.Zero; normal = Vec3.Zero;
            if (obj is IFace2 f && (f.GetSurface() as ISurface)?.IsPlane() == true)
            {
                var n = SwUtil.D((object)f.Normal);
                var p = SwUtil.D((object)f.GetClosestPointOn(0, 0, 0));
                if (n == null || p == null || p.Length < 3) return false;
                normal = SwUtil.V(n).Normalized(); point = SwUtil.V(p);
                return true;
            }
            if (obj is IFeature feat && feat.GetSpecificFeature2() is IRefPlane rp)
            {
                // Four corner points of the plane display rectangle: the normal does not depend on their order.
                var c = SwUtil.D((object)rp.CornerPoints);
                if (c == null || c.Length < 9) return false;
                Vec3 p0 = SwUtil.V(c, 0), p1 = SwUtil.V(c, 3), p2 = SwUtil.V(c, 6);
                var n = (p1 - p0).Cross(p2 - p0);
                if (n.IsZero(1e-15)) return false;
                normal = n.Normalized(); point = p0;
                return true;
            }
            return false;
        }

        public static bool TryDirection(object obj, out Vec3 dir)
        {
            dir = Vec3.Zero;
            if (obj is IEdge e && e.GetCurve() is ICurve c && c.IsLine())
            {
                var lp = SwUtil.D((object)c.LineParams); // root point (3), direction (3)
                if (lp == null || lp.Length < 6) return false;
                dir = new Vec3(lp[3], lp[4], lp[5]);
                return !dir.IsZero();
            }
            if (obj is IFeature feat && feat.GetSpecificFeature2() is IRefAxis ra)
            {
                var ap = SwUtil.D((object)ra.GetRefAxisParams()); // two points
                if (ap == null || ap.Length < 6) return false;
                dir = new Vec3(ap[3] - ap[0], ap[4] - ap[1], ap[5] - ap[2]);
                return !dir.IsZero();
            }
            return false;
        }

        public static bool TryPoint(object obj, out Vec3 point)
        {
            point = Vec3.Zero;
            if (obj is IVertex v) { var p = SwUtil.D((object)v.GetPoint()); if (p == null) return false; point = SwUtil.V(p); return true; }
            if (obj is IFeature feat && feat.GetSpecificFeature2() is IRefPoint rp)
            {
                var p = SwUtil.D((object)rp.GetRefPoint()?.ArrayData);
                if (p == null) return false;
                point = SwUtil.V(p);
                return true;
            }
            return false;
        }
    }
}
