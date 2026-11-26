using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Features;
using TubeWorks.Geometry;
using BendTable = TubeWorks.Geometry.BendTable;

namespace TubeWorks.AddIn.Sw
{
    public enum DrawingPaper { A4 = 0, A3 = 1, A2 = 2, A1 = 3 }

    internal sealed class DrawingOptions
    {
        /// <summary>Null = keep the sheet size and sheet format of the template.</summary>
        public DrawingPaper? Paper = null;
        public string Template = "";
        public bool FirstAngle = true; // ISO
        public bool DevelopedSurface = true;
        public UnwrapSurface Surface = UnwrapSurface.Outside;
        public double SeamAngle = 0;
    }

    /// <summary>Draws 2D geometry into the active sheet sketch of a drawing, on named layers.</summary>
    internal static class SheetSketch
    {
        public static readonly Dictionary<PatternLayer, int> Colours = new Dictionary<PatternLayer, int>
        {
            // COLORREF 0x00BBGGRR
            [PatternLayer.OuterProfile] = 0x000000, [PatternLayer.InternalCutouts] = 0x0080FF, [PatternLayer.BendLines] = 0x2A2AD0,
            [PatternLayer.Centreline] = 0xC85624, [PatternLayer.Engraving] = 0xC000C0, [PatternLayer.Construction] = 0x808080, [PatternLayer.Text] = 0x0080C0,
        };

        public static void EnsureLayers(IModelDoc2 drawing, DxfLayerSettings names)
        {
            var lm = drawing.GetLayerManager() as ILayerMgr;
            if (lm == null) return;
            foreach (PatternLayer l in Enum.GetValues(typeof(PatternLayer)))
                if (lm.GetLayer(names.Names[l]) == null) lm.AddLayer(names.Names[l], "TubeWorks " + l, Colours[l], 0, 0);
        }

        /// <summary>
        /// Draw a pattern with its lower-left corner at (x0, y0) on the sheet (metres of paper), at <paramref name="scale"/>.
        /// SOLIDWORKS multiplies sheet-sketch coordinates by the sheet scale, so sketch points are divided by
        /// <paramref name="sheetScale"/> (notes are placed in paper coordinates and are not). The active layer is restored.
        /// </summary>
        public static void Draw(IModelDoc2 drawing, Pattern2D pat, double x0, double y0, double scale, DxfLayerSettings names, double sheetScale = 1.0)
        {
            var sm = drawing.SketchManager;
            var lm = drawing.GetLayerManager() as ILayerMgr;
            string previousLayer = null;
            try { previousLayer = lm?.GetCurrentLayer(); } catch { }
            EnsureLayers(drawing, names);
            double inv = 1.0 / sheetScale;
            bool addToDb = sm.AddToDB, display = sm.DisplayWhenAdded;
            sm.AddToDB = true; sm.DisplayWhenAdded = false;
            try
            {
                foreach (var g in pat.Polylines.GroupBy(p => p.Layer))
                {
                    lm?.SetCurrentLayer(names.Names[g.Key]);
                    foreach (var pl in g)
                    {
                        var pts = pl.Points;
                        for (int i = 1; i < pts.Count; i++) Line(sm, pts[i - 1], pts[i], x0, y0, scale, inv);
                        if (pl.Closed && pts.Count > 2) Line(sm, pts[pts.Count - 1], pts[0], x0, y0, scale, inv);
                    }
                }
                foreach (var c in pat.Circles)
                {
                    lm?.SetCurrentLayer(names.Names[c.Layer]);
                    sm.CreateCircleByRadius((x0 + c.Centre.X * scale) * inv, (y0 + c.Centre.Y * scale) * inv, 0, c.Radius * scale * inv);
                }
            }
            finally { sm.AddToDB = addToDb; sm.DisplayWhenAdded = display; }
            foreach (var t in pat.Texts)
            {
                lm?.SetCurrentLayer(names.Names[t.Layer]);
                Note(drawing, t.Text, x0 + t.At.X * scale, y0 + t.At.Y * scale, Math.Max(0.0025, t.Height * scale));
            }
            try { if (lm != null) lm.SetCurrentLayer(string.IsNullOrEmpty(previousLayer) ? "" : previousLayer); } catch { }
        }

