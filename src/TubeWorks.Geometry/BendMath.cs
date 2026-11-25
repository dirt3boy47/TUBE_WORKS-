using System;
using System.Collections.Generic;

namespace TubeWorks.Geometry
{
    public static class Angles
    {
        public static double ToRad(double deg) => deg * Math.PI / 180.0;
        public static double ToDeg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Normalise to [0, 2*pi).</summary>
        public static double Wrap2Pi(double a)
        {
            double r = a % (2 * Math.PI);
            return r < 0 ? r + 2 * Math.PI : r;
        }

        /// <summary>Normalise to (-pi, pi].</summary>
        public static double WrapPi(double a)
        {
            double r = Wrap2Pi(a);
            return r > Math.PI ? r - 2 * Math.PI : r;
        }
    }

    /// <summary>Exact nominal geometry of one bend operation.</summary>
    public static class BendMath
    {
        /// <summary>
        /// Unit vector from the start point toward the bend centre.
        /// D = cos(phi) * RefX + sin(phi) * RefY, where phi = plane rotation (+ pi when reversed).
        /// </summary>
        public static Vec3 BendDirection(TubeFrame start, double planeRotation, bool reverse)
        {
            double phi = planeRotation + (reverse ? Math.PI : 0.0);
            return start.ReferenceX * Math.Cos(phi) + start.ReferenceY * Math.Sin(phi);
        }

        /// <summary>Plane rotation (radians, in (-pi, pi]) that produces the given bend direction.</summary>
        public static double PlaneRotationFor(TubeFrame start, Vec3 bendDirection)
        {
            Vec3 d = bendDirection - start.Tangent * bendDirection.Dot(start.Tangent);
            return Math.Atan2(d.Dot(start.ReferenceY), d.Dot(start.ReferenceX));
        }

        /// <summary>The centreline primitives of one Face Bend: the arc, then an optional straight.</summary>
        public static List<PathPrimitive> BuildBend(TubeFrame start, double radius, double angle, double planeRotation, bool reverse, double straightAfter)
        {
            var arc = new ArcPrimitive(start.Origin, start.Tangent, BendDirection(start, planeRotation, reverse), radius, angle);
            var list = new List<PathPrimitive> { arc };
            if (straightAfter > 0) list.Add(new StraightPrimitive(arc.EndPoint, arc.EndTangent, straightAfter));
            return list;
        }
    }
}
