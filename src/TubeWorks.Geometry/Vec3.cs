using System;
using System.Globalization;

namespace TubeWorks.Geometry
{
    /// <summary>Immutable 3D vector. All lengths are in metres (SOLIDWORKS API internal units).</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double DistanceTo(Vec3 b) => (this - b).Length;

        public Vec3 Normalized()
        {
            double l = Length;
            if (l < 1e-15) throw new InvalidOperationException("Cannot normalise a zero-length vector.");
            return this / l;
        }

        public bool IsZero(double tol = 1e-12) => Length <= tol;

        /// <summary>Unsigned angle to another vector, radians, in [0, pi].</summary>
        public double AngleTo(Vec3 b)
        {
            double c = Dot(b) / (Length * b.Length);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c)));
        }

        /// <summary>Rotate this vector about a unit axis by angle (right-hand rule, Rodrigues).</summary>
        public Vec3 RotateAbout(Vec3 unitAxis, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return this * c + unitAxis.Cross(this) * s + unitAxis * (unitAxis.Dot(this) * (1 - c));
        }

        public double[] ToArray() => new[] { X, Y, Z };
        public static Vec3 FromArray(double[] a, int offset = 0) => new Vec3(a[offset], a[offset + 1], a[offset + 2]);

        public bool AlmostEquals(Vec3 b, double tol) => DistanceTo(b) <= tol;
        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######}, {2:0.######})", X, Y, Z);
    }
}