        private static void Line(ISketchManager sm, Point2 a, Point2 b, double x0, double y0, double k, double inv) =>
            sm.CreateLine((x0 + a.X * k) * inv, (y0 + a.Y * k) * inv, 0, (x0 + b.X * k) * inv, (y0 + b.Y * k) * inv, 0);

        public static void Note(IModelDoc2 doc, string text, double x, double y, double height = 0.0035)
        {
            var note = doc.InsertNote(text) as INote;
            if (note == null) return;
            try { note.SetHeight(height); } catch { }
            (note.GetAnnotation() as IAnnotation)?.SetPosition2(x, y, 0);
        }
    }

    /// <summary>Automatic manufacturing drawing of a TubeWorks tube.</summary>
    internal static class DrawingGenerator
    {
        public const string StraightConfig = "TW Straight";

        public static (double w, double h, int code) Paper(DrawingPaper p) =>
            p == DrawingPaper.A4 ? (0.297, 0.210, (int)swDwgPaperSizes_e.swDwgPaperA4size)
          : p == DrawingPaper.A3 ? (0.420, 0.297, (int)swDwgPaperSizes_e.swDwgPaperA3size)
          : p == DrawingPaper.A2 ? (0.594, 0.420, (int)swDwgPaperSizes_e.swDwgPaperA2size)
          : (0.841, 0.594, (int)swDwgPaperSizes_e.swDwgPaperA1size);

        /// <summary>Preferred scales (ISO 5455): enlargement 5:1 and 2:1, full size, reductions down to 1:200.</summary>
        private static readonly double[] Scales = { 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005 };

        private static double PickScale(double modelSize, double space)
        {
            foreach (var s in Scales) if (modelSize * s <= space) return s;
            return Scales[Scales.Length - 1];
        }

        private static string ScaleText(double s) => s >= 1 ? $"{s:0.##}:1" : $"1:{1 / s:0.##}";

        public static IModelDoc2 Create(ISldWorks app, IModelDoc2 part, DrawingOptions o, out List<string> notes, out string error)
        {
            error = null; notes = new List<string>();
            string partPath = part.GetPathName();
            if (string.IsNullOrEmpty(partPath)) { error = "Save the part first. The drawing views reference the part file."; return null; }
            var all = TubeFeatureStore.Scan(part);
            if (StateFeatureBase.ActiveUnbends(all).Any()) { error = "An Unbend is active. Use Rebend first."; return null; }
            var chain = TubeFeatureStore.Solve(all);
            var path = chain.Paths.FirstOrDefault(p => p.RootIsBaseTube);
            if (path == null) { error = "The drawing needs a valid tube that starts with a Base Tube feature."; return null; }
            TubeFeatureStore.EnsureBentState(part);
            all = TubeFeatureStore.Scan(part);

            // Developed surface (computed before the configuration change, from the bent model).
            Pattern2D pattern = null;
            if (o.DevelopedSurface)
            {
                pattern = Unwrap.Run(app, part, o.Surface, o.SeamAngle, out string uerr);
                if (pattern == null) notes.Add("Developed surface not drawn: " + uerr); else notes.AddRange(pattern.Warnings);
            }

            bool straightOk = EnsureStraightConfiguration(app, part, path, out string cerr);
            if (!straightOk) notes.Add("Straightened view not drawn: " + cerr);

            string template = string.IsNullOrWhiteSpace(o.Template) ? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing) : o.Template;
            var (W0, H0, code0) = Paper(o.Paper ?? DrawingPaper.A3);
            var doc = app.NewDocument(template, code0, W0, H0) as IModelDoc2;
            if (doc == null) { error = "SOLIDWORKS could not create a drawing from the template: " + template; return null; }
            var drw = (IDrawingDoc)doc;
            var sheet = drw.GetCurrentSheet() as ISheet;
            string sheetName = sheet?.GetName() ?? "Sheet1";
            if (o.Paper.HasValue)
            {
                // Chosen size: the standard SOLIDWORKS sheet format of that size (template enum value = paper enum value for A4–A1).
                drw.SetupSheet5(sheetName, code0, code0, 1, 1, o.FirstAngle, "", W0, H0, "Default", true);
                sheet = drw.GetCurrentSheet() as ISheet;
            }
            double W = W0, H = H0;
            if (sheet != null) { double w = 0, h = 0; sheet.GetSize(ref w, ref h); if (w > 0 && h > 0) { W = w; H = h; } }
            var props = SwUtil.D((object)sheet?.GetProperties2());
            int code = props != null && props.Length > 1 ? (int)props[0] : code0, templIn = props != null && props.Length > 1 ? (int)props[1] : code0;
            string sheetFormat = sheet?.GetTemplateName() ?? "";

