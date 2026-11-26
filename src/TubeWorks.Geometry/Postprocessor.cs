using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TubeWorks.Geometry
{
    /// <summary>
    /// Text postprocessor defined by a template file. A template has settings lines (key=value) and three sections:
    /// [header], [bend] (repeated for every bend) and [footer]. Placeholders in braces are replaced; an unknown
    /// placeholder is an error, so a template typo never produces silent wrong output.
    ///
    /// Placeholders: {PART} {MATERIAL} {OD} {WALL} {CUTLENGTH} {NOMINALLENGTH} {BENDS} {DATE}
    /// per bend: {N} {L} (feed before the bend) {R} (rotation) {A} (commanded angle) {A0} (design angle) {CLR}
    /// {X} {Y} {Z} (tangent-line intersection; empty at 180° or more) and in footer: {LAST} (final feed).
    ///
    /// The format of each real machine controller must be taken from that machine's documentation. TubeWorks ships
    /// only generic examples, not certified machine formats.
    /// </summary>
    public sealed class PostTemplate
    {
        public string Name { get; set; } = "Generic";
        public string Extension { get; set; } = ".txt";
        public int Decimals { get; set; } = 2;
        public LengthUnit Unit { get; set; } = LengthUnit.Millimetre;
        public RotationSign RotationSign { get; set; } = RotationSign.RightHandAboutFeed;
        /// <summary>Report rotation as 0..360 instead of -180..180.</summary>
        public bool RotationPositive { get; set; }
        public AngleConvention AngleConvention { get; set; } = AngleConvention.BendAngle;
        public string Header { get; set; } = "";
        public string Bend { get; set; } = "";
        public string Footer { get; set; } = "";

        public static PostTemplate Parse(string text)
        {
            var t = new PostTemplate();
            string section = null;
            var sb = new Dictionary<string, StringBuilder> { ["header"] = new StringBuilder(), ["bend"] = new StringBuilder(), ["footer"] = new StringBuilder() };
            int line = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                line++;
                string l = raw.TrimEnd();
                string trimmed = l.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).ToLowerInvariant();
                    if (!sb.ContainsKey(section)) throw new FormatException($"Line {line}: unknown section [{section}]. Use [header], [bend] or [footer].");
                    continue;
                }
                if (section != null) { sb[section].Append(l).Append("\r\n"); continue; }
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) throw new FormatException($"Line {line}: expected key=value before the first section.");
                string k = trimmed.Substring(0, eq).Trim().ToLowerInvariant(), v = trimmed.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "name": t.Name = v; break;
                    case "extension": t.Extension = v.StartsWith(".") ? v : "." + v; break;
                    case "decimals": t.Decimals = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case "unit": t.Unit = v.ToLowerInvariant().StartsWith("in") ? LengthUnit.Inch : LengthUnit.Millimetre; break;
                    case "rotation": t.RotationSign = v.ToLowerInvariant().StartsWith("left") ? RotationSign.LeftHandAboutFeed : RotationSign.RightHandAboutFeed; break;
                    case "rotationrange": t.RotationPositive = v.Trim() == "0-360"; break;
                    case "angle": t.AngleConvention = v.ToLowerInvariant().StartsWith("incl") ? AngleConvention.IncludedAngle : AngleConvention.BendAngle; break;
                    default: throw new FormatException($"Line {line}: unknown setting '{k}'.");
                }
            }
            t.Header = sb["header"].ToString(); t.Bend = sb["bend"].ToString(); t.Footer = sb["footer"].ToString();
            if (t.Bend.Trim().Length == 0) throw new FormatException("The template has no [bend] section.");
            return t;
        }

        public string Run(BendTable table, CompensatedData comp, IDictionary<string, string> part)
        {
            double L(double m) => Unit == LengthUnit.Inch ? m / 0.0254 : m * 1000;
            string F(double v) => v.ToString("F" + Decimals, CultureInfo.InvariantCulture);
            string Rot(double rad)
            {
                double d = Angles.ToDeg(RotationSign == RotationSign.LeftHandAboutFeed ? -rad : rad);
                if (RotationPositive) d = (d % 360 + 360) % 360;
                return F(Math.Abs(d) < 0.5 * Math.Pow(10, -Decimals) ? 0 : d);
            }
            string Ang(double deg) => F(AngleConvention == AngleConvention.IncludedAngle ? 180 - deg : deg);

            var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CUTLENGTH"] = F(L(comp.CutLength)), ["NOMINALLENGTH"] = F(L(comp.NominalLength)), ["BENDS"] = table.Rows.Count.ToString(CultureInfo.InvariantCulture),
                ["LAST"] = F(L(comp.FinalFeed)),
            };
            foreach (var kv in part) common[kv.Key] = kv.Value;

            var sb = new StringBuilder();
            sb.Append(Fill(Header, common));
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i]; var c = comp.Rows[i];
                var v = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
                {
                    ["N"] = row.Number.ToString(CultureInfo.InvariantCulture), ["L"] = F(L(c.Feed)), ["R"] = Rot(row.Rotation),
                    ["A"] = Ang(c.CommandDeg), ["A0"] = Ang(c.TargetDeg), ["CLR"] = F(L(row.Radius)),
                    ["X"] = row.Vertex.HasValue ? F(L(row.Vertex.Value.X)) : "", ["Y"] = row.Vertex.HasValue ? F(L(row.Vertex.Value.Y)) : "",
                    ["Z"] = row.Vertex.HasValue ? F(L(row.Vertex.Value.Z)) : "",
                };
                sb.Append(Fill(Bend, v));
            }
            sb.Append(Fill(Footer, common));
            return sb.ToString();
        }

        private static string Fill(string text, IDictionary<string, string> v)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int o = text.IndexOf('{', i);
                if (o < 0) { sb.Append(text, i, text.Length - i); break; }
                int c = text.IndexOf('}', o);
                if (c < 0) throw new FormatException("A '{' has no closing '}'.");
                sb.Append(text, i, o - i);
                string key = text.Substring(o + 1, c - o - 1);
                if (!v.TryGetValue(key, out var val)) throw new FormatException($"Unknown placeholder {{{key}}}.");
                sb.Append(val);
                i = c + 1;
            }
            return sb.ToString();
        }

        /// <summary>Generic examples shipped with TubeWorks. They are not formats of a particular machine.</summary>
        public static readonly string ExampleLraCsv =
            "name=Generic LRA (CSV)\nextension=.csv\ndecimals=2\nunit=mm\nrotation=right\nangle=bend\n" +
            "[header]\n# {PART}  OD {OD} x {WALL}  {MATERIAL}\nStep,L,R,A,CLR\n[bend]\n{N},{L},{R},{A},{CLR}\n[footer]\nEnd,{LAST},,,\nCut length,{CUTLENGTH}\n";

        public static readonly string ExampleXyz =
            "name=Generic XYZ\nextension=.xyz\ndecimals=3\nunit=mm\n" +
            "[header]\nPART {PART}\nTUBE {OD} {WALL}\n[bend]\nP{N} {X} {Y} {Z} R{CLR}\n[footer]\nCUT {CUTLENGTH}\n";
    }
}
