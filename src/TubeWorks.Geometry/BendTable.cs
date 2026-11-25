using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TubeWorks.Geometry
{
    public enum RotationSign
    {
        /// <summary>Positive rotation = right-hand rule about the feed direction.</summary>
        RightHandAboutFeed = 0,
        /// <summary>Positive rotation = left-hand rule about the feed direction.</summary>
        LeftHandAboutFeed = 1,
    }

    public enum AngleConvention
    {
        /// <summary>Angle through which the tube is bent (0° = straight).</summary>
        BendAngle = 0,
        /// <summary>Included angle between the legs (180° = straight).</summary>
        IncludedAngle = 1,
    }

    public enum LengthUnit { Millimetre = 0, Inch = 1 }

    public sealed class BendTableOptions
    {
        public RotationSign RotationSign { get; set; } = RotationSign.RightHandAboutFeed;
        public AngleConvention AngleConvention { get; set; } = AngleConvention.BendAngle;
        public LengthUnit Unit { get; set; } = LengthUnit.Millimetre;
        public int Decimals { get; set; } = 3;
    }

    /// <summary>One row of the bend table. Lengths in metres, angles in radians; formatting converts.</summary>
    public sealed class BendRow
    {
        public int Number { get; set; }
        public string FeatureId { get; set; }
        /// <summary>Straight centreline length before this bend (LRA "L").</summary>
        public double Feed { get; set; }
        public double Radius { get; set; }
        /// <summary>Bend angle (LRA "A"), always the bend angle; the angle convention is applied only when formatting.</summary>
        public double Angle { get; set; }
        /// <summary>Signed rotation of this bend plane relative to the previous bend plane, about the feed direction (LRA "R"). 0 for the first bend.</summary>
        public double Rotation { get; set; }
        public Vec3 Start { get; set; }
        public Vec3 Finish { get; set; }
        public Vec3 Centre { get; set; }
        /// <summary>Intersection of the tangent lines. Null at 180° and more.</summary>
        public Vec3? Vertex { get; set; }
        public double DevelopedBendLength => Radius * Angle;
        /// <summary>Developed length from the tube start to the bend finish.</summary>
        public double CumulativeLength { get; set; }
    }

    public sealed class BendTable
    {
        public List<BendRow> Rows { get; } = new List<BendRow>();
        /// <summary>Straight after the last bend.</summary>
        public double FinalStraight { get; set; }
        public double TotalStraight { get; set; }
        public double TotalBend { get; set; }
        public double TotalDevelopedLength => TotalStraight + TotalBend;
        public Vec3 StartPoint { get; set; }
        public Vec3 EndPoint { get; set; }

        /// <summary>
        /// Build the table from an ordered centreline.
        /// Developed length = sum of straight lengths + sum of (centreline radius x bend angle in radians).
        /// </summary>
        public static BendTable Build(IReadOnlyList<PathPrimitive> path)
        {
            var t = new BendTable();
            if (path.Count == 0) return t;
            t.StartPoint = path[0].StartPoint;
            t.EndPoint = path[path.Count - 1].EndPoint;

            double feed = 0, cumulative = 0;
            Vec3? prevNormal = null;
            int n = 0;
            foreach (var p in path)
            {
                if (p is StraightPrimitive s)
                {
                    feed += s.Length; cumulative += s.Length; t.TotalStraight += s.Length;
                }
                else if (p is ArcPrimitive a)
                {
                    double rot = 0;
                    if (prevNormal.HasValue)
                    {
                        Vec3 axis = a.StartTangent;
                        rot = Math.Atan2(axis.Dot(prevNormal.Value.Cross(a.PlaneNormal)), prevNormal.Value.Dot(a.PlaneNormal));
                    }
                    cumulative += a.Length; t.TotalBend += a.Length;
                    t.Rows.Add(new BendRow
                    {
                        Number = ++n, FeatureId = a.FeatureId, Feed = feed, Radius = a.Radius, Angle = a.Angle, Rotation = rot,
                        Start = a.StartPoint, Finish = a.EndPoint, Centre = a.Center, Vertex = a.TangentIntersection, CumulativeLength = cumulative,
                    });
                    prevNormal = a.PlaneNormal;
                    feed = 0;
                }
            }
            t.FinalStraight = feed;
            return t;
        }

        // ------------------------------------------------------------------ formatting

        private static double L(double metres, BendTableOptions o) => o.Unit == LengthUnit.Inch ? metres / 0.0254 : metres * 1000.0;
        private static string F(double v, int dec) => v.ToString("F" + dec, CultureInfo.InvariantCulture);
        private static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        public double FormatAngle(double bendAngle, BendTableOptions o) =>
            Angles.ToDeg(o.AngleConvention == AngleConvention.IncludedAngle ? Math.PI - bendAngle : bendAngle);

        public double FormatRotation(double rot, BendTableOptions o) => Angles.ToDeg(o.RotationSign == RotationSign.LeftHandAboutFeed ? -rot : rot);

        /// <summary>Full bend table as CSV (comma separated, invariant culture, "." decimal separator).</summary>
        public string ToCsv(BendTableOptions o, string title = null)
        {
            string u = o.Unit == LengthUnit.Inch ? "in" : "mm";
            int d = o.Decimals;
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title)) sb.AppendLine(Csv(title));
            sb.AppendLine($"Bend,Feed ({u}),CLR ({u}),{(o.AngleConvention == AngleConvention.IncludedAngle ? "Included angle" : "Bend angle")} (deg),Rotation (deg),Direction," +
                          $"Start X ({u}),Start Y ({u}),Start Z ({u}),Finish X ({u}),Finish Y ({u}),Finish Z ({u}),Developed bend ({u}),Cumulative developed ({u})");
            foreach (var r in Rows)
            {
                double rot = FormatRotation(r.Rotation, o);
                string dir = r.Number == 1 ? "reference" : Math.Abs(rot) < 1e-9 ? "none" : rot > 0 ? "positive" : "negative";
                sb.AppendLine(string.Join(",", r.Number.ToString(CultureInfo.InvariantCulture), F(L(r.Feed, o), d), F(L(r.Radius, o), d),
                    F(FormatAngle(r.Angle, o), d), F(rot, d), dir,
                    F(L(r.Start.X, o), d), F(L(r.Start.Y, o), d), F(L(r.Start.Z, o), d),
                    F(L(r.Finish.X, o), d), F(L(r.Finish.Y, o), d), F(L(r.Finish.Z, o), d),
                    F(L(r.DevelopedBendLength, o), d), F(L(r.CumulativeLength, o), d)));
            }
            sb.AppendLine($"Final straight,{F(L(FinalStraight, o), d)}");
            sb.AppendLine($"Total straight,{F(L(TotalStraight, o), d)}");
            sb.AppendLine($"Total bend arc,{F(L(TotalBend, o), d)}");
            sb.AppendLine($"Total developed length,{F(L(TotalDevelopedLength, o), d)}");
            sb.AppendLine("Rotation sign," + (o.RotationSign == RotationSign.RightHandAboutFeed ? "right-hand about feed direction" : "left-hand about feed direction"));
            sb.AppendLine("Length basis,nominal centreline (no stretch or springback compensation applied)");
            return sb.ToString();
        }

        /// <summary>XYZ data: tube start, tangent-line intersections (vertices), tube end, with the radius at each vertex.</summary>
        public string ToXyzCsv(BendTableOptions o)
        {
            string u = o.Unit == LengthUnit.Inch ? "in" : "mm";
            int d = o.Decimals;
            var sb = new StringBuilder();
            sb.AppendLine($"Point,X ({u}),Y ({u}),Z ({u}),CLR ({u})");
            sb.AppendLine($"Start,{F(L(StartPoint.X, o), d)},{F(L(StartPoint.Y, o), d)},{F(L(StartPoint.Z, o), d)},");
            foreach (var r in Rows)
            {
                if (r.Vertex.HasValue)
                {
                    var v = r.Vertex.Value;
                    sb.AppendLine($"V{r.Number},{F(L(v.X, o), d)},{F(L(v.Y, o), d)},{F(L(v.Z, o), d)},{F(L(r.Radius, o), d)}");
                }
                else sb.AppendLine($"V{r.Number},n/a (bend of 180° or more has no tangent intersection),,,{F(L(r.Radius, o), d)}");
            }
            sb.AppendLine($"End,{F(L(EndPoint.X, o), d)},{F(L(EndPoint.Y, o), d)},{F(L(EndPoint.Z, o), d)},");
            return sb.ToString();
        }

        /// <summary>LRA data (feed, rotation, angle), one line per bend, then the final feed.</summary>
        public string ToLraCsv(BendTableOptions o)
        {
            string u = o.Unit == LengthUnit.Inch ? "in" : "mm";
            int d = o.Decimals;
            var sb = new StringBuilder();
            sb.AppendLine($"Step,L ({u}),R (deg),A (deg),CLR ({u})");
            foreach (var r in Rows)
                sb.AppendLine($"{r.Number},{F(L(r.Feed, o), d)},{F(FormatRotation(r.Rotation, o), d)},{F(FormatAngle(r.Angle, o), d)},{F(L(r.Radius, o), d)}");
            sb.AppendLine($"End,{F(L(FinalStraight, o), d)},,,");
            return sb.ToString();
        }
    }
}
