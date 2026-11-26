using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TubeWorks.Geometry
{
    /// <summary>One step of manual bending: where to mark the straight bar, how far to turn it, how far to bend it.</summary>
    public sealed class BendingStep
    {
        public int Number;
        /// <summary>Distance from the start of the bar to the start of the bend (tangent point), m.</summary>
        public double MarkStart;
        /// <summary>Distance from the start of the bar to the middle of the bend, m.</summary>
        public double MarkCentre;
        public double BendLength;
        public double RotationDeg;
        public double DesignDeg;
        public double CommandDeg;
        public double Clr;
    }

    /// <summary>
    /// Bending layout of the straight bar (the flat pattern as a bar): mark positions measured from the start of the bar,
    /// the rotation before each bend and the bend angle. Uses the compensated data (trims, commanded angles) when given.
    /// </summary>
    public static class BendingLayout
    {
        public static List<BendingStep> Steps(BendTable t, CompensatedData c = null)
        {
            double trim = c != null && c.Rows.Count > 0 ? c.Rows[0].Feed - t.Rows[0].Feed : 0;
            var steps = new List<BendingStep>();
            double s = trim;
            for (int i = 0; i < t.Rows.Count; i++)
            {
                var r = t.Rows[i];
                double dev = c != null && i < c.Rows.Count ? c.Rows[i].DevelopedBend : r.DevelopedBendLength;
                s += r.Feed;
                steps.Add(new BendingStep
                {
                    Number = r.Number, MarkStart = s, MarkCentre = s + dev / 2, BendLength = dev,
                    RotationDeg = Angles.ToDeg(r.Rotation), DesignDeg = Angles.ToDeg(r.Angle),
                    CommandDeg = c != null && i < c.Rows.Count ? c.Rows[i].CommandDeg : Angles.ToDeg(r.Angle), Clr = r.Radius,
                });
                s += dev;
            }
            return steps;
        }

        /// <summary>
        /// 2D layout in model metres: the bar (cut length × OD), bend zones, a centre mark per bend, a label per bend
        /// (number, angle, rotation) above the bar, and the mark positions as ordinate values below it.
        /// </summary>
        public static Pattern2D Build(BendTable t, double od, CompensatedData c, double textHeight)
        {
            var steps = Steps(t, c);
            double len = c?.CutLength ?? t.TotalDevelopedLength;
            var p = new Pattern2D { Length = len, Width = od };
            var bar = new PatternPolyline { Layer = PatternLayer.OuterProfile, Closed = true };
            bar.Points.AddRange(new[] { new Point2(0, 0), new Point2(len, 0), new Point2(len, od), new Point2(0, od) });
            p.Polylines.Add(bar);
            double h = textHeight, tick = Math.Max(od * 0.35, h * 1.2);
            string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            // Ordinate baseline below the bar: 0 at the start, then every mark, then the end.
            double yb = -tick * 1.5;
            p.Line(PatternLayer.Construction, 0, yb, len, yb);
            void Ord(double x, string text, double rowOffset)
            {
                p.Line(PatternLayer.Construction, x, yb - tick * 0.4, x, yb + tick * 0.4);
                p.Texts.Add(new PatternText { Layer = PatternLayer.Text, At = new Point2(x - h * 0.3, yb - tick * 0.6 - h * (1.2 + rowOffset)), Height = h, RotationDeg = 0, Text = text });
            }
            Ord(0, "0", 0);
            foreach (var st in steps)
            {
                p.Line(PatternLayer.BendLines, st.MarkStart, -tick * 0.3, st.MarkStart, od + tick * 0.3);
                p.Line(PatternLayer.BendLines, st.MarkStart + st.BendLength, 0, st.MarkStart + st.BendLength, od);
                p.Line(PatternLayer.Centreline, st.MarkCentre, -tick * 0.15, st.MarkCentre, od + tick * 0.15);
                Ord(st.MarkStart, F(st.MarkStart * 1000), (st.Number % 2) * 1.3);
                string ang = Math.Abs(st.CommandDeg - st.DesignDeg) > 1e-6
                    ? $"BEND {st.DesignDeg:0.##}° (COMMAND {st.CommandDeg:0.##}°)" : $"BEND {st.DesignDeg:0.##}°";
                p.Texts.Add(new PatternText
                {
                    Layer = PatternLayer.Text, At = new Point2(st.MarkStart, od + tick * 0.5 + h * (1 + (st.Number % 2) * 3.6)), Height = h,
                    Text = $"B{st.Number}  MARK {F(st.MarkStart * 1000)}  ROTATE {st.RotationDeg:+0.##;-0.##;0}°  {ang}  CLR {F(st.Clr * 1000)}",
                });
            }
            Ord(len, F(len * 1000) + " (CUT LENGTH)", 0);
            return p;
        }
    }
}
