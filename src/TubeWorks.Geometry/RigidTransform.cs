using System;

namespace TubeWorks.Geometry
{
    /// <summary>Rigid motion p' = R·p + t. R is stored as its three columns (images of X, Y, Z).</summary>
    public readonly struct RigidTransform
    {
        public readonly Vec3 C0, C1, C2, T;

        public RigidTransform(Vec3 c0, Vec3 c1, Vec3 c2, Vec3 t) { C0 = c0; C1 = c1; C2 = c2; T = t; }

        public static readonly RigidTransform Identity = new RigidTransform(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);

        public Vec3 ApplyDir(Vec3 v) => C0 * v.X + C1 * v.Y + C2 * v.Z;
        public Vec3 Apply(Vec3 p) => ApplyDir(p) + T;

        /// <summary>this ∘ other: first <paramref name="other"/>, then this.</summary>
        public RigidTransform Compose(RigidTransform other) =>
            new RigidTransform(ApplyDir(other.C0), ApplyDir(other.C1), ApplyDir(other.C2), Apply(other.T));

        public RigidTransform Inverse()
        {
            // R^-1 = R^T. Rows of R are the columns of R^T.
            var r0 = new Vec3(C0.X, C1.X, C2.X);
            var r1 = new Vec3(C0.Y, C1.Y, C2.Y);
            var r2 = new Vec3(C0.Z, C1.Z, C2.Z);
            var inv = new RigidTransform(r0, r1, r2, Vec3.Zero);
            return new RigidTransform(r0, r1, r2, -inv.ApplyDir(T));
        }

        /// <summary>Rotation by <paramref name="angle"/> about the line through <paramref name="point"/> along <paramref name="unitAxis"/>.</summary>
        public static RigidTransform Rotation(Vec3 point, Vec3 unitAxis, double angle)
        {
            var c0 = Vec3.UnitX.RotateAbout(unitAxis, angle);
            var c1 = Vec3.UnitY.RotateAbout(unitAxis, angle);
            var c2 = Vec3.UnitZ.RotateAbout(unitAxis, angle);
            var r = new RigidTransform(c0, c1, c2, Vec3.Zero);
            return new RigidTransform(c0, c1, c2, point - r.ApplyDir(point));
        }

        public static RigidTransform Translation(Vec3 t) => new RigidTransform(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, t);

        /// <summary>Map frame (origin a, tangent ta, roll reference ra) onto frame (b, tb, rb).</summary>
        public static RigidTransform FrameToFrame(Vec3 a, Vec3 ta, Vec3 ra, Vec3 b, Vec3 tb, Vec3 rb)
        {
            Vec3 az = ta.Normalized(), ax = (ra - az * ra.Dot(az)).Normalized(), ay = az.Cross(ax);
            Vec3 bz = tb.Normalized(), bx = (rb - bz * rb.Dot(bz)).Normalized(), by = bz.Cross(bx);
            // R = B · A^T
            Vec3 Col(Vec3 e) => bx * (ax.Dot(e)) + by * (ay.Dot(e)) + bz * (az.Dot(e));
            var r = new RigidTransform(Col(Vec3.UnitX), Col(Vec3.UnitY), Col(Vec3.UnitZ), Vec3.Zero);
            return new RigidTransform(r.C0, r.C1, r.C2, b - r.ApplyDir(a));
        }

        /// <summary>SOLIDWORKS MathTransform array candidate: 9 rotation values, 3 translation, scale, 3 unused.
        /// <paramref name="transposed"/> chooses the order of the 9 rotation values; the add-in checks which one SOLIDWORKS uses.</summary>
        public double[] ToSwArray(bool transposed)
        {
            var a = new double[16];
            Vec3[] cols = { C0, C1, C2 };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    // element (row i, col j) of R
                    double rij = j == 0 ? Get(cols[0], i) : j == 1 ? Get(cols[1], i) : Get(cols[2], i);
                    a[transposed ? j * 3 + i : i * 3 + j] = rij;
                }
            a[9] = T.X; a[10] = T.Y; a[11] = T.Z; a[12] = 1.0;
            return a;
        }

        private static double Get(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

        public bool AlmostEquals(RigidTransform o, double tol) =>
            C0.AlmostEquals(o.C0, tol) && C1.AlmostEquals(o.C1, tol) && C2.AlmostEquals(o.C2, tol) && T.AlmostEquals(o.T, tol);
    }
}
