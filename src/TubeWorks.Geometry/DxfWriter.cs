using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TubeWorks.Geometry
{
    /// <summary>Configurable DXF layer names and colours (AutoCAD colour index).</summary>
    public sealed class DxfLayerSettings
    {
        public Dictionary<PatternLayer, string> Names { get; } = new Dictionary<PatternLayer, string>
        {
            [PatternLayer.OuterProfile] = "OUTER_PROFILE",
            [PatternLayer.InternalCutouts] = "INTERNAL_CUTOUTS",
            [PatternLayer.BendLines] = "BEND_LINES",
            [PatternLayer.Centreline] = "CENTRELINE",
            [PatternLayer.Engraving] = "ENGRAVING",
            [PatternLayer.Construction] = "CONSTRUCTION",
            [PatternLayer.Text] = "TEXT",
        };
        public Dictionary<PatternLayer, int> Colours { get; } = new Dictionary<PatternLayer, int>
        {
            [PatternLayer.OuterProfile] = 7, [PatternLayer.InternalCutouts] = 1, [PatternLayer.BendLines] = 3,
            [PatternLayer.Centreline] = 4, [PatternLayer.Engraving] = 6, [PatternLayer.Construction] = 8, [PatternLayer.Text] = 2,
        };
        public LengthUnit Unit { get; set; } = LengthUnit.Millimetre;
        public double Scale { get; set; } = 1.0;
    }

    /// <summary>Writes ASCII DXF R12 (AC1009): LAYER/LTYPE tables, LINE, POLYLINE, TEXT. 1:1 in the selected unit by default.</summary>
    public static class DxfWriter
    {
        public static string Write(Pattern2D pattern, DxfLayerSettings settings = null)
        {
            settings = settings ?? new DxfLayerSettings();
            double k = (settings.Unit == LengthUnit.Inch ? 1 / 0.0254 : 1000.0) * settings.Scale;
            var sb = new StringBuilder();
            void G(int code, string v) { sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append("\r\n").Append(v).Append("\r\n"); }
            void N(int code, double v) => G(code, v.ToString("0.########", CultureInfo.InvariantCulture));
            string L(PatternLayer l) => Sanitize(settings.Names[l]);

            G(0, "SECTION"); G(2, "HEADER");
            G(9, "$ACADVER"); G(1, "AC1009");
            G(9, "$EXTMIN"); N(10, 0); N(20, 0); N(30, 0);
            G(9, "$EXTMAX"); N(10, pattern.Length * k); N(20, pattern.Width * k); N(30, 0);
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "TABLES");
            G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
            G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); N(40, 0);
            G(0, "ENDTAB");
            var layers = Enum.GetValues(typeof(PatternLayer)).Cast<PatternLayer>().ToList();
            G(0, "TABLE"); G(2, "LAYER"); G(70, layers.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var l in layers)
            {
                G(0, "LAYER"); G(2, L(l)); G(70, "0"); G(62, settings.Colours[l].ToString(CultureInfo.InvariantCulture)); G(6, "CONTINUOUS");
            }
            G(0, "ENDTAB");
            G(0, "ENDSEC");

            G(0, "SECTION"); G(2, "ENTITIES");
            foreach (var pl in pattern.Polylines)
            {
                if (pl.Points.Count < 2) continue;
                if (pl.Points.Count == 2 && !pl.Closed)
                {
                    G(0, "LINE"); G(8, L(pl.Layer));
                    N(10, pl.Points[0].X * k); N(20, pl.Points[0].Y * k); N(30, 0);
                    N(11, pl.Points[1].X * k); N(21, pl.Points[1].Y * k); N(31, 0);
                    continue;
                }
                G(0, "POLYLINE"); G(8, L(pl.Layer)); G(66, "1"); N(10, 0); N(20, 0); N(30, 0); G(70, pl.Closed ? "1" : "0");
                foreach (var p in pl.Points) { G(0, "VERTEX"); G(8, L(pl.Layer)); N(10, p.X * k); N(20, p.Y * k); N(30, 0); }
                G(0, "SEQEND"); G(8, L(pl.Layer));
            }
            foreach (var t in pattern.Texts)
            {
                G(0, "TEXT"); G(8, L(t.Layer)); N(10, t.At.X * k); N(20, t.At.Y * k); N(30, 0); N(40, t.Height * k);
                G(1, Ascii(t.Text)); if (Math.Abs(t.RotationDeg) > 1e-9) N(50, t.RotationDeg);
            }
            G(0, "ENDSEC");
            G(0, "EOF");
            return sb.ToString();
        }

        private static string Sanitize(string name)
        {
            var s = new string((name ?? "0").Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? char.ToUpperInvariant(c) : '_').ToArray());
            return s.Length == 0 ? "0" : s.Length > 31 ? s.Substring(0, 31) : s;
        }

        private static string Ascii(string s) => new string((s ?? "").Select(c => c >= 32 && c < 127 ? c : '?').ToArray());
    }
}
