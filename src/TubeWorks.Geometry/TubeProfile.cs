using System;

namespace TubeWorks.Geometry
{
    public enum ProfileShape
    {
        /// <summary>Circular hollow section. The only shape implemented in release 1.</summary>
        Circular = 0,
        // Reserved for later releases. The engine rejects these values today.
        Square = 1,
        Rectangular = 2,
        Elliptical = 3,
    }

    /// <summary>Tube cross section. Values in metres.</summary>
    public sealed class TubeProfile
    {
        public ProfileShape Shape { get; }
        public double OuterDiameter { get; }
        public double WallThickness { get; }
        public double InnerDiameter => OuterDiameter - 2 * WallThickness;
        public double OuterRadius => OuterDiameter / 2;
        public double InnerRadius => InnerDiameter / 2;
        /// <summary>Mean diameter (mid-wall). Used for the nominal neutral surface.</summary>
        public double MeanDiameter => OuterDiameter - WallThickness;

        private TubeProfile(ProfileShape shape, double od, double wall)
        {
            Shape = shape;
            OuterDiameter = od;
            WallThickness = wall;
        }

        public static TubeProfile FromOdWall(double od, double wall)
        {
            var p = new TubeProfile(ProfileShape.Circular, od, wall);
            string err = p.Validate();
            if (err != null) throw new ArgumentException(err);
            return p;
        }

        public static TubeProfile FromOdId(double od, double id) => FromOdWall(od, (od - id) / 2);

        /// <summary>Returns null when valid, else a message.</summary>
        public string Validate()
        {
            if (Shape != ProfileShape.Circular) return "Only circular hollow tube is supported in this release.";
            if (!(OuterDiameter > 0)) return "Outside diameter must be greater than zero.";
            if (!(WallThickness > 0)) return "Wall thickness must be greater than zero.";
            if (!(WallThickness < OuterDiameter / 2)) return "Wall thickness must be less than half the outside diameter.";
            return null;
        }

        public bool SameAs(TubeProfile other, double tol) =>
            other != null && Math.Abs(OuterDiameter - other.OuterDiameter) <= tol && Math.Abs(WallThickness - other.WallThickness) <= tol;
    }
}