            var table = BendTable.Build(path.Primitives);
            var samples = path.Primitives.SelectMany(p => p.Sample(Math.Max(path.Profile.OuterDiameter, path.DevelopedLength / 400)).Select(x => x.point)).ToList();
            double r = path.Profile.OuterRadius;
            double ex = samples.Max(p => p.X) - samples.Min(p => p.X) + 2 * r;
            double ey = samples.Max(p => p.Y) - samples.Min(p => p.Y) + 2 * r;
            double ez = samples.Max(p => p.Z) - samples.Min(p => p.Z) + 2 * r;

            // ---------------- Sheet 1: views, centreline, bend table, notes
            double scale = PickScale(Math.Max(ex, Math.Max(ey, ez)), Math.Min(W * 0.24, H * 0.32));
            sheet?.SetScale(scale >= 1 ? scale : 1, scale >= 1 ? 1 : 1 / scale, true, true);

            // First-angle arrangement (ISO): the top view below the front view, the view from the right on the left.
            double fx = W * 0.30, fy = H * 0.62;
            Place(drw, partPath, "*Front", fx, fy, "FRONT", doc, notes);
            Place(drw, partPath, "*Top", fx, o.FirstAngle ? H * 0.27 : H * 0.90, "TOP", doc, notes);
            Place(drw, partPath, "*Right", o.FirstAngle ? W * 0.10 : W * 0.50, fy, "RIGHT", doc, notes);
            Place(drw, partPath, "*Isometric", W * 0.52, H * 0.62, "ISOMETRIC", doc, notes);

            // Centreline view: isometric projection of the centreline, drawn as a sheet sketch.
            drw.EditSheet();
            DrawCentreline(doc, path, table, W * 0.43, H * 0.30, W * 0.18, H * 0.20, scale);

            // Bend table.
            InsertBendTable(drw, table, W * 0.66, H * 0.95);

            var part2 = part as IPartDoc;
            string mat = part2?.GetMaterialPropertyName2("", out string _) ?? "";
            var prof = path.Profile;
            string I(double m) => (m * 1000).ToString("0.###", CultureInfo.InvariantCulture);
            SheetSketch.Note(doc,
                $"TUBE: OD {I(prof.OuterDiameter)} x {I(prof.WallThickness)} WALL (ID {I(prof.InnerDiameter)}) mm\n" +
                $"MATERIAL: {(string.IsNullOrEmpty(mat) ? "NOT SET" : mat)}\n" +
                $"REQUIRED STOCK LENGTH (DEVELOPED, NOMINAL CENTRELINE): {I(table.TotalDevelopedLength)} mm\n" +
                $"BENDS: {table.Rows.Count}   OVERALL ENVELOPE: {I(ex)} x {I(ey)} x {I(ez)} mm (X x Y x Z)\n" +
                $"SCALE {ScaleText(scale)}   {(o.FirstAngle ? "FIRST ANGLE PROJECTION (ISO)" : "THIRD ANGLE PROJECTION")}\n" +
                "NO STRETCH OR SPRINGBACK COMPENSATION INCLUDED. GENERATED BY TUBEWORKS.",
                W * 0.04, H * 0.30);

