using System;

namespace TubeWorks.Geometry
{
    /// <summary>
    /// A tube end: the centre point of the end face and the unit tangent that points out of the tube
    /// (the direction in which the next segment continues).
    /// </summary>
    public readonly struct TubeFrame
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Tangent;

        public TubeFrame(Vec3 origin, Vec3 tangent)
        {
            Origin = origin;
            Tangent = tangent.Normalized();
        }

        public TubeFrame Reversed() => new TubeFrame(Origin, -Tangent);

        /// <summary>
        /// Reference direction (local X) that is used to measure bend plane rotation.
        /// Convention: the projection of global +Y onto the end-face plane. When the tube axis is
        /// within 0.1 degrees of global Y, global +Z is projected instead. The convention depends only
        /// on the tangent, so it is deterministic on every rebuild.
        /// </summary>
        public Vec3 ReferenceX
        {
            get
            {
                Vec3 up = Math.Abs(Tangent.Dot(Vec3.UnitY)) > Math.Cos(0.1 * Math.PI / 180.0) ? Vec3.UnitZ : Vec3.UnitY;
                return (up - Tangent * up.Dot(Tangent)).Normalized();
            }
        }

        /// <summary>Local Y = Tangent x ReferenceX. Local Z = Tangent.</summary>
        public Vec3 ReferenceY => Tangent.Cross(ReferenceX);

        /// <summary>Convert a point given in this frame's local coordinates (X = ReferenceX, Y = ReferenceY, Z = Tangent) to global.</summary>
        public Vec3 LocalToGlobal(Vec3 local) => Origin + ReferenceX * local.X + ReferenceY * local.Y + Tangent * local.Z;

        public Vec3 GlobalToLocal(Vec3 global)
        {
            Vec3 d = global - Origin;
            return new Vec3(d.Dot(ReferenceX), d.Dot(ReferenceY), d.Dot(Tangent));
        }

        public override string ToString() => $"O={Origin} T={Tangent}";
    }
}
