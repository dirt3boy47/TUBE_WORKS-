using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Ui
{
    /// <summary>Unwrap Tube Surface: options, then a 1:1 DXF of the developed surface with configurable layers.</summary>
    internal sealed class UnwrapForm : Form
    {
        private readonly ISldWorks _app;
        private readonly IModelDoc2 _model;
        private readonly ComboBox _surface = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly NumericUpDown _seam = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 2, Width = 80 };
        private readonly ComboBox _unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        private readonly Dictionary<PatternLayer, TextBox> _layers = new Dictionary<PatternLayer, TextBox>();

        public static void ShowFor(ISldWorks app, IModelDoc2 model) { using (var f = new UnwrapForm(app, model)) f.ShowDialog(new SwWindow(app)); }

        private UnwrapForm(ISldWorks app, IModelDoc2 model)
        {
            _app = app; _model = model;
            Text = "TubeWorks Unwrap Tube Surface"; Width = 460; Height = 470; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            _surface.Items.AddRange(new object[] { "Outside surface", "Inside surface", "Nominal neutral surface (mean diameter)" }); _surface.SelectedIndex = 0;
            _unit.Items.AddRange(new object[] { "mm", "inch" }); _unit.SelectedIndex = (int)UserSettings.Current.TableUnit;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8), AutoScroll = true };
            void Row(string label, Control c) { t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }); t.Controls.Add(c); }
            Row("Reference surface", _surface);
            Row("Seam / start angle (deg)", _seam);
            Row("DXF unit (scale 1:1)", _unit);
            var defaults = new DxfLayerSettings();
            foreach (PatternLayer l in Enum.GetValues(typeof(PatternLayer)))
            {
                var tb = new TextBox { Text = defaults.Names[l], Width = 200 };
                _layers[l] = tb;
                Row("Layer: " + l, tb);
            }
            Row("", new Label { Text = "Seam angle is measured about the tube axis from the reference\ndirection used for bend plane rotation.", AutoSize = true, ForeColor = Color.DimGray });
            var ok = new Button { Text = "Export DXF…", AutoSize = true };
            ok.Click += (o, e) => Export(false);
            Row("", ok);
            var dwg = new Button { Text = "Export DWG… (through SOLIDWORKS)", AutoSize = true };
            dwg.Click += (o, e) => Export(true);
            Row("", dwg);
            Controls.Add(t);
        }

        private void Export(bool dwg)
        {
            var surface = (UnwrapSurface)Math.Max(0, _surface.SelectedIndex);
            Pattern2D pat;
            Cursor = Cursors.WaitCursor;
            try { pat = Unwrap.Run(_app, _model, surface, Angles.ToRad((double)_seam.Value), out string err); if (pat == null) { MessageBox.Show(this, err, Text); return; } }
            finally { Cursor = Cursors.Default; }

            var settings = new DxfLayerSettings { Unit = _unit.SelectedIndex == 1 ? LengthUnit.Inch : LengthUnit.Millimetre };
            foreach (var kv in _layers) settings.Names[kv.Key] = string.IsNullOrWhiteSpace(kv.Value.Text) ? settings.Names[kv.Key] : kv.Value.Text.Trim();
            using (var d = new SaveFileDialog { Filter = dwg ? "DWG (*.dwg)|*.dwg" : "DXF R12 (*.dxf)|*.dxf", FileName = Path.GetFileNameWithoutExtension(_model.GetTitle()) + (dwg ? " unwrapped.dwg" : " unwrapped.dxf") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (dwg)
                {
                    if (settings.Unit == LengthUnit.Inch) { MessageBox.Show(this, "DWG export is written in millimetres at 1:1 (the drawing sheet unit). Choose mm.", Text); return; }
                    if (!DwgExport.Export(_app, pat, settings, d.FileName, out string derr)) { MessageBox.Show(this, derr, Text); return; }
                }
                else File.WriteAllText(d.FileName, DxfWriter.Write(pat, settings), Encoding.ASCII);
            }
            string msg = string.Format(CultureInfo.InvariantCulture, "Exported at 1:1 in {0}. Pattern {1:0.###} × {2:0.###} {0}.",
                settings.Unit == LengthUnit.Inch ? "inch" : "mm", pat.Length * (settings.Unit == LengthUnit.Inch ? 1 / 0.0254 : 1000), pat.Width * (settings.Unit == LengthUnit.Inch ? 1 / 0.0254 : 1000));
            if (pat.Warnings.Count > 0) msg += "\n\nWarnings:\n" + string.Join("\n", pat.Warnings.Select(w => "• " + w));
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, pat.Warnings.Count > 1 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }

    /// <summary>Tube library editor: presets with profile, material, stock lengths, tooling radii and limits. Import/export XML or CSV.</summary>
    internal sealed class LibraryForm : Form
    {
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
        private TubeLibrary _lib;

        public static void ShowFor(ISldWorks app) { using (var f = new LibraryForm()) f.ShowDialog(new SwWindow(app)); }

        private LibraryForm()
        {
            Text = "TubeWorks Tube Library  (" + LibraryStore.FilePath + ")"; Width = 1200; Height = 500; StartPosition = FormStartPosition.CenterParent;
            _lib = LibraryStore.Load();
            var head = new TubeLibrary().ToCsv().Split('\n')[0].Trim().Split(',');
            foreach (var h in head) _grid.Columns.Add(h, h);
            Fill(_lib);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
            top.Controls.AddRange(new Control[]
            {
                Btn("Save", (o, e) => Save()), Btn("Import…", (o, e) => Import()), Btn("Export…", (o, e) => Export()),
                new Label { Text = "Values in mm and degrees. Lists: separate with ';'. Leave a limit empty when it is not known: then it is not checked.", AutoSize = true, Padding = new Padding(8, 6, 0, 0) },
            });
            Controls.Add(_grid); Controls.Add(top);
        }

        private static Button Btn(string t, EventHandler h) { var b = new Button { Text = t, AutoSize = true }; b.Click += h; return b; }

        /// <summary>The grid uses the CSV columns, so the CSV codec does all unit conversion and validation.</summary>
        private void Fill(TubeLibrary lib)
        {
            _grid.Rows.Clear();
            foreach (var p in lib.Presets)
            {
                var one = new TubeLibrary(); one.Presets.Add(p);
                var csvRow = one.ToCsv().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)[1];
                var cells = ParseRow(csvRow);
                _grid.Rows.Add(cells.Cast<object>().ToArray());
            }
        }

        private static List<string> ParseRow(string line)
        {
            var cells = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q) { if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (ch == '"') q = false; else sb.Append(ch); }
                else if (ch == '"') q = true; else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        private TubeLibrary FromGrid()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", _grid.Columns.Cast<DataGridViewColumn>().Select(c => Q(c.HeaderText))));
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.IsNewRow) continue;
                sb.AppendLine(string.Join(",", r.Cells.Cast<DataGridViewCell>().Select(c => Q(Convert.ToString(c.Value, CultureInfo.InvariantCulture) ?? ""))));
            }
            return TubeLibrary.FromCsv(sb.ToString());
        }

        private static string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        private bool Validate(TubeLibrary lib)
        {
            var e = lib.Check();
            if (e.Count == 0) return true;
            MessageBox.Show(this, "The library has errors and was not saved:\n" + string.Join("\n", e), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void Save()
        {
            try
            {
                var lib = FromGrid();
                if (!Validate(lib)) return;
                LibraryStore.Save(lib);
                _lib = lib;
                MessageBox.Show(this, $"Saved {lib.Presets.Count} preset(s).", Text);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void Import()
        {
            using (var d = new OpenFileDialog { Filter = "Tube library (*.xml;*.csv)|*.xml;*.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    TubeLibrary lib;
                    if (d.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) lib = TubeLibrary.FromCsv(File.ReadAllText(d.FileName));
                    else using (var s = File.OpenRead(d.FileName)) lib = TubeLibrary.ReadXml(s);
                    var current = FromGrid();
                    var names = new HashSet<string>(current.Presets.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
                    int added = 0, skipped = 0;
                    foreach (var p in lib.Presets) { if (names.Add(p.Name)) { current.Presets.Add(p); added++; } else skipped++; }
                    Fill(current);
                    MessageBox.Show(this, $"Imported {added} preset(s); {skipped} skipped because the name exists. Click Save to keep them.", Text);
                }
                catch (Exception ex) { MessageBox.Show(this, "Import failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void Export()
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|XML (*.xml)|*.xml", FileName = "tube-library.csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var lib = FromGrid();
                    if (d.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) using (var s = File.Create(d.FileName)) lib.WriteXml(s);
                    else File.WriteAllText(d.FileName, lib.ToCsv(), new UTF8Encoding(true));
                }
                catch (Exception ex) { MessageBox.Show(this, "Export failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}

namespace TubeWorks.AddIn.Ui
{
    /// <summary>Options for the automatic manufacturing drawing.</summary>
    internal sealed class DrawingForm : System.Windows.Forms.Form
    {
        private readonly SolidWorks.Interop.sldworks.ISldWorks _app;
        private readonly System.Windows.Forms.ComboBox _paper = new System.Windows.Forms.ComboBox { DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList, Width = 120 };
        private readonly System.Windows.Forms.ComboBox _proj = new System.Windows.Forms.ComboBox { DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList, Width = 220 };
        private readonly System.Windows.Forms.ComboBox _surface = new System.Windows.Forms.ComboBox { DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList, Width = 220 };
        private readonly System.Windows.Forms.CheckBox _dev = new System.Windows.Forms.CheckBox { Text = "Developed surface pattern on sheet 2", Checked = true, AutoSize = true };
        private readonly System.Windows.Forms.ComboBox _template = new System.Windows.Forms.ComboBox { Width = 330, DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown };
        private Sw.DrawingOptions _result;

        public static Sw.DrawingOptions Ask(SolidWorks.Interop.sldworks.ISldWorks app)
        { using (var f = new DrawingForm(app, null)) { f.ShowDialog(new SwWindow(app)); return f._result; } }

        /// <summary>Drawing templates (*.drwdot) in the SOLIDWORKS template folders (Tools > Options > File Locations > Document Templates).</summary>
        private static System.Collections.Generic.List<string> FindTemplates(SolidWorks.Interop.sldworks.ISldWorks app)
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                string folders = app.GetUserPreferenceStringValue((int)SolidWorks.Interop.swconst.swUserPreferenceStringValue_e.swFileLocationsDocumentTemplates) ?? "";
                foreach (var dir in folders.Split(';'))
                    if (System.IO.Directory.Exists(dir.Trim()))
                        list.AddRange(System.IO.Directory.GetFiles(dir.Trim(), "*.drwdot", System.IO.SearchOption.AllDirectories));
            }
            catch (System.Exception ex) { Log.Error("Find templates", ex); }
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(list, System.StringComparer.OrdinalIgnoreCase));
        }

        private DrawingForm(SolidWorks.Interop.sldworks.ISldWorks app, SolidWorks.Interop.sldworks.IModelDoc2 model)
        {
            _app = app;
            Text = "TubeWorks Drawing"; Width = 600; Height = 340; FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog; StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            _paper.Items.AddRange(new object[] { "From the template", "A4", "A3", "A2", "A1" }); _paper.SelectedIndex = 0;
            _proj.Items.AddRange(new object[] { "First angle (ISO)", "Third angle" }); _proj.SelectedIndex = 0;
            _surface.Items.AddRange(new object[] { "Outside surface", "Inside surface", "Neutral surface" }); _surface.SelectedIndex = 0;
            foreach (var t in FindTemplates(app)) _template.Items.Add(t);
            string last = UserSettings.Current.LastDrawingTemplate;
            _template.Text = !string.IsNullOrEmpty(last) && System.IO.File.Exists(last) ? last
                : app.GetUserPreferenceStringValue((int)SolidWorks.Interop.swconst.swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            var browse = new System.Windows.Forms.Button { Text = "Browse…", AutoSize = true };
            browse.Click += (o, e) =>
            {
                using (var d = new System.Windows.Forms.OpenFileDialog { Filter = "SOLIDWORKS drawing template (*.drwdot)|*.drwdot", FileName = _template.Text })
                    if (d.ShowDialog(this) == System.Windows.Forms.DialogResult.OK) _template.Text = d.FileName;
            };
            var tp = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, Margin = new System.Windows.Forms.Padding(0) };
            tp.Controls.Add(_template); tp.Controls.Add(browse);
            var t2 = new System.Windows.Forms.TableLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, ColumnCount = 2, Padding = new System.Windows.Forms.Padding(8) };
            void Row(string l, System.Windows.Forms.Control c) { t2.Controls.Add(new System.Windows.Forms.Label { Text = l, AutoSize = true }); t2.Controls.Add(c); }
            Row("Drawing template", tp); Row("Sheet size", _paper); Row("Projection", _proj); Row("", _dev); Row("Developed surface", _surface);
            var ok = new System.Windows.Forms.Button { Text = "Create drawing", AutoSize = true };
            ok.Click += (o, e) => Create();
            Row("", ok);
            Row("", new System.Windows.Forms.Label { Text = "\"From the template\" keeps the template's sheet size, border and title block.\nThe part must be saved. A configuration \"TW Straight\" is added for the straightened view.", AutoSize = true, ForeColor = System.Drawing.Color.DimGray });
            Controls.Add(t2);
        }

        private void Create()
        {
            _result = new Sw.DrawingOptions
            {
                Paper = _paper.SelectedIndex <= 0 ? (Sw.DrawingPaper?)null : (Sw.DrawingPaper)(_paper.SelectedIndex - 1), FirstAngle = _proj.SelectedIndex == 0,
                DevelopedSurface = _dev.Checked, Surface = (TubeWorks.Geometry.UnwrapSurface)System.Math.Max(0, _surface.SelectedIndex), Template = _template.Text.Trim(),
            };
            if (_result.Template.Length > 0 && !System.IO.File.Exists(_result.Template))
            { System.Windows.Forms.MessageBox.Show(this, "The template file does not exist: " + _result.Template, Text); _result = null; return; }
            UserSettings.Current.LastDrawingTemplate = _result.Template; UserSettings.Current.Save();
            Close();
        }

        /// <summary>Make the drawing (called after the options window has closed).</summary>
        public static void CreateDrawing(SolidWorks.Interop.sldworks.ISldWorks app, SolidWorks.Interop.sldworks.IModelDoc2 model, Sw.DrawingOptions o)
        {
            SolidWorks.Interop.sldworks.IModelDoc2 doc;
            System.Collections.Generic.List<string> notes; string err;
            try { doc = Sw.DrawingGenerator.Create(app, model, o, out notes, out err); }
            catch (System.Exception ex) { Log.Error("Drawing", ex); doc = null; notes = new System.Collections.Generic.List<string>(); err = ex.Message; }
            if (doc == null) { Sw.SwUtil.Message(app, "TubeWorks: the drawing was not created: " + err, SolidWorks.Interop.swconst.swMessageBoxIcon_e.swMbWarning); return; }
            string msg = "TubeWorks: drawing created. Check it before use: view placement and annotations depend on your template.";
            if (notes.Count > 0) msg += "\n\nNotes:\n" + string.Join("\n", System.Linq.Enumerable.Select(notes, n => "• " + n));
            Sw.SwUtil.Message(app, msg);
        }
    }
}