            // ---------------- Sheet 2: straightened tube, bending layout of the straight bar, developed surface
            {
                var lib = LibraryStore.Load();
                var preset = lib.Match(path.Profile);
                var comp = Compensation.Apply(table, preset?.Compensation);
                double barLen = comp.CutLength;
                double s2 = PickScale(barLen, W * 0.80);
                drw.NewSheet3("Straightened", code, templIn, s2 >= 1 ? s2 : 1, s2 >= 1 ? 1 : 1 / s2, o.FirstAngle, sheetFormat, W, H, "");
                if (straightOk)
                {
                    var up = TubeUnbender.ForFeature(path, null).UnbentPath;
                    string view = BestSideView(up[0].StartTangent);
                    var v = Place(drw, partPath, view, W * 0.5, H * 0.84, "STRAIGHTENED TUBE (" + StraightConfig + ")", doc, notes);
                    if (v != null) { v.ReferencedConfiguration = StraightConfig; }
                }
                drw.EditSheet();
                // Bending layout of the bar: marks, rotations and angles; 3 mm text on paper.
                var layout = BendingLayout.Build(table, prof.OuterDiameter, comp, 0.003 / s2);
                SheetSketch.Draw(doc, layout, W * 0.08, H * 0.62, s2, new DxfLayerSettings(), s2);
                SheetSketch.Note(doc, $"BENDING LAYOUT OF THE STRAIGHT BAR  SCALE {ScaleText(s2)}  (marks measured from the bar start = feed end, at the bend start/tangent point)",
                    W * 0.08, H * 0.735 + prof.OuterDiameter * s2, 0.0035);
                InsertSequenceTable(drw, BendingLayout.Steps(table, comp), comp, W * 0.08, H * 0.52);
                SheetSketch.Note(doc, "ROTATE = turn the tube about its axis before the bend, from the previous bend plane (" +
                    (UserSettings.Current.RotationSign == RotationSign.RightHandAboutFeed ? "+ = right-hand about the feed direction" : "+ = left-hand about the feed direction") + ").\n" +
                    string.Join("\n", comp.Notes), W * 0.55, H * 0.52, 0.0025);
                if (pattern != null)
                {
                    double ps = PickScale(Math.Max(pattern.Length / (W * 0.80), pattern.Width / (H * 0.22)), 1);
                    SheetSketch.Draw(doc, pattern, W * 0.08, H * 0.10, ps, new DxfLayerSettings(), s2);
                    SheetSketch.Note(doc, $"DEVELOPED {o.Surface.ToString().ToUpperInvariant()} SURFACE  SCALE {ScaleText(ps)}  (1:1 file: Unwrap Tube Surface)", W * 0.08, H * 0.10 + pattern.Width * ps + 0.008);
                }
                drw.ActivateSheet(sheetName);
            }
            doc.ForceRebuild3(true);
            doc.ViewZoomtofit2();
            return doc;
        }

        private static IView Place(IDrawingDoc drw, string partPath, string viewName, double x, double y, string label, IModelDoc2 doc, List<string> notes)
        {
            var v = drw.CreateDrawViewFromModelView3(partPath, viewName, x, y, 0) as IView;
            if (v == null) { notes.Add($"View {viewName} could not be created."); return null; }
            try { v.SetDisplayMode3(false, (int)swDisplayMode_e.swHIDDEN, false, true); } catch { }
            var box = SwUtil.D((object)v.GetOutline()); // xmin, ymin, xmax, ymax on the sheet
            drw.EditSheet();
            SheetSketch.Note(doc, label, box != null && box.Length >= 4 ? box[0] : x, box != null && box.Length >= 4 ? box[1] - 0.006 : y - 0.04, 0.0035);
            return v;
        }

