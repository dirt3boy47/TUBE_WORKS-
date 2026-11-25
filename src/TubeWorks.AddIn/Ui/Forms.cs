using System;
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
    internal sealed class SwWindow : IWin32Window
    {
        public IntPtr Handle { get; }
        public SwWindow(ISldWorks app)
        {
            try { Handle = new IntPtr(((IFrame)app.Frame()).GetHWndx64()); } catch { Handle = IntPtr.Zero; }
        }
    }

    /// <summary>Bend table window: XYZ and LRA data, configurable conventions, CSV export, custom properties.</summary>
    internal sealed class BendTableForm : Form
    {
        private readonly IModelDoc2 _model;
        private readonly ChainResult _chain;
        private readonly ComboBox _tube = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly ComboBox _unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly ComboBox _rot = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly ComboBox _ang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
        private readonly Label _summary = new Label { Dock = DockStyle.Bottom, Height = 40 };

        public static void ShowFor(ISldWorks app, IModelDoc2 model)
        {
            var chain = TubeFeatureStore.Solve(model);
            if (chain.Paths.Count == 0)
            {
                var err = chain.Features.FirstOrDefault(f => f.Error != null);
                SwUtil.Message(app, err != null ? $"The tube has an error: {err.FeatureName}: {err.Error}" : "No TubeWorks tube in this part.", swMessageBoxIcon_e.swMbWarning);
                return;
            }
            using (var f = new BendTableForm(model, chain)) f.ShowDialog(new SwWindow(app));
        }

        private BendTableForm(IModelDoc2 model, ChainResult chain)
        {
            _model = model; _chain = chain;
            Text = "TubeWorks Bend Table"; Width = 1100; Height = 480; StartPosition = FormStartPosition.CenterParent;
            var s = UserSettings.Current;
            for (int i = 0; i < chain.Paths.Count; i++) _tube.Items.Add($"Tube {i + 1}");
            _tube.SelectedIndex = 0;
            _unit.Items.AddRange(new object[] { "mm", "in" }); _unit.SelectedIndex = (int)s.TableUnit;
            _rot.Items.AddRange(new object[] { "+ = right-hand about feed", "+ = left-hand about feed" }); _rot.SelectedIndex = (int)s.RotationSign;
            _ang.Items.AddRange(new object[] { "Bend angle", "Included angle" }); _ang.SelectedIndex = (int)s.AngleConvention;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
            top.Controls.AddRange(new Control[] { _tube, _unit, _rot, _ang,
                Btn("Export CSV…", (o, e) => Save("bend table", t => t.ToCsv(Options(), _model.GetTitle()))),
                Btn("Export XYZ…", (o, e) => Save("XYZ", t => t.ToXyzCsv(Options()))),
                Btn("Export LRA…", (o, e) => Save("LRA", t => t.ToLraCsv(Options()))),
                Btn("Write custom properties", (o, e) => WriteProperties()) });
            Controls.Add(_grid); Controls.Add(top); Controls.Add(_summary);
            foreach (var c in new[] { _tube, _unit, _rot, _ang }) c.SelectedIndexChanged += (o, e) => Fill();
            Fill();
        }

        private static Button Btn(string text, EventHandler h) { var b = new Button { Text = text, AutoSize = true }; b.Click += h; return b; }

        private BendTableOptions Options() => new BendTableOptions
        {
            Unit = (LengthUnit)Math.Max(0, _unit.SelectedIndex), RotationSign = (RotationSign)Math.Max(0, _rot.SelectedIndex),
            AngleConvention = (AngleConvention)Math.Max(0, _ang.SelectedIndex),
        };

        private TubePath Path0 => _chain.Paths[Math.Max(0, _tube.SelectedIndex)];
        private BendTable Table => BendTable.Build(Path0.Primitives);

        private void Fill()
        {
            var o = Options();
            var s = UserSettings.Current; s.TableUnit = o.Unit; s.RotationSign = o.RotationSign; s.AngleConvention = o.AngleConvention; s.Save();
            // Show exactly what the CSV export writes.
            var lines = Table.ToCsv(o).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var head = lines[0].Split(',');
            _grid.Columns.Clear();
            foreach (var h in head) _grid.Columns.Add(h, h);
            foreach (var l in lines.Skip(1))
            {
                var cells = l.Split(',');
                _grid.Rows.Add(cells.Take(head.Length).Cast<object>().ToArray());
            }
            var p = Path0;
            _summary.Text = $"  Ø {SwUtil.FormatLength(_model, p.Profile.OuterDiameter)} × {SwUtil.FormatLength(_model, p.Profile.WallThickness)} wall.  " +
                            $"Developed length = Σ straight + Σ (CLR × angle in radians) = {SwUtil.FormatLength(_model, Table.TotalDevelopedLength)} (nominal centreline, no compensation).";
        }

        private void Save(string what, Func<BendTable, string> content)
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = System.IO.Path.GetFileNameWithoutExtension(_model.GetTitle()) + " " + what + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, content(Table), new UTF8Encoding(true));
            }
        }

        private void WriteProperties()
        {
            var t = Table; var p = Path0;
            var cpm = _model.Extension.CustomPropertyManager[""];
            void Set(string n, string v) => cpm.Add3(n, (int)swCustomInfoType_e.swCustomInfoText, v, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            Set("TW Outside Diameter", (p.Profile.OuterDiameter * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm");
            Set("TW Wall Thickness", (p.Profile.WallThickness * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm");
            Set("TW Bend Count", t.Rows.Count.ToString(CultureInfo.InvariantCulture));
            Set("TW Developed Length", (t.TotalDevelopedLength * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm");
            MessageBox.Show(this, "Custom properties written: TW Outside Diameter, TW Wall Thickness, TW Bend Count, TW Developed Length.", Text);
        }
    }

    /// <summary>Export of the bent tube or the straight manufacturing blank through the native SOLIDWORKS translators.</summary>
    internal sealed class ExportForm : Form
    {
        private readonly ISldWorks _app;
        private readonly IModelDoc2 _model;
        private readonly ComboBox _state = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ComboBox _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private static readonly string[] Ext = { ".step", ".x_t", ".sldprt", ".csv" };

        public static void ShowFor(ISldWorks app, IModelDoc2 model) { using (var f = new ExportForm(app, model)) f.ShowDialog(new SwWindow(app)); }

        private ExportForm(ISldWorks app, IModelDoc2 model)
        {
            _app = app; _model = model;
            Text = "TubeWorks Export"; Width = 420; Height = 220; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            _state.Items.AddRange(new object[] { "Bent tube geometry", "Straightened 3D tube (developed blank)" }); _state.SelectedIndex = 0;
            _format.Items.AddRange(new object[] { "STEP AP214 (*.step)", "Parasolid (*.x_t)", "SOLIDWORKS part copy (*.sldprt)", "Tube centreline XYZ (*.csv)" }); _format.SelectedIndex = 0;
            var note = new Label { Text = "Planned (Phase 2): DXF/DWG of the unwrapped surface, PDF drawings, CNC postprocessors.", AutoSize = false, Width = 380, Height = 32, ForeColor = Color.DimGray };
            var ok = new Button { Text = "Export…", AutoSize = true }; ok.Click += (o, e) => DoExport();
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
            p.Controls.AddRange(new Control[] { _state, _format, note, ok });
            Controls.Add(p);
        }

        private void DoExport()
        {
            int fi = Math.Max(0, _format.SelectedIndex);
            bool straight = _state.SelectedIndex == 1;
            string ext = Ext[fi];
            using (var d = new SaveFileDialog { Filter = $"{_format.Text}|*{ext}", FileName = System.IO.Path.GetFileNameWithoutExtension(_model.GetTitle()) + (straight ? " straight" : " bent") + ext })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (ext == ".csv")
                {
                    var chain = TubeFeatureStore.Solve(_model);
                    if (chain.Paths.Count == 0) { MessageBox.Show(this, "No valid tube."); return; }
                    var t = BendTable.Build(chain.Paths[0].Primitives);
                    File.WriteAllText(d.FileName, t.ToXyzCsv(new BendTableOptions()), new UTF8Encoding(true));
                    MessageBox.Show(this, "Centreline exported.", Text);
                    return;
                }
                var all = TubeFeatureStore.Scan(_model);
                var flat = all.FirstOrDefault(x => x.Data.Kind == TubeFeatureKind.FlatPattern);
                if (straight && flat == null)
                {
                    MessageBox.Show(this, "Run Flatten Tube once to create the Tube Flat Pattern feature, then export again.", Text);
                    return;
                }
                bool wasSuppressed = flat?.Suppressed ?? true;
                bool needFlat = straight;
                try
                {
                    if (flat != null && wasSuppressed == needFlat)
                    {
                        flat.Feature.SetSuppression2(needFlat ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature : (int)swFeatureSuppressionAction_e.swSuppressFeature,
                            (int)swInConfigurationOpts_e.swThisConfiguration, null);
                        _model.ForceRebuild3(false);
                    }
                    int errors = 0, warnings = 0;
                    bool ok = _model.Extension.SaveAs3(d.FileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null, null, ref errors, ref warnings);
                    MessageBox.Show(this, ok ? $"Exported at 1:1 (model units) to {d.FileName}." : $"SOLIDWORKS export failed (error {errors}, warning {warnings}).", Text);
                }
                finally
                {
                    if (flat != null && SwUtil.IsSuppressed(flat.Feature) != wasSuppressed)
                    {
                        flat.Feature.SetSuppression2(wasSuppressed ? (int)swFeatureSuppressionAction_e.swSuppressFeature : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                            (int)swInConfigurationOpts_e.swThisConfiguration, null);
                        _model.ForceRebuild3(false);
                    }
                }
            }
        }
    }
}
