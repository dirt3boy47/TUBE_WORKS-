using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Features;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>Tube Unbend PropertyManager: which bends to straighten and which segment stays fixed.</summary>
    public sealed class UnbendPage : PageBase
    {
        private const int IdAll = 10, IdList = 11, IdFixed = 12, IdInfo = 13;
        private readonly TubeFeatureData _data;
        private readonly TubePath _path;
        private readonly List<FeatureSolution> _bends;
        private readonly List<(string featureId, int index, string text)> _segments;
        private readonly IFeature _editFeature;
        private readonly IMacroFeatureData _editDef;
        private readonly IBody2 _body;
        private readonly Preview _preview;
        private readonly BodyBuilder _builder;
        private IPropertyManagerPageCheckbox _all;
        private IPropertyManagerPageListbox _list;
        private IPropertyManagerPageCombobox _fixed;
        private IPropertyManagerPageLabel _info;
        private List<CutInfo> _cuts = new List<CutInfo>();

        private UnbendPage(ISldWorks app, IModelDoc2 model, TubeFeatureData data, TubePath path, ChainResult chain, IBody2 body, IFeature f, IMacroFeatureData md) : base(app, model)
        {
            _data = data; _path = path; _body = body; _editFeature = f; _editDef = md;
            _preview = new Preview(model);
            _builder = new BodyBuilder(app) { VerifyGeometry = UserSettings.Current.VerifyGeometry };
            var ids = new HashSet<string>(path.FeatureIds);
            _bends = chain.Features.Where(x => ids.Contains(x.Data.Id) && x.Data.Kind == TubeFeatureKind.Bend && x.IsValid).ToList();
            _segments = new List<(string, int, string)>();
            foreach (var fs in chain.Features.Where(x => ids.Contains(x.Data.Id) && x.IsValid))
                for (int i = 0; i < fs.Primitives.Count; i++)
                    if (fs.Primitives[i] is StraightPrimitive sp)
                        _segments.Add((fs.Data.Id, i, $"{fs.FeatureName}{(fs.Primitives.Count > 1 ? $" (straight {i + 1})" : "")}  {SwUtil.FormatLength(model, sp.Length)}"));
            if (body != null)
            {
                try { _cuts = CutAnalysis.Analyze(_builder, path, TubeUnbender.ForFeature(path, null), body, id => id, out bool _, out string _) ?? new List<CutInfo>(); }
                catch (Exception ex) { Log.Error("Unbend cut preview", ex); }
            }
        }

        public static void Create(ISldWorks app, IModelDoc2 model)
        {
            var all = TubeFeatureStore.Scan(model);
            if (StateFeatureBase.ActiveUnbends(all).Any()) { SwUtil.Message(app, "An Unbend is already active. Use Rebend first."); return; }
            var chain = TubeFeatureStore.Solve(all);
            var path = chain.Paths.FirstOrDefault(p => p.RootIsBaseTube);
            if (path == null) { SwUtil.Message(app, "Unbend needs a valid tube that starts with a Base Tube feature."); return; }
            if (!path.Primitives.OfType<ArcPrimitive>().Any()) { SwUtil.Message(app, "The tube has no bends."); return; }
            var body = TubeFeatureStore.TubeBody(all, path);
            var page = new UnbendPage(app, model, new TubeFeatureData { Kind = TubeFeatureKind.Unbend, ParentId = path.RootId }, path, chain, body, null, null);
            page.Build("Tube Unbend");
            page.ShowPage();
            page.Refresh();
        }

        public static void Edit(ISldWorks app, IModelDoc2 model, IFeature f, IMacroFeatureData md, TubeFeatureData data)
        {
            var all = TubeFeatureStore.Scan(model);
            var chain = TubeFeatureStore.Solve(all);
            var path = chain.Paths.FirstOrDefault(p => p.RootId == data.ParentId);
            if (path == null) { md.ReleaseSelectionAccess(); SwUtil.Message(app, "The tube of this Unbend has an error."); return; }
            var page = new UnbendPage(app, model, data, path, chain, null, f, md);
            page.Build("Edit " + f.Name);
            page.ShowPage();
            page.Refresh();
        }

        private void Build(string title)
        {
            if (!CreatePage(title)) throw new InvalidOperationException("Could not create the PropertyManager page.");
            var g = Group(1, "Bends to straighten");
            _all = Check(g, IdAll, "All bends", "Straighten every bend", _data.BendIds.Count == 0);
            _list = Add<IPropertyManagerPageListbox>(g, IdList, swPropertyManagerPageControlType_e.swControlType_Listbox, "", "Bends to straighten (Ctrl+click for several)");
            _list.Style = (int)swPropMgrPageListBoxStyle_e.swPropMgrPageListBoxStyle_MultipleItemSelect;
            _list.Height = 120;
            _list.AddItems(_bends.Select(b => $"{b.FeatureName}  {SwUtil.FormatAngle(b.Data.BendAngle)}  R {SwUtil.FormatLength(Model, b.Data.BendRadius)}").ToArray());
            for (short i = 0; i < _bends.Count; i++) if (_data.BendIds.Contains(_bends[i].Data.Id)) _list.SetSelectedItem(i, true);

            var g2 = Group(2, "Fixed segment");
            int sel = Math.Max(0, _segments.FindIndex(s => s.featureId == _data.FixedFeatureId && s.index == _data.FixedPrimitive));
            _fixed = Combo(g2, IdFixed, "Segment that does not move", _segments.Select(s => s.text).ToArray(), sel);
            _info = Label(g2, IdInfo, "Cuts in straight segments move with their segment. Cuts in bends are reported.");
        }

        protected override void OnChanged(int id) => Refresh();

        private void Read()
        {
            _data.BendIds.Clear();
            if (!_all.Checked)
            {
                var sel = _list.GetSelectedItems() as short[];
                if (sel != null) foreach (var i in sel) if (i >= 0 && i < _bends.Count) _data.BendIds.Add(_bends[i].Data.Id);
            }
            int f = Math.Max(0, (int)_fixed.CurrentSelection);
            if (f < _segments.Count) { _data.FixedFeatureId = _segments[f].featureId; _data.FixedPrimitive = _segments[f].index; }
        }

        private void Refresh()
        {
            Enable(_list, !_all.Checked);
            Read();
            _preview.Clear();
            if (!CanAccept(out string why)) { Message(why, true); return; }
            Message(null);
            var u = TubeUnbender.ForFeature(_path, _data);
            var body = _builder.Build(TubeUnbender.MergeCollinear(u.UnbentPath), _path.Profile, out string err);
            if (body == null) Message(err, true); else _preview.Show(body);
            if (_cuts.Count > 0)
            {
                var stay = _cuts.Where(c => TubeUnbender.ClassifyRigid(u, c.Piece.Samples) < 0).ToList();
                _info.Caption = $"Cuts: {_cuts.Count - stay.Count} move exactly with their segment; {stay.Count} lie in a straightened bend and are not moved.";
                if (stay.Count > 0) Message("Not moved by this Unbend (they come back unchanged with Rebend):\n" + string.Join("\n", stay.Select(c => "• " + c.Description)));
            }
        }

        protected override bool CanAccept(out string reason)
        {
            reason = null;
            Read();
            if (!_all.Checked && _data.BendIds.Count == 0) { reason = "Select at least one bend, or tick All bends."; return false; }
            try { TubeUnbender.ForFeature(_path, _data); }
            catch (InvalidOperationException ex) { reason = ex.Message; return false; }
            return true;
        }

        protected override void OnCancel()
        {
            _preview.Clear();
            if (_editFeature != null) _editDef.ReleaseSelectionAccess();
        }

        protected override void OnOk()
        {
            _preview.Clear();
            if (_editFeature != null) { TubeFeatureStore.Modify(Model, _editFeature, _editDef, _data); ShowReport(App, Model, _data.Id, "Unbend"); return; }
            Model.ClearSelection2(true);
            var f = TubeFeatureStore.Insert(Model, _data, _body, TubeFeatureStore.NextName(Model, TubeFeatureKind.Unbend));
            if (f == null) { SwUtil.Message(App, "SOLIDWORKS could not create the Tube Unbend feature.", swMessageBoxIcon_e.swMbStop); return; }
            ShowReport(App, Model, _data.Id, "Unbend");
        }

        /// <summary>Show the cut transfer report of a feature after its rebuild, when there is something to report.</summary>
        internal static void ShowReport(ISldWorks app, IModelDoc2 model, string featureId, string what)
        {
            if (!MaterialTransfer.Reports.TryGetValue(featureId, out var rep)) return;
            if (rep.NotTransferred.Count == 0 && !rep.MaterialAdded) return;
            SwUtil.Message(app, $"Tube {what}: {rep}", swMessageBoxIcon_e.swMbWarning);
        }
    }
}