        /// <summary>Standard view whose viewing direction is most nearly perpendicular to the tube axis.</summary>
        private static string BestSideView(Vec3 axis)
        {
            var c = new[] { ("*Front", Math.Abs(axis.Z)), ("*Top", Math.Abs(axis.Y)), ("*Right", Math.Abs(axis.X)) };
            return c.OrderBy(t => t.Item2).First().Item1;
        }

        private static void DrawCentreline(IModelDoc2 doc, TubePath path, BendTable table, double x0, double y0, double w, double h, double sheetScale)
        {
            // Isometric projection: X' = (x - z)·cos30°, Y' = y + (x + z)·sin30°.
            Point2 P(Vec3 v) => new Point2((v.X - v.Z) * Math.Cos(Math.PI / 6), v.Y + (v.X + v.Z) * 0.5);
            var pts = path.Primitives.SelectMany(p => p.Sample(Math.Max(1e-4, p.Length / 24)).Select(x => P(x.point))).ToList();
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double k = Math.Min(w / Math.Max(1e-9, maxX - minX), h / Math.Max(1e-9, maxY - minY));
            var pat = new Pattern2D();
            var pl = new PatternPolyline { Layer = PatternLayer.Centreline };
            pl.Points.AddRange(pts.Select(p => new Point2(p.X - minX, p.Y - minY)));
            pat.Polylines.Add(pl);
            foreach (var row in table.Rows)
            {
                var a = path.Primitives.OfType<ArcPrimitive>().ElementAt(row.Number - 1);
                var q = P(a.PointAtAngle(a.Angle / 2));
                pat.Texts.Add(new PatternText { Layer = PatternLayer.Text, At = new Point2(q.X - minX, q.Y - minY), Height = 0.003 / k, Text = "B" + row.Number });
            }
            pat.Simplify(1e-9);
            SheetSketch.Draw(doc, pat, x0, y0, k, new DxfLayerSettings(), sheetScale);
            SheetSketch.Note(doc, "CENTRELINE (ISOMETRIC PROJECTION, NOT TO SCALE)", x0, y0 - 0.006, 0.003);
        }

        private static void InsertBendTable(IDrawingDoc drw, BendTable t, double x, double y)
        {
            var o = new BendTableOptions { Unit = LengthUnit.Millimetre, RotationSign = UserSettings.Current.RotationSign, AngleConvention = UserSettings.Current.AngleConvention, Decimals = 2 };
            var lines = t.ToCsv(o).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var head = lines[0].Split(',');
            int[] cols = { 0, 1, 2, 3, 4, 5, 12, 13 }; // bend, feed, CLR, angle, rotation, direction, developed bend, cumulative
            int rows = t.Rows.Count + 2;
            var tab = drw.InsertTableAnnotation2(false, x, y, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft, "", rows, cols.Length) as ITableAnnotation;
            if (tab == null) return;
            for (int c = 0; c < cols.Length; c++) tab.Text[0, c] = head[cols[c]].ToUpperInvariant();
            for (int r = 0; r < t.Rows.Count; r++)
            {
                var cells = lines[r + 1].Split(',');
                for (int c = 0; c < cols.Length; c++) tab.Text[r + 1, c] = cells[cols[c]];
            }
            tab.Text[rows - 1, 0] = "TOTAL";
            tab.Text[rows - 1, 1] = (t.TotalDevelopedLength * 1000).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void InsertSequenceTable(IDrawingDoc drw, List<BendingStep> steps, CompensatedData comp, double x, double y)
        {
            string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            var tab = drw.InsertTableAnnotation2(false, x, y, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft, "", steps.Count + 2, 7) as ITableAnnotation;
            if (tab == null) return;
            string[] head = { "STEP", "MARK AT (MM)", "BEND CENTRE AT (MM)", "ROTATE (°)", "BEND (°)", "COMMAND (°)", "CLR (MM)" };
            for (int c = 0; c < head.Length; c++) tab.Text[0, c] = head[c];
            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                string[] row = { "B" + st.Number, F(st.MarkStart * 1000), F(st.MarkCentre * 1000), F(st.RotationDeg), F(st.DesignDeg), F(st.CommandDeg), F(st.Clr * 1000) };
                for (int c = 0; c < row.Length; c++) tab.Text[i + 1, c] = row[c];
            }
            tab.Text[steps.Count + 1, 0] = "CUT";
            tab.Text[steps.Count + 1, 1] = F(comp.CutLength * 1000);
        }

