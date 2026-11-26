using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;
using BendTable = TubeWorks.Geometry.BendTable;

namespace TubeWorks.AddIn.Ui
{
    internal static class P3
    {
        public static Button Btn(string t, EventHandler h) { var b = new Button { Text = t, AutoSize = true }; b.Click += h; return b; }

        /// <summary>Cost rates for one tube: the shop rates, with the tube's own price per metre from the library.</summary>
        public static CostRates RatesFor(TubePreset p)
        {
            var src = ProductionSettings.Current.Rates;
            var r = new CostRates
            {
                Currency = src.Currency, MaterialPerMetre = p?.PricePerMetre ?? src.MaterialPerMetre, HourlyRate = src.HourlyRate, SetupHours = src.SetupHours,
                CutSeconds = src.CutSeconds, LoadUnloadSeconds = src.LoadUnloadSeconds, SecondsPerBend = src.SecondsPerBend,
                SecondsPerRotation = src.SecondsPerRotation, FeedSecondsPerMetre = src.FeedSecondsPerMetre, ExtraPerPart = src.ExtraPerPart,
            };
            return r;
        }

        public static void Open(string file) { try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch (Exception ex) { Log.Error("Open " + file, ex); } }
    }

    /// <summary>Production settings editor (cost rates, saw data, postprocessor).</summary>
    internal sealed class ProductionSettingsForm : Form
    {
        public static void ShowFor(IWin32Window owner) { using (var f = new ProductionSettingsForm()) f.ShowDialog(owner); }

