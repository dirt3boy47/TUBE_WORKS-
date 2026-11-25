using System;
using System.Collections.Generic;

namespace TubeWorks.Geometry
{
    /// <summary>One piece of the tube centreline: a straight line or a circular arc.</summary>
    public abstract class PathPrimitive
    {
        /// <summary>Id of the TubeWorks feature that owns this primitive.</summary>
        public string FeatureId { get; set; }
        public abstract Vec3 StartPoint { get; }
        public abstract Vec3 EndPoint { get; }
        public abstract Vec3 StartTangent { get; }
        public abstract Vec3 EndTangent { get; }
        /// <summary>Centreline length, metres.</summary>
        public abstract double Length { get; }
        public abstract Vec3 PointAt(double s);
        public abstract PathPrimitive Reversed();

        /// <summary>Sample points with spacing not larger than maxStep. Includes both ends.</summary>
        public IEnumerable<(Vec3 point, double s)> Sample(double maxStep)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Length / maxStep));
            for (int i = 0; i <= n; i++)
            {
                double s = Length * i / n;
                yield return (PointAt(s), s);
            }
        }
    }

    public sealed class StraightPrimitive : PathPrimitive
    {
        private readonly Vec3 _start, _dir;
        private readonly double _len;

        public StraightPrimitive(Vec3 start, Vec3 unitDir, double length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Straight length cannot be negative.");
            _start = start; _dir = unitDir.Normalized(); _len = length;
        }

        public Vec3 Direction => _dir;
        public override Vec3 StartPoint => _start;
        public override Vec3 EndPoint => _start + _dir * _len;
        public override Vec3 StartTangent => _dir;
        public override Vec3 EndTangent => _dir;
        public override double Length => _len;
        public override Vec3 PointAt(double s) => _start + _dir * s;
        public override PathPrimitive Reversed() => new StraightPrimitive(EndPoint, -_dir, _len) { FeatureId = FeatureId };
    }

    /// <summary>
    /// Circular arc of the centreline. The tangent rotates about <see cref="PlaneNormal"/> by <see cref="Angle"/>
    /// (right-hand rule). <see cref="BendDirection"/> is the unit vector from the start point toward the centre.
    /// </summary>
    public sealed class ArcPrimitive : PathPrimitive
    {
        public Vec3 Center { get; }
        public double Radius { get; }
        public double Angle { get; }
        public Vec3 PlaneNormal { get; }
        public Vec3 BendDirection { get; }
        private readonly Vec3 _start, _t0;

        public ArcPrimitive(Vec3 start, Vec3 startTangent, Vec3 bendDirection, double radius, double angle)
        {
            if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius));
            _start = start;
            _t0 = startTangent.Normalized();
            // Remove any small component along the tangent, so the arc stays exact.
            BendDirection = (bendDirection - _t0 * bendDirection.Dot(_t0)).Normalized();
            Radius = radius;
            Angle = angle;
            Center = start + BendDirection * radius;
            PlaneNormal = _t0.Cross(BendDirection);
        }

        public override Vec3 StartPoint => _start;
        public override Vec3 StartTangent => _t0;
        public override Vec3 EndPoint => PointAtAngle(Angle);
        public override Vec3 EndTangent => TangentAtAngle(Angle);
        public override double Length => Radius * Angle;
        public override Vec3 PointAt(double s) => PointAtAngle(s / Radius);

        public Vec3 PointAtAngle(double a) => Center + (BendDirection * -Math.Cos(a) + _t0 * Math.Sin(a)) * Radius;
        public Vec3 TangentAtAngle(double a) => _t0 * Math.Cos(a) + BendDirection * Math.Sin(a);

        /// <summary>Intersection of the start and end tangent lines (the XYZ "vertex"). Undefined at 180 degrees and more.</summary>
        public Vec3? TangentIntersection
        {
            get
            {
                if (Angle >= Math.PI - 1e-9) return null;
                return _start + _t0 * (Radius * Math.Tan(Angle / 2));
            }
        }

        public override PathPrimitive Reversed()
        {
            // Travelling backwards: start at the end point, tangent = -end tangent; the centre is unchanged.
            Vec3 s = EndPoint;
            Vec3 dir = (Center - s).Normalized();
            return new ArcPrimitive(s, -EndTangent, dir, Radius, Angle) { FeatureId = FeatureId };
        }
    }
}
