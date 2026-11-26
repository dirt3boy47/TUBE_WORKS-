using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace TubeWorks.Geometry
{
    // ===================================================================== stock length optimisation / tube nesting

    public sealed class NestPart
    {
        public string Name { get; set; } = "";
        /// <summary>Cut length (m).</summary>
        public double Length { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public sealed class NestStock
    {
        public double Length { get; set; }
        /// <summary>Bars available; null = unlimited.</summary>
        public int? Available { get; set; }
        /// <summary>Price per bar (optional, for the cost of the nest).</summary>
        public double? PricePerBar { get; set; }
    }

    public sealed class NestSettings
    {
        /// <summary>Material lost per cut (saw kerf), m.</summary>
        public double Kerf { get; set; }
        /// <summary>Unusable length at the start of each bar (squaring cut), m.</summary>
        public double BarStartTrim { get; set; }
        /// <summary>Unusable remnant at the end of each bar (clamping on the saw), m.</summary>
        public double BarEndTrim { get; set; }
    }

    public sealed class NestBar
    {
        public double StockLength;
        public List<(string name, double length)> Parts = new List<(string, double)>();
        public double Used;      // parts + kerfs + trims
        public double Offcut => StockLength - Used;
    }

    public sealed class NestResult
    {
        public List<NestBar> Bars { get; } = new List<NestBar>();
        public List<string> Errors { get; } = new List<string>();
        public double PartLength => Bars.Sum(b => b.Parts.Sum(p => p.length));
        public double StockUsed => Bars.Sum(b => b.StockLength);
        /// <summary>Parts length / stock length.</summary>
        public double Utilisation => StockUsed > 0 ? PartLength / StockUsed : 0;
    }

    /// <summary>
    /// One-dimensional nesting of straight tube blanks into stock bars (cutting-stock problem).
    /// Heuristic: longest part first, best fit into the open bars; a new bar takes the shortest stock length that fits;
    /// finally each bar is moved to the shortest stock length that still holds its parts. The result is valid
    /// (no bar overfilled, every part placed) but not proven optimal.
    /// </summary>
    public static class StockNesting
    {
        public static NestResult Nest(IEnumerable<NestPart> parts, IEnumerable<NestStock> stock, NestSettings s)
        {
            s = s ?? new NestSettings();
            var res = new NestResult();
            var stocks = stock.Where(x => x.Length > 0).OrderBy(x => x.Length).ToList();
            var left = stocks.ToDictionary(x => x, x => x.Available);
            if (stocks.Count == 0) { res.Errors.Add("No stock length is defined."); return res; }
            double Overhead(double len) => s.BarStartTrim + s.BarEndTrim;
            var items = parts.SelectMany(p => Enumerable.Repeat((p.Name, p.Length), Math.Max(0, p.Quantity))).OrderByDescending(p => p.Length).ToList();

            foreach (var (name, len) in items)
            {
                if (!(len > 0)) { res.Errors.Add($"{name}: length must be greater than zero."); continue; }
                // Each part after the first in a bar costs one kerf.
                NestBar best = null; double bestRest = double.MaxValue;
                foreach (var b in res.Bars)
                {
                    double need = len + (b.Parts.Count > 0 ? s.Kerf : 0);
                    double rest = b.StockLength - b.Used - need;
                    if (rest >= -1e-12 && rest < bestRest) { best = b; bestRest = rest; }
                }
                if (best == null)
                {
                    var st = stocks.FirstOrDefault(x => x.Length - Overhead(x.Length) >= len - 1e-12 && (!left[x].HasValue || left[x] > 0));
                    if (st == null)
                    {
                        res.Errors.Add(stocks.All(x => x.Length - Overhead(x.Length) < len) ? $"{name}: {len * 1000:0.#} mm is longer than the longest usable stock length." : $"{name}: not enough stock bars.");
                        continue;
                    }
                    if (left[st].HasValue) left[st]--;
                    best = new NestBar { StockLength = st.Length, Used = Overhead(st.Length) };
                    res.Bars.Add(best);
                }
                best.Used += len + (best.Parts.Count > 0 ? s.Kerf : 0);
                best.Parts.Add((name, len));
            }
            // Shortest stock that still holds each bar.
            foreach (var b in res.Bars)
            {
                var cur = stocks.First(x => Math.Abs(x.Length - b.StockLength) < 1e-12);
                var shorter = stocks.FirstOrDefault(x => x.Length < b.StockLength && x.Length >= b.Used - 1e-12 && (!left[x].HasValue || left[x] > 0));
                if (shorter != null)
                {
                    if (left[shorter].HasValue) left[shorter]--;
                    if (left[cur].HasValue) left[cur]++;
                    b.StockLength = shorter.Length;
                }
            }
            return res;
        }

        public static double? Cost(NestResult r, IEnumerable<NestStock> stock)
        {
            var list = stock.ToList();
            double total = 0;
            foreach (var b in r.Bars)
            {
                var st = list.FirstOrDefault(x => Math.Abs(x.Length - b.StockLength) < 1e-12);
                if (st?.PricePerBar == null) return null;
                total += st.PricePerBar.Value;
            }
            return total;
        }
    }

    // ===================================================================== cost estimation

    /// <summary>Cost rates and process times. All values are user data; a null value is "not defined" and its line is left out.</summary>
    public sealed class CostRates
    {
        public string Currency { get; set; } = "";
        /// <summary>Material price per metre of tube.</summary>
        public double? MaterialPerMetre { get; set; }
        public double? HourlyRate { get; set; }
        /// <summary>Set-up time per batch (h).</summary>
        public double? SetupHours { get; set; }
        /// <summary>Process times per part (s): cutting, loading/unloading, per bend, per metre of feed.</summary>
        public double? CutSeconds { get; set; }
        public double? LoadUnloadSeconds { get; set; }
        public double? SecondsPerBend { get; set; }
        public double? SecondsPerRotation { get; set; }
        public double? FeedSecondsPerMetre { get; set; }
        /// <summary>Fixed extra cost per part (finishing, inspection).</summary>
        public double? ExtraPerPart { get; set; }
    }

    public sealed class CostLine { public string Item; public double PerPart; public double Batch; }

    public sealed class CostEstimate
    {
        public int Quantity;
        public List<CostLine> Lines { get; } = new List<CostLine>();
        public List<string> NotDefined { get; } = new List<string>();
        public double CycleSeconds;
        public double PerPart => Lines.Sum(l => l.PerPart);
        public double Batch => Lines.Sum(l => l.Batch);
    }

    public static class CostModel
    {
        /// <param name="materialFromNest">Material cost of the batch from a nest (bars actually used); overrides the per-metre cut-length estimate.</param>
        public static CostEstimate Estimate(CompensatedData part, BendTable table, int quantity, CostRates r, double? materialFromNest = null)
        {
            r = r ?? new CostRates();
            var e = new CostEstimate { Quantity = Math.Max(1, quantity) };
            int q = e.Quantity;
            if (materialFromNest.HasValue) e.Lines.Add(new CostLine { Item = "Material (bars used in the nest)", PerPart = materialFromNest.Value / q, Batch = materialFromNest.Value });
            else if (r.MaterialPerMetre.HasValue) e.Lines.Add(new CostLine { Item = "Material (cut length × price per metre, no offcut)", PerPart = part.CutLength * r.MaterialPerMetre.Value, Batch = part.CutLength * r.MaterialPerMetre.Value * q });
            else e.NotDefined.Add("material price");

            int rotations = table.Rows.Count(x => Math.Abs(x.Rotation) > 1e-9);
            double cycle = 0; bool anyTime = false;
            void T(double? v, double n, string what) { if (v.HasValue) { cycle += v.Value * n; anyTime = true; } else e.NotDefined.Add(what); }
            T(r.CutSeconds, 1, "cutting time");
            T(r.LoadUnloadSeconds, 1, "load/unload time");
            T(r.SecondsPerBend, table.Rows.Count, "time per bend");
            T(r.SecondsPerRotation, rotations, "time per rotation");
            T(r.FeedSecondsPerMetre, part.CutLength, "feed time");
            e.CycleSeconds = cycle;
            if (r.HourlyRate.HasValue)
            {
                if (anyTime) e.Lines.Add(new CostLine { Item = $"Machine time ({cycle:0.#} s per part)", PerPart = cycle / 3600 * r.HourlyRate.Value, Batch = cycle / 3600 * r.HourlyRate.Value * q });
                if (r.SetupHours.HasValue) e.Lines.Add(new CostLine { Item = "Set-up (shared by the batch)", PerPart = r.SetupHours.Value * r.HourlyRate.Value / q, Batch = r.SetupHours.Value * r.HourlyRate.Value });
                else e.NotDefined.Add("set-up time");
            }
            else e.NotDefined.Add("hourly rate");
            if (r.ExtraPerPart.HasValue) e.Lines.Add(new CostLine { Item = "Extra per part", PerPart = r.ExtraPerPart.Value, Batch = r.ExtraPerPart.Value * q });
            return e;
        }
    }

    // ===================================================================== manufacturing report

    /// <summary>Self-contained HTML manufacturing report (printable; print to PDF from the browser).</summary>
    public sealed class ManufacturingReport
    {
        private readonly StringBuilder _b = new StringBuilder();
        private readonly string _unit;
        private readonly Func<double, double> _l;

        public ManufacturingReport(string title, LengthUnit unit = LengthUnit.Millimetre)
        {
            _unit = unit == LengthUnit.Inch ? "in" : "mm";
            _l = unit == LengthUnit.Inch ? (Func<double, double>)(m => m / 0.0254) : m => m * 1000;
            _b.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(H(title)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#111}h1{font-size:20px}h2{font-size:16px;margin-top:24px;border-bottom:1px solid #999}")
              .Append("table{border-collapse:collapse;margin:8px 0}td,th{border:1px solid #aaa;padding:3px 8px;text-align:right}th{background:#eee}td.l,th.l{text-align:left}")
              .Append(".err{color:#b00000}.warn{color:#8a5a00}.note{color:#555;font-size:12px}@media print{h2{page-break-after:avoid}}")
              .Append("</style></head><body><h1>").Append(H(title)).Append("</h1>");
        }

        public static string H(string s) => WebUtility.HtmlEncode(s ?? "");
        private string Len(double m) => _l(m).ToString("0.##", CultureInfo.InvariantCulture);

        public ManufacturingReport Section(string title) { _b.Append("<h2>").Append(H(title)).Append("</h2>"); return this; }
        public ManufacturingReport Para(string text, string cls = null) { _b.Append(cls == null ? "<p>" : $"<p class=\"{cls}\">").Append(H(text)).Append("</p>"); return this; }

        public ManufacturingReport Table(string[] head, IEnumerable<string[]> rows, int leftColumns = 1)
        {
            _b.Append("<table><tr>");
            for (int i = 0; i < head.Length; i++) _b.Append(i < leftColumns ? "<th class=\"l\">" : "<th>").Append(H(head[i])).Append("</th>");
            _b.Append("</tr>");
            foreach (var r in rows)
            {
                _b.Append("<tr>");
                for (int i = 0; i < r.Length; i++) _b.Append(i < leftColumns ? "<td class=\"l\">" : "<td>").Append(H(r[i])).Append("</td>");
                _b.Append("</tr>");
            }
            _b.Append("</table>");
            return this;
        }

        public ManufacturingReport Part(string name, TubeProfile p, string material, BendTable t, CompensatedData c, IEnumerable<ValidationIssue> issues, CostEstimate cost)
        {
            Section("Part: " + name);
            Table(new[] { "Item", "Value" }, new[]
            {
                new[] { "Tube", $"OD {Len(p.OuterDiameter)} × {Len(p.WallThickness)} wall {_unit} (ID {Len(p.InnerDiameter)})" },
                new[] { "Material", string.IsNullOrEmpty(material) ? "not set" : material },
                new[] { "Bends", t.Rows.Count.ToString(CultureInfo.InvariantCulture) },
                new[] { "Nominal developed length", Len(c.NominalLength) + " " + _unit },
                new[] { "Cut length (with compensation and trims)", Len(c.CutLength) + " " + _unit },
            });
            Table(new[] { "Bend", $"Feed ({_unit})", "Rotation (°)", "Design angle (°)", "Command angle (°)", $"CLR ({_unit})" },
                c.Rows.Select(r => new[]
                {
                    r.Number.ToString(CultureInfo.InvariantCulture), Len(r.Feed), Angles.ToDeg(r.Rotation).ToString("0.##", CultureInfo.InvariantCulture),
                    r.TargetDeg.ToString("0.##", CultureInfo.InvariantCulture), r.CommandDeg.ToString("0.##", CultureInfo.InvariantCulture), Len(r.Clr),
                }).Concat(new[] { new[] { "End", Len(c.FinalFeed), "", "", "", "" } }));
            foreach (var n in c.Notes) Para(n, "note");
            var list = (issues ?? Enumerable.Empty<ValidationIssue>()).ToList();
            if (list.Count == 0) Para("Manufacturing check: no errors, no warnings (limits not defined in the library are not checked).");
            foreach (var i in list) Para((i.Severity == IssueSeverity.Error ? "ERROR: " : "Warning: ") + i.Message, i.Severity == IssueSeverity.Error ? "err" : "warn");
            Guide(t, c, p);
            if (cost != null) Cost(cost);
            return this;
        }

        /// <summary>Rolled-out tube with bend marks (SVG) for manual bending.</summary>
        public ManufacturingReport Guide(BendTable t, CompensatedData c, TubeProfile p)
        {
            if (t.Rows.Count == 0) return this;
            _b.Append("<p><b>Bending guide</b> (straightened tube; green = first straight, blue = straight, red = bend; marks from the start end S):</p>");
            _b.Append(BendingGuide.Svg(t, c, p));
            _b.Append("<ol>");
            foreach (var i in BendingGuide.Instructions(t, c)) _b.Append("<li>").Append(H(i)).Append("</li>");
            _b.Append("</ol>");
            return this;
        }

        public ManufacturingReport Cost(CostEstimate e)
        {
            Table(new[] { "Cost item", "Per part", $"Batch of {e.Quantity}" },
                e.Lines.Select(l => new[] { l.Item, l.PerPart.ToString("0.00", CultureInfo.InvariantCulture), l.Batch.ToString("0.00", CultureInfo.InvariantCulture) })
                 .Concat(new[] { new[] { "TOTAL (defined items only)", e.PerPart.ToString("0.00", CultureInfo.InvariantCulture), e.Batch.ToString("0.00", CultureInfo.InvariantCulture) } }));
            if (e.NotDefined.Count > 0) Para("Not included because not defined in the cost rates: " + string.Join(", ", e.NotDefined.Distinct()) + ".", "warn");
            return this;
        }

        public ManufacturingReport Nest(NestResult r, NestSettings s)
        {
            Section("Stock and nesting");
            Table(new[] { "Bar", $"Stock ({_unit})", "Parts", $"Offcut ({_unit})" },
                r.Bars.Select((b, i) => new[] { (i + 1).ToString(CultureInfo.InvariantCulture), Len(b.StockLength), string.Join(" + ", b.Parts.Select(p => $"{p.name} ({Len(p.length)})")), Len(b.Offcut) }), 1);
            Para($"Bars: {r.Bars.Count}. Stock used {Len(r.StockUsed)} {_unit}, parts {Len(r.PartLength)} {_unit}, utilisation {r.Utilisation * 100:0.#} %. " +
                 $"Kerf {Len(s.Kerf)} {_unit}, bar trims {Len(s.BarStartTrim)} + {Len(s.BarEndTrim)} {_unit}. Heuristic nest (valid, not proven optimal).", "note");
            foreach (var e in r.Errors) Para("ERROR: " + e, "err");
            return this;
        }

        public string Html() => _b.ToString() + "<p class=\"note\">Generated by TubeWorks Designer.</p></body></html>";
    }
}