        private ProductionSettingsForm()
        {
            Text = "TubeWorks Production Settings (" + ProductionSettings.FilePath + ")"; Width = 560; Height = 520; StartPosition = FormStartPosition.CenterParent;
            var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = ProductionSettings.Current, PropertySort = PropertySort.Alphabetical };
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 48, Padding = new Padding(6),
                Text = "Lengths in metres, times in seconds (set-up in hours), money in your currency. Leave a value empty when you do not know it:\nTubeWorks then leaves that item out and says so. Material prices per tube are in the Tube Library.",
            };
            var save = P3.Btn("Save", (o, e) => { ProductionSettings.Current.Save(); Close(); });
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
            bottom.Controls.Add(save);
            Controls.Add(grid); Controls.Add(note); Controls.Add(bottom);
        }
    }

    /// <summary>CNC Output: compensated bend data through a postprocessor template.</summary>
    internal sealed class CncForm : Form
    {
        private readonly PartResult _part;
        private readonly ComboBox _post = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly TextBox _out = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9), ReadOnly = true, WordWrap = false };
        private Dictionary<string, string> _templates;
        private string _text = "", _ext = ".txt";

        public static void ShowFor(ISldWorks app, IModelDoc2 model)
        {
            var part = PartAnalysis.Run(app, model, LibraryStore.Load(), false);
            if (part.Error != null) { SwUtil.Message(app, "CNC Output: " + part.Error, swMessageBoxIcon_e.swMbWarning); return; }
            using (var f = new CncForm(part)) f.ShowDialog(new SwWindow(app));
        }

        private CncForm(PartResult part)
        {
            _part = part;
            Text = "TubeWorks CNC Output — " + part.Name; Width = 900; Height = 600; StartPosition = FormStartPosition.CenterParent;
            _templates = PostStore.All();
            _post.Items.AddRange(_templates.Keys.Cast<object>().ToArray());
            _post.SelectedItem = _templates.ContainsKey(ProductionSettings.Current.Postprocessor) ? ProductionSettings.Current.Postprocessor : _templates.Keys.First();
            _post.SelectedIndexChanged += (o, e) => Render();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
            top.Controls.AddRange(new Control[]
            {
                new Label { Text = "Postprocessor", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _post,
                P3.Btn("Save file…", (o, e) => SaveOut()), P3.Btn("Open postprocessor folder", (o, e) => P3.Open(PostStore.Folder)),
            });
            Controls.Add(_out); Controls.Add(top);
            Render();
        }

        private void Render()
        {
            string key = (string)_post.SelectedItem;
            try
            {
                var t = PostTemplate.Parse(_templates[key]);
                _ext = t.Extension;
                _text = t.Run(_part.Table, _part.Comp, _part.Fields);
                ProductionSettings.Current.Postprocessor = key;
                var sb = new StringBuilder(_text);
                sb.AppendLine().AppendLine("---- not written to the file ----");
                foreach (var n in _part.Comp.Notes) sb.AppendLine("• " + n);
                sb.AppendLine("• Postprocessor templates are generic. Check the output format against your machine's documentation and a test part.");
                _out.Text = sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            }
            catch (Exception ex) { _text = ""; _out.Text = "Postprocessor error: " + ex.Message; }
        }

        private void SaveOut()
        {
            if (_text.Length == 0) return;
            using (var d = new SaveFileDialog { FileName = _part.Name + _ext, Filter = "Machine file (*" + _ext + ")|*" + _ext + "|All files (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(d.FileName, _text, new UTF8Encoding(false)); try { ProductionSettings.Current.Save(); } catch { } }
        }
    }

    /// <summary>Manufacturing report for the active part: data, checks, cost and stock for a quantity.</summary>
    internal sealed class ReportForm : Form
    {
        private readonly ISldWorks _app;
        private readonly IModelDoc2 _model;
        private readonly NumericUpDown _qty = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1, Width = 80 };

        public static void ShowFor(ISldWorks app, IModelDoc2 model) { using (var f = new ReportForm(app, model)) f.ShowDialog(new SwWindow(app)); }

        private ReportForm(ISldWorks app, IModelDoc2 model)
        {
            _app = app; _model = model;
            Text = "TubeWorks Manufacturing Report"; Width = 460; Height = 170; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            p.Controls.AddRange(new Control[] { new Label { Text = "Quantity", AutoSize = true }, _qty, P3.Btn("Create report…", (o, e) => Create()), P3.Btn("Production settings…", (o, e) => ProductionSettingsForm.ShowFor(this)) });
            Controls.Add(p);
        }

        private void Create()
        {
            Cursor = Cursors.WaitCursor;
            string html;
            PartResult part;
            try
            {
                part = PartAnalysis.Run(_app, _model, LibraryStore.Load(), true);
                if (part.Error != null) { MessageBox.Show(this, part.Error, Text); return; }
                int q = (int)_qty.Value;
                var rep = new ManufacturingReport("Manufacturing report — " + part.Name);
                var stock = PartAnalysis.Stock(part.Preset);
                NestResult nest = null; double? nestCost = null;
                if (stock.Count > 0)
                {
                    nest = StockNesting.Nest(new[] { new NestPart { Name = part.Name, Length = part.Comp.CutLength, Quantity = q } }, stock, ProductionSettings.Current.Saw);
                    nestCost = nest.Errors.Count == 0 ? StockNesting.Cost(nest, stock) : null;
                }
                var cost = CostModel.Estimate(part.Comp, part.Table, q, P3.RatesFor(part.Preset), nestCost);
                rep.Part(part.Name, part.Path.Profile, part.Material, part.Table, part.Comp, part.Issues, cost);
                if (nest != null) rep.Nest(nest, ProductionSettings.Current.Saw); else rep.Para("Stock and nesting: no stock lengths in the tube library for this tube.", "warn");
                html = rep.Html();
            }
            finally { Cursor = Cursors.Default; }
            string dir = string.IsNullOrEmpty(part.PathName) ? Path.GetTempPath() : Path.GetDirectoryName(part.PathName);
            string file = Path.Combine(dir, part.Name + " manufacturing report.html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            P3.Open(file);
            Close();
        }
    }

    /// <summary>Batch processing of a folder of parts: outputs per part, nesting by tube size, one combined report.</summary>
    internal sealed class BatchForm : Form
    {
        private readonly ISldWorks _app;
        private readonly TextBox _folder = new TextBox { Width = 420 };
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly CheckBox _post = new CheckBox { Text = "CNC file", AutoSize = true }, _dxf = new CheckBox { Text = "Unwrapped DXF", AutoSize = true },
            _step = new CheckBox { Text = "STEP (bent)", AutoSize = true }, _table = new CheckBox { Text = "Bend table CSV", AutoSize = true };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 24 };

        public static void ShowFor(ISldWorks app) { using (var f = new BatchForm(app)) f.ShowDialog(new SwWindow(app)); }

        private BatchForm(ISldWorks app)
        {
            _app = app;
            var s = ProductionSettings.Current;
            Text = "TubeWorks Batch & Nesting"; Width = 900; Height = 560; StartPosition = FormStartPosition.CenterParent;
            _folder.Text = s.BatchFolder; _post.Checked = s.BatchPost; _dxf.Checked = s.BatchDxf; _step.Checked = s.BatchStep; _table.Checked = s.BatchTable;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Use", FillWeight = 10 });
            _grid.Columns.Add("Part", "Part file"); _grid.Columns["Part"].ReadOnly = true; _grid.Columns["Part"].FillWeight = 70;
            _grid.Columns.Add("Qty", "Quantity"); _grid.Columns["Qty"].FillWeight = 20;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(4) };
            top.Controls.AddRange(new Control[]
            {
                new Label { Text = "Folder", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _folder, P3.Btn("Browse…", (o, e) => Browse()), P3.Btn("List parts", (o, e) => List()),
                new Label { Text = "Outputs:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _post, _dxf, _step, _table,
                P3.Btn("Production settings…", (o, e) => ProductionSettingsForm.ShowFor(this)), P3.Btn("Run batch", (o, e) => Run()),
            });
            Controls.Add(_grid); Controls.Add(top); Controls.Add(_status);
            if (Directory.Exists(_folder.Text)) List();
        }

        private void Browse()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = _folder.Text })
                if (d.ShowDialog(this) == DialogResult.OK) { _folder.Text = d.SelectedPath; List(); }
        }

        private void List()
        {
            _grid.Rows.Clear();
            if (!Directory.Exists(_folder.Text)) return;
            foreach (var f in Directory.GetFiles(_folder.Text, "*.sldprt").OrderBy(x => x))
                if (!Path.GetFileName(f).StartsWith("~$")) _grid.Rows.Add(true, Path.GetFileName(f), "1");
            _status.Text = $"{_grid.Rows.Count} part file(s). Parts without a TubeWorks tube are reported and skipped.";
        }

        private void Run()
        {
            var s = ProductionSettings.Current;
            s.BatchFolder = _folder.Text; s.BatchPost = _post.Checked; s.BatchDxf = _dxf.Checked; s.BatchStep = _step.Checked; s.BatchTable = _table.Checked;
            try { s.Save(); } catch { }
            string outDir = Path.Combine(_folder.Text, "TubeWorks output");
            Directory.CreateDirectory(outDir);
            var lib = LibraryStore.Load();
            var templates = PostStore.All();
            PostTemplate post = null;
            if (_post.Checked)
            {
                try { post = PostTemplate.Parse(templates.ContainsKey(s.Postprocessor) ? templates[s.Postprocessor] : templates.Values.First()); }
                catch (Exception ex) { MessageBox.Show(this, "Postprocessor error: " + ex.Message, Text); return; }
            }
            var rep = new ManufacturingReport("Batch manufacturing report — " + Path.GetFileName(_folder.Text));
            var results = new List<(PartResult part, int qty)>();
            var problems = new List<string[]>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (!(row.Cells["Use"].Value is bool use) || !use) continue;
                    string file = Path.Combine(_folder.Text, (string)row.Cells["Part"].Value);
                    int qty = int.TryParse(Convert.ToString(row.Cells["Qty"].Value), out int q) && q > 0 ? q : 1;
                    _status.Text = "Processing " + Path.GetFileName(file) + "…"; _status.Refresh();
                    string res = ProcessOne(file, qty, outDir, lib, post, results);
                    if (res != null) problems.Add(new[] { Path.GetFileName(file), res });
                }

                rep.Section("Summary");
                rep.Table(new[] { "Part", "Qty", "Tube", "Bends", "Cut length (mm)", "Errors", "Warnings" },
                    results.Select(r => new[]
                    {
                        r.part.Name, r.qty.ToString(CultureInfo.InvariantCulture),
                        $"{r.part.Path.Profile.OuterDiameter * 1000:0.##} x {r.part.Path.Profile.WallThickness * 1000:0.##}", r.part.Table.Rows.Count.ToString(CultureInfo.InvariantCulture),
                        (r.part.Comp.CutLength * 1000).ToString("0.##", CultureInfo.InvariantCulture),
                        r.part.Issues.Count(i => i.Severity == IssueSeverity.Error).ToString(CultureInfo.InvariantCulture),
                        r.part.Issues.Count(i => i.Severity == IssueSeverity.Warning).ToString(CultureInfo.InvariantCulture),
                    }), 3);
                if (problems.Count > 0) { rep.Para("Parts not processed:", "err"); rep.Table(new[] { "Part", "Reason" }, problems, 2); }

                // Nesting per tube size, material cost from the bars used.
                var nestCostByPart = new Dictionary<PartResult, double?>();
                foreach (var g in results.GroupBy(r => (Math.Round(r.part.Path.Profile.OuterDiameter, 6), Math.Round(r.part.Path.Profile.WallThickness, 6))))
                {
                    var preset = g.First().part.Preset;
                    var stock = PartAnalysis.Stock(preset);
                    rep.Section($"Tube {g.Key.Item1 * 1000:0.##} x {g.Key.Item2 * 1000:0.##}" + (preset != null ? $" ({preset.Name})" : ""));
                    if (stock.Count == 0) { rep.Para("No stock lengths in the tube library for this tube: not nested.", "warn"); foreach (var r in g) nestCostByPart[r.part] = null; continue; }
                    var nest = StockNesting.Nest(g.Select(r => new NestPart { Name = r.part.Name, Length = r.part.Comp.CutLength, Quantity = r.qty }), stock, s.Saw);
                    rep.Nest(nest, s.Saw);
                    double? total = nest.Errors.Count == 0 ? StockNesting.Cost(nest, stock) : null;
                    double totalLen = g.Sum(r => r.part.Comp.CutLength * r.qty);
                    foreach (var r in g) nestCostByPart[r.part] = total.HasValue ? total * (r.part.Comp.CutLength * r.qty) / totalLen : (double?)null; // shared by length
                }
                foreach (var r in results)
                    rep.Part(r.part.Name, r.part.Path.Profile, r.part.Material, r.part.Table, r.part.Comp, r.part.Issues,
                        CostModel.Estimate(r.part.Comp, r.part.Table, r.qty, P3.RatesFor(r.part.Preset), nestCostByPart.TryGetValue(r.part, out var nc) ? nc : null));
            }
            finally { Cursor = Cursors.Default; }

            string reportFile = Path.Combine(outDir, "Batch manufacturing report.html");
            File.WriteAllText(reportFile, rep.Html(), new UTF8Encoding(false));
            _status.Text = $"Done: {results.Count} part(s) processed, {problems.Count} not processed. Output in {outDir}";
            P3.Open(reportFile);
        }

        /// <summary>Open (silently), analyse and export one part. Returns null on success, else the reason.</summary>
        private string ProcessOne(string file, int qty, string outDir, TubeLibrary lib, PostTemplate post, List<(PartResult, int)> results)
        {
            IModelDoc2 doc = null; bool opened = false;
            try
            {
                doc = _app.GetOpenDocumentByName(file) as IModelDoc2;
                if (doc == null)
                {
                    int err = 0, warn = 0;
                    doc = _app.OpenDoc6(file, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);
                    if (doc == null) return $"could not open (error {err})";
                    opened = true;
                }
                var part = PartAnalysis.Run(_app, doc, lib, true);
                if (part.Error != null) return part.Error;
                string stem = Path.Combine(outDir, part.Name);
                if (post != null) File.WriteAllText(stem + post.Extension, post.Run(part.Table, part.Comp, part.Fields), new UTF8Encoding(false));
                if (_table.Checked) File.WriteAllText(stem + " bend table.csv", part.Table.ToCsv(new BendTableOptions(), part.Name), new UTF8Encoding(true));
                if (_dxf.Checked && part.Path.RootIsBaseTube)
                {
                    var pat = Unwrap.Run(_app, doc, UnwrapSurface.Outside, 0, out string uerr);
                    if (pat != null) File.WriteAllText(stem + " unwrapped.dxf", DxfWriter.Write(pat), Encoding.ASCII);
                    else part.Issues.Add(new ValidationIssue { Severity = IssueSeverity.Warning, Message = "Unwrapped DXF not written: " + uerr });
                }
                if (_step.Checked)
                {
                    int e2 = 0, w2 = 0;
                    if (!doc.Extension.SaveAs(stem + ".step", (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null, ref e2, ref w2))
                        part.Issues.Add(new ValidationIssue { Severity = IssueSeverity.Warning, Message = $"STEP export failed (error {e2})." });
                }
                results.Add((part, qty));
                return null;
            }
            catch (Exception ex) { Log.Error("Batch " + file, ex); return ex.Message; }
            finally { if (opened && doc != null) _app.CloseDoc(doc.GetTitle()); }
        }
    }
}