        /// <summary>Configuration "TW Straight" in which only the Tube Flat Pattern is unsuppressed.</summary>
        private static bool EnsureStraightConfiguration(ISldWorks app, IModelDoc2 part, TubePath path, out string error)
        {
            error = null;
            try
            {
                string active = (part.GetActiveConfiguration() as IConfiguration)?.Name ?? "";
                var all = TubeFeatureStore.Scan(part);
                var flat = all.FirstOrDefault(x => x.Data.Kind == TubeFeatureKind.FlatPattern);
                if (flat == null)
                {
                    var f = TubeFeatureStore.InsertFlatPattern(part, all, path);
                    if (f == null) { error = "the Tube Flat Pattern could not be created."; return false; }
                    flat = TubeFeatureStore.TryRead(f);
                }
                var names = SwUtil.Arr<string>((object)part.GetConfigurationNames()).ToList();
                if (!names.Contains(StraightConfig))
                {
                    if (part.ConfigurationManager.AddConfiguration(StraightConfig, "TubeWorks straightened tube", "", 0, "", "") == null)
                    { error = "the configuration could not be created."; return false; }
                    names.Add(StraightConfig);
                }
                var others = names.Where(n => n != StraightConfig).ToArray();
                if (others.Length > 0) flat.Feature.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swSpecifyConfiguration, others);
                flat.Feature.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swSpecifyConfiguration, new[] { StraightConfig });
                part.ShowConfiguration2(string.IsNullOrEmpty(active) || active == StraightConfig ? others.FirstOrDefault() ?? active : active);
                part.ForceRebuild3(false);
                return true;
            }
            catch (Exception ex) { Log.Error("Straight configuration", ex); error = ex.Message; return false; }
        }
    }

    /// <summary>DWG export of the developed surface: a 1:1 drawing sheet with the pattern on named layers, saved with SOLIDWORKS' DWG export.</summary>
    internal static class DwgExport
    {
        public static bool Export(ISldWorks app, Pattern2D pat, DxfLayerSettings names, string fileName, out string error)
        {
            error = null;
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            double margin = 0.02;
            double w = pat.Length + 2 * margin, h = pat.Width + 4 * margin;
            var doc = app.NewDocument(template, (int)swDwgPaperSizes_e.swDwgPapersUserDefined, w, h) as IModelDoc2;
            if (doc == null) { error = "SOLIDWORKS could not create a temporary drawing."; return false; }
            try
            {
                var drw = (IDrawingDoc)doc;
                (drw.GetCurrentSheet() as ISheet)?.SetProperties2((int)swDwgPaperSizes_e.swDwgPapersUserDefined, (int)swDwgTemplates_e.swDwgTemplateNone, 1, 1, true, w, h, true);
                drw.EditSheet();
                SheetSketch.Draw(doc, pat, margin, 2 * margin, 1.0, names);
                int errs = 0, warns = 0;
                bool ok = doc.Extension.SaveAs(fileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null, ref errs, ref warns);
                if (!ok) error = $"SOLIDWORKS DWG export failed (error {errs}, warning {warns}).";
                return ok;
            }
            finally { app.CloseDoc(doc.GetTitle()); }
        }
    }
}
