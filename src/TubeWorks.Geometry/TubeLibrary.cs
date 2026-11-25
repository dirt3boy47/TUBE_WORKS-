using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace TubeWorks.Geometry
{
    /// <summary>One library entry: profile, material, stock lengths, tooling and optional manufacturing limits. Values in metres.</summary>
    public sealed class TubePreset
    {
        public string Name { get; set; } = "";
        public string Material { get; set; } = "";
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        public List<double> StockLengths { get; set; } = new List<double>();
        /// <summary>Centreline radii available on the tooling.</summary>
        public List<double> BendRadii { get; set; } = new List<double>();
        public double? MinBendRadius { get; set; }
        public double? MinStraightBetweenBends { get; set; }
        public double? MinClampLength { get; set; }
        /// <summary>Optional machine limit; null = 180° default of the validator.</summary>
        public double? MaxBendAngleDeg { get; set; }
        /// <summary>Calibration data, stored for Phase 3 compensation. TubeWorks does not apply these values yet.</summary>
        public double? SpringbackDeg { get; set; }
        public double? StretchPerBend { get; set; }
        public string Machine { get; set; } = "";
        public string Tooling { get; set; } = "";
    }

    /// <summary>Tube library. No values are built in: every limit is user data.</summary>
    public sealed class TubeLibrary
    {
        public List<TubePreset> Presets { get; set; } = new List<TubePreset>();

        public TubePreset Match(TubeProfile p) =>
            p == null ? null : Presets.Find(x => Math.Abs(x.OuterDiameter - p.OuterDiameter) < 1e-6 && Math.Abs(x.WallThickness - p.WallThickness) < 1e-6);

        public ManufacturingRules RulesFor(TubeProfile p)
        {
            var m = Match(p);
            var r = new ManufacturingRules();
            if (m != null)
            {
                r.MinBendRadius = m.MinBendRadius; r.MinStraightBetweenBends = m.MinStraightBetweenBends; r.MinClampLength = m.MinClampLength;
                if (m.MaxBendAngleDeg.HasValue) r.MaxBendAngle = Angles.ToRad(m.MaxBendAngleDeg.Value);
            }
            return r;
        }

        /// <summary>Validation of the library itself. Returns messages; empty when the data is consistent.</summary>
        public List<string> Check()
        {
            var e = new List<string>();
            foreach (var p in Presets)
            {
                string n = string.IsNullOrWhiteSpace(p.Name) ? "(no name)" : p.Name;
                if (!(p.OuterDiameter > 0)) e.Add($"{n}: outside diameter must be greater than zero.");
                if (!(p.WallThickness > 0) || !(p.WallThickness < p.OuterDiameter / 2)) e.Add($"{n}: wall thickness must be greater than zero and less than half the OD.");
                if (p.MinBendRadius.HasValue && !(p.MinBendRadius > p.OuterDiameter / 2)) e.Add($"{n}: minimum bend radius must be greater than half the OD.");
                if (p.BendRadii.Any(r => !(r > p.OuterDiameter / 2))) e.Add($"{n}: every tooling radius must be greater than half the OD.");
            }
            foreach (var g in Presets.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                e.Add($"Duplicate preset name: {g.Key}");
            return e;
        }

        // ------------------------------------------------------------------ XML

        public void WriteXml(Stream s) => new XmlSerializer(typeof(TubeLibrary)).Serialize(s, this);
        public static TubeLibrary ReadXml(Stream s) => (TubeLibrary)new XmlSerializer(typeof(TubeLibrary)).Deserialize(s);

        // ------------------------------------------------------------------ CSV (millimetres, degrees; ';' separates list values)

        private static readonly string[] Header =
        {
            "Name", "Material", "OD (mm)", "Wall (mm)", "Stock lengths (mm)", "Bend radii (mm)", "Min CLR (mm)",
            "Min straight between bends (mm)", "Min clamp length (mm)", "Max bend angle (deg)", "Springback (deg)", "Stretch per bend (mm)", "Machine", "Tooling",
        };

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));
            foreach (var p in Presets)
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(p.Name), Q(p.Material), Mm(p.OuterDiameter), Mm(p.WallThickness), Q(string.Join(";", p.StockLengths.Select(Mm))),
                    Q(string.Join(";", p.BendRadii.Select(Mm))), Mm(p.MinBendRadius), Mm(p.MinStraightBetweenBends), Mm(p.MinClampLength),
                    D(p.MaxBendAngleDeg), D(p.SpringbackDeg), Mm(p.StretchPerBend), Q(p.Machine), Q(p.Tooling),
                }));
            return sb.ToString();
        }

        public static TubeLibrary FromCsv(string text)
        {
            var lib = new TubeLibrary();
            var rows = ParseCsv(text);
            if (rows.Count == 0) return lib;
            var head = rows[0];
            int Col(string name) { int i = head.FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase)); return i; }
            int[] c = Header.Select(Col).ToArray();
            if (c[0] < 0 || c[2] < 0 || c[3] < 0) throw new FormatException("The CSV needs at least the columns Name, OD (mm) and Wall (mm).");
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.All(string.IsNullOrWhiteSpace)) continue;
                string Get(int k) => c[k] >= 0 && c[k] < row.Count ? row[c[k]].Trim() : "";
                double? Opt(int k, double f) { var v = Get(k); return v.Length == 0 ? (double?)null : Parse(v, r) * f; }
                List<double> List(int k) => Get(k).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(v => Parse(v, r) / 1000).ToList();
                lib.Presets.Add(new TubePreset
                {
                    Name = Get(0), Material = Get(1), OuterDiameter = Parse(Get(2), r) / 1000, WallThickness = Parse(Get(3), r) / 1000,
                    StockLengths = List(4), BendRadii = List(5), MinBendRadius = Opt(6, 0.001), MinStraightBetweenBends = Opt(7, 0.001),
                    MinClampLength = Opt(8, 0.001), MaxBendAngleDeg = Opt(9, 1), SpringbackDeg = Opt(10, 1), StretchPerBend = Opt(11, 0.001),
                    Machine = Get(12), Tooling = Get(13),
                });
            }
            return lib;
        }

        private static double Parse(string v, int row)
        {
            if (!double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException($"Row {row + 1}: '{v}' is not a number (use '.' as the decimal separator).");
            return d;
        }

        private static string Mm(double v) => (v * 1000).ToString("0.####", CultureInfo.InvariantCulture);
        private static string Mm(double? v) => v.HasValue ? Mm(v.Value) : "";
        private static string D(double? v) => v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : "";
        private static string Q(string s) => (s ?? "").IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s ?? "";

        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool q = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (q)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') q = false;
                    else cell.Append(ch);
                }
                else if (ch == '"') q = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>();
                }
                else cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
