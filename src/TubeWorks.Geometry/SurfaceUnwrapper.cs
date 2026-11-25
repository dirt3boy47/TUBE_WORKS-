using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TubeWorks.Geometry
{
    public enum PatternLayer { OuterProfile, InternalCutouts, BendLines, Centreline, Engraving, Construction, Text }

    public struct Point2 { public double X, Y; public Point2(double x, double y) { X = x; Y = y; } public override string ToString() => $"({X:0.######}, {Y:0.######})"; }

    public sealed class PatternPolyline { public PatternLayer Layer; public List<Point2> Points = new List<Point2>(); public bool Closed; }
    public sealed class PatternText { public PatternLayer Layer; public Point2 At; public double Height; public string Text; public double RotationDeg; }

    /// <summary>2D manufacturing pattern. Coordinates in metres: X along the developed tube, Y around the circumference.</summary>
    public sealed class Pattern2D
    {
        public List<PatternPolyline> Polylines { get; } = new List<PatternPolyline>();
        public List<PatternText> Texts { get; } = new List<PatternText>();
        public List<string> Warnings { get; } = new List<string>();
        public double Length { get; set; }
        public double Width { get; set; }

        public void Line(PatternLayer layer, double x0, double y0, double x1, double y1) =>
            Polylines.Add(new PatternPolyline { Layer = layer, Points = { new Point2(x0, y0), new Point2(x1, y1) } });
    }

    public enum UnwrapSurface { Outside = 0, Inside = 1, Neutral = 2 }

    /// <summary>Bend marking input, in developed (straightened) coordinates.</summary>
    public sealed class BendMark
    {
        public int Number;
        public double StartS, EndS;
        public double Radius, Angle, Rotation;
        /// <summary>Angle around the tube (from the seam reference, before the seam offset) of the inside of the bend.</summary>
        public double InsideAngle;
    }

    /// <summary>
    /// Develops the cylindrical surface of a STRAIGHT tube into a rectangle: x = axial position, y = r_ref · angle.
    /// For a circular cylinder this mapping is exact (the cylinder is a developable surface). The bent tube must be
    /// straightened first; whatever lay inside a bend is only approximately placed by that straightening.
    /// </summary>
    public sealed class SurfaceUnwrapper
    {
        public Vec3 Origin { get; }
        public Vec3 Axis { get; }
        public Vec3 RefX { get; }
        public Vec3 RefY { get; }
        public double SeamAngle { get; }
        public double RefRadius { get; }
        public double Length { get; }
        public double Width => 2 * Math.PI * RefRadius;

        public SurfaceUnwrapper(Vec3 origin, Vec3 axis, double length, double refRadius, double seamAngle)
        {
            if (!(refRadius > 0)) throw new ArgumentOutOfRangeException(nameof(refRadius));
            Origin = origin; Axis = axis.Normalized(); Length = length; RefRadius = refRadius; SeamAngle = seamAngle;
            var f = new TubeFrame(origin, Axis);
            RefX = f.ReferenceX; RefY = f.ReferenceY;
        }

        public static double ReferenceRadius(TubeProfile p, UnwrapSurface s) =>
            s == UnwrapSurface.Outside ? p.OuterRadius : s == UnwrapSurface.Inside ? p.InnerRadius : p.MeanDiameter / 2;

        /// <summary>Axial position and angle from the seam, angle in [0, 2π).</summary>
        public (double s, double angle) Map(Vec3 p)
        {
            Vec3 d = p - Origin;
            double s = d.Dot(Axis);
            double a = Math.Atan2(d.Dot(RefY), d.Dot(RefX)) - SeamAngle;
            return (s, Angles.Wrap2Pi(a));
        }

        /// <summary>
        /// Map a closed loop of 3D points on the tube surface. A loop that crosses the seam is split into pieces at
        /// y = 0 and y = W. Returns closed polylines (no seam crossing) or open pieces.
        /// Also returns the winding: 0 for a hole, ±1 for a loop that goes once around the tube (an end profile).
        /// </summary>
        public (List<PatternPolyline> pieces, int winding) MapLoop(IReadOnlyList<Vec3> loop, PatternLayer layer)
        {
            var pieces = new List<PatternPolyline>();
            if (loop.Count < 2) return (pieces, 0);
            // Continuous angle (no jumps larger than π between neighbours).
            var s = new double[loop.Count + 1];
            var a = new double[loop.Count + 1];
            for (int i = 0; i <= loop.Count; i++)
            {
                var (si, ai) = Map(loop[i % loop.Count]);
                s[i] = si;
                if (i == 0) a[i] = ai;
                else a[i] = a[i - 1] + Angles.WrapPi(ai - Angles.Wrap2Pi(a[i - 1]));
            }
            int winding = (int)Math.Round((a[loop.Count] - a[0]) / (2 * Math.PI));

            double minA = a.Min(), maxA = a.Max();
            if (winding == 0 && Math.Floor(minA / (2 * Math.PI)) == Math.Floor(maxA / (2 * Math.PI) - 1e-12))
            {
                double band = Math.Floor(minA / (2 * Math.PI)) * 2 * Math.PI;
                var pl = new PatternPolyline { Layer = layer, Closed = true };
                for (int i = 0; i < loop.Count; i++) pl.Points.Add(new Point2(s[i], RefRadius * (a[i] - band)));
                pieces.Add(pl);
                return (pieces, 0);
            }

            // Split at every multiple of 2π.
            PatternPolyline cur = null;
            double curBand = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                double a0 = a[i], a1 = a[i + 1], s0 = s[i], s1 = s[i + 1];
                double b0 = Math.Floor(a0 / (2 * Math.PI));
                if (cur == null) { cur = new PatternPolyline { Layer = layer }; curBand = b0; cur.Points.Add(new Point2(s0, RefRadius * (a0 - curBand * 2 * Math.PI))); }
                double b1 = Math.Floor(a1 / (2 * Math.PI));
                // Cross boundaries between a0 and a1 one by one.
                while (b1 != curBand)
                {
                    double boundary = (b1 > curBand ? curBand + 1 : curBand) * 2 * Math.PI;
                    double t = (boundary - a0) / (a1 - a0);
                    double sb = s0 + t * (s1 - s0);
                    cur.Points.Add(new Point2(sb, RefRadius * (boundary - curBand * 2 * Math.PI)));
                    pieces.Add(cur);
                    curBand += b1 > curBand ? 1 : -1;
                    cur = new PatternPolyline { Layer = layer };
                    cur.Points.Add(new Point2(sb, RefRadius * (boundary - curBand * 2 * Math.PI)));
                }
                cur.Points.Add(new Point2(s1, RefRadius * (a1 - curBand * 2 * Math.PI)));
            }
            if (cur != null && cur.Points.Count > 1) pieces.Add(cur);
            // Join the last piece to the first when they are the same band (the loop started mid-piece).
            if (pieces.Count > 1)
            {
                var first = pieces[0]; var last = pieces[pieces.Count - 1];
                var lp = last.Points[last.Points.Count - 1]; var fp = first.Points[0];
                if (Math.Abs(lp.X - fp.X) < 1e-12 && Math.Abs(lp.Y - fp.Y) < 1e-12)
                {
                    last.Points.AddRange(first.Points.Skip(1));
                    pieces.RemoveAt(0);
                }
            }
            return (pieces, winding);
        }

        /// <summary>
        /// Build the complete pattern. <paramref name="loops"/> are the boundary loops of the selected cylindrical face
        /// (3D points). Loops at the tube ends that are plain circles become straight end lines.
        /// </summary>
        public Pattern2D Build(IEnumerable<IReadOnlyList<Vec3>> loops, IEnumerable<BendMark> bends, string title)
        {
            var pat = new Pattern2D { Length = Length, Width = Width };
            double W = Width, tol = Math.Max(1e-7, Length * 1e-7);
            List<PatternPolyline> leftEnd = null, rightEnd = null;

            foreach (var loop in loops)
            {
                var (pieces, winding) = MapLoop(loop, PatternLayer.InternalCutouts);
                if (winding != 0)
                {
                    // Goes around the tube: an end profile.
                    var pts = pieces.SelectMany(p => p.Points).ToList();
                    double meanS = pts.Average(q => q.X);
                    bool constantS = pts.All(q => Math.Abs(q.X - meanS) <= tol * 10);
                    bool atEnd = Math.Abs(meanS) <= tol * 10 || Math.Abs(meanS - Length) <= tol * 10;
                    if (constantS && !atEnd) continue; // boundary between two faces of the same cylinder, not an edge of the blank
                    foreach (var p in pieces) p.Layer = PatternLayer.OuterProfile;
                    if (meanS < Length / 2) leftEnd = constantS ? null : pieces; else rightEnd = constantS ? null : pieces;
                    continue;
                }
                pat.Polylines.AddRange(pieces);
            }

            double XAt(List<PatternPolyline> end, double y, double def)
            {
                if (end == null) return def;
                var pts = end.SelectMany(p => new[] { p.Points[0], p.Points[p.Points.Count - 1] });
                var best = pts.OrderBy(q => Math.Abs(q.Y - y)).First();
                return best.X;
            }

            if (leftEnd == null) pat.Line(PatternLayer.OuterProfile, 0, 0, 0, W); else pat.Polylines.AddRange(leftEnd);
            if (rightEnd == null) pat.Line(PatternLayer.OuterProfile, Length, 0, Length, W); else pat.Polylines.AddRange(rightEnd);
            pat.Line(PatternLayer.OuterProfile, XAt(leftEnd, 0, 0), 0, XAt(rightEnd, 0, Length), 0);
            pat.Line(PatternLayer.OuterProfile, XAt(leftEnd, W, 0), W, XAt(rightEnd, W, Length), W);

            // Centreline references: quadrant lines.
            for (int q = 1; q < 4; q++) pat.Line(PatternLayer.Construction, 0, W * q / 4, Length, W * q / 4);

            double th = Math.Max(W / 40, 0.0015);
            foreach (var b in bends)
            {
                pat.Line(PatternLayer.BendLines, b.StartS, 0, b.StartS, W);
                pat.Line(PatternLayer.BendLines, b.EndS, 0, b.EndS, W);
                double c = (b.StartS + b.EndS) / 2;
                pat.Line(PatternLayer.Centreline, c, 0, c, W);
                // Inside of the bend: a short mark across the centre line.
                double yIn = RefRadius * Angles.Wrap2Pi(b.InsideAngle - SeamAngle);
                double half = Math.Min((b.EndS - b.StartS) / 2, th * 2);
                pat.Line(PatternLayer.BendLines, c - half, yIn, c + half, yIn);
                pat.Texts.Add(new PatternText
                {
                    Layer = PatternLayer.Text, At = new Point2(c + th * 0.3, yIn + th * 0.5), Height = th, RotationDeg = 90,
                    Text = string.Format(CultureInfo.InvariantCulture, "B{0} {1:0.##}deg R{2:0.###} ROT {3:+0.##;-0.##;0}deg INSIDE",
                        b.Number, Angles.ToDeg(b.Angle), b.Radius * 1000, Angles.ToDeg(b.Rotation)),
                });
            }
            pat.Texts.Add(new PatternText { Layer = PatternLayer.Text, At = new Point2(0, -th * 2), Height = th, Text = title ?? "" });
            return pat;
        }
    }
}
