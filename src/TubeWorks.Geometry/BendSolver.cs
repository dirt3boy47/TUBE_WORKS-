using System;
using System.Collections.Generic;

namespace TubeWorks.Geometry
{
    /// <summary>Result of solving bend parameters from a target. When <see cref="Success"/> is false,
    /// <see cref="Conflict"/> explains why and <see cref="Alternatives"/> lists valid options.</summary>
    public sealed class BendSolution
    {
        public bool Success { get; internal set; }
        public double Radius { get; internal set; }
        public double Angle { get; internal set; }
        public double PlaneRotation { get; internal set; }
        public double StraightAfter { get; internal set; }
        public string Conflict { get; internal set; }
        public List<BendAlternative> Alternatives { get; } = new List<BendAlternative>();
    }

    public sealed class BendAlternative
    {
        public string Description { get; set; }
        /// <summary>Radius to use with this alternative, when it changes the radius.</summary>
        public double? Radius { get; set; }
        public bool NeedsSecondBend { get; set; }
    }

    /// <summary>
    /// Constrained solver for one circular bend, followed by an optional straight, from a fixed start
    /// frame (position + tangent). The start tangent is never changed, so tangent continuity always holds.
    /// </summary>
    public static class BendSolver
    {
        public const double LengthTol = 1e-9;

        /// <summary>
        /// Solve angle, plane rotation and straight length so that the tube centreline ends at <paramref name="target"/>,
        /// with a fixed centreline radius.
        /// Model: Q - C = R*(sin a, -cos a) + L*(cos a, sin a) in the bend plane basis (T0, D), with L &gt;= 0.
        /// A solution exists only when |Q - C| &gt;= R.
        /// </summary>
        public static BendSolution SolveForEndpoint(TubeFrame start, double radius, Vec3 target)
        {
            var sol = new BendSolution { Radius = radius };
            if (!(radius > 0)) { sol.Conflict = "Bend radius must be greater than zero."; return sol; }

            Vec3 d = target - start.Origin;
            double a = d.Dot(start.Tangent);            // along the tube axis
            Vec3 lateral = d - start.Tangent * a;
            double b = lateral.Length;                  // distance from the axis

            if (b < LengthTol)
            {
                if (a > LengthTol)
                {
                    sol.Conflict = "The target point is on the tube axis. A bend cannot reach it.";
                    sol.Alternatives.Add(new BendAlternative { Description = $"Use Straight Extension with length {a * 1000:0.###} mm." });
                }
                else
                {
                    sol.Conflict = "The target point is behind the end face on the tube axis. One bend cannot reach it.";
                    sol.Alternatives.Add(new BendAlternative { Description = "Use two bends (for example two 90° bends in the same plane).", NeedsSecondBend = true });
                }
                return sol;
            }

            Vec3 dir = lateral / b;
            double wx = a, wy = b - radius;              // Q - C in (T0, D) basis
            double dist2 = wx * wx + wy * wy;
            double r2 = radius * radius;

            if (dist2 < r2 - 1e-12)
            {
                double rMax = (a * a + b * b) / (2 * b);
                sol.Conflict = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The target point is inside the bend circle of radius {0:0.###} mm. One bend with this radius cannot reach it.", radius * 1000);
                sol.Alternatives.Add(new BendAlternative
                {
                    Description = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Reduce the centreline radius to {0:0.###} mm or less (at exactly {0:0.###} mm the bend ends at the point with no straight).", rMax * 1000),
                    Radius = rMax,
                });
                sol.Alternatives.Add(new BendAlternative { Description = "Use a bend followed by a second bend.", NeedsSecondBend = true });
                return sol;
            }

            double len = Math.Sqrt(Math.Max(0, dist2 - r2));
            double angle = Angles.Wrap2Pi(Math.Atan2(wy, wx) - Math.Atan2(-radius, len));
            sol.Success = true;
            sol.Angle = angle;
            sol.StraightAfter = len < LengthTol ? 0 : len;
            sol.PlaneRotation = BendMath.PlaneRotationFor(start, dir);
            return sol;
        }

        /// <summary>Solve angle and plane rotation so that the end tangent equals <paramref name="direction"/>.</summary>
        public static BendSolution SolveForDirection(TubeFrame start, double radius, Vec3 direction, double currentPlaneRotation, double straightAfter)
        {
            var sol = new BendSolution { Radius = radius, StraightAfter = straightAfter };
            if (direction.IsZero()) { sol.Conflict = "The direction vector is zero."; return sol; }
            Vec3 t1 = direction.Normalized();
            double angle = start.Tangent.AngleTo(t1);
            if (angle < 1e-9)
            {
                sol.Conflict = "The direction equals the current tube axis. No bend is necessary.";
                sol.Alternatives.Add(new BendAlternative { Description = "Use Straight Extension." });
                return sol;
            }
            if (angle > Math.PI - 1e-9)
            {
                // 180 degrees: any plane is valid. Keep the operator's current plane rotation.
                sol.Success = true;
                sol.Angle = Math.PI;
                sol.PlaneRotation = currentPlaneRotation;
                return sol;
            }
            Vec3 d = (t1 - start.Tangent * t1.Dot(start.Tangent)).Normalized();
            sol.Success = true;
            sol.Angle = angle;
            sol.PlaneRotation = BendMath.PlaneRotationFor(start, d);
            return sol;
        }

        /// <summary>
        /// Plane rotation that puts the bend in a given plane (identified by its normal).
        /// The tube axis must lie in that plane. Returns null when it does not (within 0.01 degrees).
        /// The two possible directions differ by 180 degrees; the one closest to <paramref name="currentPlaneRotation"/> is returned.
        /// </summary>
        public static double? PlaneRotationForPlane(TubeFrame start, Vec3 planeNormal, double currentPlaneRotation)
        {
            Vec3 n = planeNormal.Normalized();
            if (Math.Abs(n.Dot(start.Tangent)) > Math.Sin(Angles.ToRad(0.01))) return null;
            Vec3 d = n.Cross(start.Tangent).Normalized();
            double r1 = BendMath.PlaneRotationFor(start, d);
            double r2 = Angles.WrapPi(r1 + Math.PI);
            return Math.Abs(Angles.WrapPi(r1 - currentPlaneRotation)) <= Math.Abs(Angles.WrapPi(r2 - currentPlaneRotation)) ? r1 : r2;
        }
    }
}
