using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace TubeWorks.Geometry
{
    /// <summary>
    /// Bending guide for manual bending: the straightened ("rolled out") tube drawn as an SVG, with the first straight
    /// green, the other straights blue and the bend zones red; bend start marks measured from the start end; and
    /// step-by-step instructions. Values come from <see cref="BendingLayout.Steps"/> (with calibration when given).
    /// </summary>
    public static class BendingGuide
    {
        public const string Green = "#1f9d3a", Blue = "#2456c8", Red = "#d02a2a";
        private const string GreenFill = "#d7f0dc", BlueFill = "#dbe5fb", RedFill = "#f7d0d0";

        private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Mm(double m) => (m * 1000).ToString("0.#", CultureInfo.InvariantCulture);
        private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        /// <summary>SVG of the rolled-out tube, <paramref name="widthPx"/> wide.</summary>
        public static string Svg(BendTable t, CompensatedData c, TubeProfile p, double widthPx = 1000)
        {
            var steps = BendingLayout.Steps(t, c);
            double len = c?.CutLength ?? t.TotalDevelopedLength;
            double margin = 40, k = (widthPx - 2 * margin) / len;          // px per metre
            double tubeH = Math.Max(p.OuterDiameter * k, 36);               // drawn at least 36 px high
            bool exaggerated = tubeH > p.OuterDiameter * k + 1e-9;
            double wallH = tubeH * p.WallThickness / p.OuterDiameter;
            double top = 150, bottom = top + tubeH, cy = top + tubeH / 2;
            // Rows of the ruler below the tube; labels alternate between two rows so that close marks do not overlap.
            double heightPx = bottom + 120;
            double X(double s) => margin + s * k;

            // Zones in order along the bar.
            var zones = new List<(double s0, double s1, bool bend, int n)>();
            double cur = 0;
            foreach (var st in steps)
            {
                if (st.MarkStart > cur + 1e-12) zones.Add((cur, st.MarkStart, false, 0));
                zones.Add((st.MarkStart, st.MarkStart + st.BendLength, true, st.Number));
                cur = st.MarkStart + st.BendLength;
            }
            if (len > cur + 1e-12) zones.Add((cur, len, false, 0));

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(widthPx)}\" height=\"{N(heightPx)}\" viewBox=\"0 0 {N(widthPx)} {N(heightPx)}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"13\">");
            bool firstStraight = true;
            foreach (var z in zones)
            {
                string fill = z.bend ? RedFill : firstStraight ? GreenFill : BlueFill;
                string line = z.bend ? Red : firstStraight ? Green : Blue;
                if (!z.bend) firstStraight = false;
                sb.Append($"<rect x=\"{N(X(z.s0))}\" y=\"{N(top)}\" width=\"{N((z.s1 - z.s0) * k)}\" height=\"{N(tubeH)}\" fill=\"{fill}\"/>");
                sb.Append($"<line x1=\"{N(X(z.s0))}\" y1=\"{N(cy)}\" x2=\"{N(X(z.s1))}\" y2=\"{N(cy)}\" stroke=\"{line}\" stroke-width=\"3\"/>");
            }
            // Tube outline, inner wall (dashed) and zone boundaries (rings).
            sb.Append($"<rect x=\"{N(X(0))}\" y=\"{N(top)}\" width=\"{N(len * k)}\" height=\"{N(tubeH)}\" fill=\"none\" stroke=\"#222\" stroke-width=\"1.5\"/>");
            sb.Append($"<line x1=\"{N(X(0))}\" y1=\"{N(top + wallH)}\" x2=\"{N(X(len))}\" y2=\"{N(top + wallH)}\" stroke=\"#666\" stroke-dasharray=\"6 4\"/>");
            sb.Append($"<line x1=\"{N(X(0))}\" y1=\"{N(bottom - wallH)}\" x2=\"{N(X(len))}\" y2=\"{N(bottom - wallH)}\" stroke=\"#666\" stroke-dasharray=\"6 4\"/>");
            foreach (var z in zones.Skip(1)) sb.Append($"<line x1=\"{N(X(z.s0))}\" y1=\"{N(top - 6)}\" x2=\"{N(X(z.s0))}\" y2=\"{N(bottom + 6)}\" stroke=\"#222\" stroke-width=\"1\"/>");
            sb.Append($"<text x=\"{N(X(0))}\" y=\"{N(top - 10)}\" font-weight=\"bold\">S (start end)</text>");

            // Bend labels: bubble with the number, then angle and rotation, on alternating heights.
            foreach (var st in steps)
            {
                double xm = X(st.MarkCentre), yb = top - 34 - (st.Number % 2 == 0 ? 52 : 0);
                sb.Append($"<line x1=\"{N(xm)}\" y1=\"{N(yb + 12)}\" x2=\"{N(xm)}\" y2=\"{N(top)}\" stroke=\"{Red}\" stroke-width=\"1\"/>");
                sb.Append($"<circle cx=\"{N(xm)}\" cy=\"{N(yb)}\" r=\"12\" fill=\"#fff\" stroke=\"{Red}\" stroke-width=\"2\"/>");
                sb.Append($"<text x=\"{N(xm)}\" y=\"{N(yb + 5)}\" text-anchor=\"middle\" font-weight=\"bold\" fill=\"{Red}\">{st.Number}</text>");
                string ang = Math.Abs(st.CommandDeg - st.DesignDeg) > 1e-6 ? $"bend {N(st.DesignDeg)}° (set {N(st.CommandDeg)}°)" : $"bend {N(st.DesignDeg)}°";
                string rot = st.Number == 1 ? "first bend" : Turn(st.RotationDeg);
                sb.Append($"<text x=\"{N(xm + 16)}\" y=\"{N(yb - 2)}\">{H(ang)}</text>");
                sb.Append($"<text x=\"{N(xm + 16)}\" y=\"{N(yb + 14)}\" fill=\"#444\">{H(rot)}, CLR {Mm(st.Clr)}</text>");
            }

            // Ruler: ticks at every zone boundary, distance from S in mm.
            double yr = bottom + 30;
            sb.Append($"<line x1=\"{N(X(0))}\" y1=\"{N(yr)}\" x2=\"{N(X(len))}\" y2=\"{N(yr)}\" stroke=\"#222\"/>");
            var ticks = new List<(double s, bool isBendStart)> { (0, false) };
            foreach (var st in steps) { ticks.Add((st.MarkStart, true)); ticks.Add((st.MarkStart + st.BendLength, false)); }
            ticks.Add((len, false));
            ticks = ticks.GroupBy(x => Math.Round(x.s, 7)).Select(g => (g.First().s, g.Any(x => x.isBendStart))).OrderBy(x => x.Item1).ToList();
            int row = 0;
            foreach (var (s, isStart) in ticks)
            {
                double x = X(s), y = yr + 18 + (row++ % 2) * 18;
                sb.Append($"<line x1=\"{N(x)}\" y1=\"{N(yr - 6)}\" x2=\"{N(x)}\" y2=\"{N(yr + 6)}\" stroke=\"#222\"/>");
                sb.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" text-anchor=\"middle\" {(isStart ? $"font-weight=\"bold\" fill=\"{Red}\"" : "")}>{Mm(s)}</text>");
            }
            sb.Append($"<text x=\"{N(X(0))}\" y=\"{N(heightPx - 8)}\" fill=\"#444\">Distances in mm from the start end S. Bold red = bend start mark (tangent point). Cut length {Mm(len)} mm." +
                      (exaggerated ? " Tube diameter drawn larger than scale for legibility." : "") + "</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Hand movement for a rotation. The rotation is the angle from the previous bend plane to this one, right-hand
        /// about the direction from S towards the far end: positive is clockwise when you look from S along the tube.
        /// With a bender that always bends the same way, the tube is turned the same angle the other way.
        /// </summary>
        public static string Turn(double rotationDeg) =>
            Math.Abs(rotationDeg) < 1e-6 ? "no turn (same plane)" : $"turn tube {N(Math.Abs(rotationDeg))}° {(rotationDeg > 0 ? "ACW" : "CW")}";

        /// <summary>Step-by-step manual bending instructions.</summary>
        public static List<string> Instructions(BendTable t, CompensatedData c)
        {
            var steps = BendingLayout.Steps(t, c);
            double len = c?.CutLength ?? t.TotalDevelopedLength;
            var list = new List<string>
            {
                $"Cut the tube to {Mm(len)} mm. Mark the start end \"S\".",
                "Mark the bend start lines, measured from S: " + string.Join(", ", steps.Select(s => $"B{s.Number} at {Mm(s.MarkStart)} mm")) + ". "
                + "Each bend zone (red) starts at its mark and runs towards the far end.",
                "Draw a straight reference line along the whole tube. It shows how far you turn the tube between bends.",
                "In these steps, clockwise (CW) and anticlockwise (ACW) are as seen from the S end, looking along the tube towards the far end.",
            };
            foreach (var s in steps)
            {
                string ang = Math.Abs(s.CommandDeg - s.DesignDeg) > 1e-6 ? $"{N(s.DesignDeg)}° (set the bender to {N(s.CommandDeg)}° to allow for springback)" : $"{N(s.DesignDeg)}°";
                string rot;
                if (s.Number == 1) rot = "Turn the reference line to the top. This sets the plane of bend 1.";
                else if (Math.Abs(s.RotationDeg) < 1e-6) rot = $"Do not turn the tube: bend {s.Number} is in the same plane and the same direction as bend {s.Number - 1}.";
                else
                {
                    double a = Math.Abs(s.RotationDeg);
                    string bendSide = s.RotationDeg > 0 ? "clockwise" : "anticlockwise", hand = s.RotationDeg > 0 ? "anticlockwise" : "clockwise";
                    rot = $"Bend {s.Number} must point {N(a)}° {bendSide} from bend {s.Number - 1}: with a bender that always bends the same way, turn the tube {N(a)}° {hand} from its position for bend {s.Number - 1}.";
                }
                list.Add($"Bend {s.Number}: put mark B{s.Number} ({Mm(s.MarkStart)} mm from S) at the start of the bend (tangent point of the die), with the S side in the pressure die (the side that stays straight). {rot} Bend {ang} on a {Mm(s.Clr)} mm centreline radius. The bend ends at {Mm(s.MarkStart + s.BendLength)} mm from S.");
            }
            bool stretch = c != null && c.Notes.Any(n => n.StartsWith("Stretch:") && !n.Contains("no data"));
            list.Add("Check the part against the drawing. " + (stretch ? "The marks include your stretch data." : "The marks use the nominal centreline; real tube stretches a little in each bend, so make a test part first and enter calibration data in the Tube Library."));
            return list;
        }

        /// <summary>Printable HTML page: title, SVG, step table and instructions.</summary>
        public static string Html(string part, TubeProfile p, string material, BendTable t, CompensatedData c)
        {
            var steps = BendingLayout.Steps(t, c);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Bending guide — ").Append(H(part)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:20px;color:#111}h1{font-size:20px}table{border-collapse:collapse}td,th{border:1px solid #aaa;padding:3px 8px;text-align:right}th{background:#eee}")
              .Append("ol li{margin:6px 0}.k{display:inline-block;width:14px;height:10px;margin:0 4px 0 10px;vertical-align:middle}@media print{svg{max-width:100%;height:auto}}</style></head><body>");
            sb.Append("<h1>Bending guide — ").Append(H(part)).Append("</h1>");
            sb.Append($"<p>Tube OD {Mm(p.OuterDiameter)} × {Mm(p.WallThickness)} mm wall, material {H(string.IsNullOrEmpty(material) ? "not set" : material)}. " +
                      $"Cut length <b>{Mm(c?.CutLength ?? t.TotalDevelopedLength)} mm</b>, {steps.Count} bend(s)." +
                      $"<span class=\"k\" style=\"background:{Green}\"></span>first straight<span class=\"k\" style=\"background:{Blue}\"></span>straight<span class=\"k\" style=\"background:{Red}\"></span>bend</p>");
            sb.Append(Svg(t, c, p));
            sb.Append("<table><tr><th>Bend</th><th>Mark from S (mm)</th><th>Bend ends at (mm)</th><th>Turn tube before bending (seen from S)</th><th>Bend (°)</th><th>Set bender to (°)</th><th>CLR (mm)</th></tr>");
            foreach (var s in steps)
                sb.Append($"<tr><td>{s.Number}</td><td><b>{Mm(s.MarkStart)}</b></td><td>{Mm(s.MarkStart + s.BendLength)}</td><td>{(s.Number == 1 ? "reference line on top" : Turn(s.RotationDeg))}</td><td>{N(s.DesignDeg)}</td><td>{N(s.CommandDeg)}</td><td>{Mm(s.Clr)}</td></tr>");
            sb.Append("</table><h2 style=\"font-size:16px\">Steps</h2><ol>");
            foreach (var i in Instructions(t, c)) sb.Append("<li>").Append(H(i)).Append("</li>");
            sb.Append("</ol>");
            if (c != null) { sb.Append("<p style=\"color:#555;font-size:12px\">"); foreach (var n in c.Notes) sb.Append(H(n)).Append("<br>"); sb.Append("</p>"); }
            sb.Append("<p style=\"color:#555;font-size:12px\">Generated by TubeWorks Designer.</p></body></html>");
            return sb.ToString();
        }
    }
}
