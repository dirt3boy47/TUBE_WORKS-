using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>Straight Extension PropertyManager: extends a tube end along its axis.</summary>
    public sealed class StraightPage : SegmentPage
    {
        private const int TargetMark = 2;
        private const int IdFace = 10, IdInfo = 11, IdAttach = 12, IdCond = 20, IdLen = 21, IdTarget = 22, IdEndPos = 23, IdMerge = 30, IdTotal = 31, IdWarn = 32;

        private IPropertyManagerPageSelectionbox _face, _target;
        private IPropertyManagerPageCombobox _cond;
        private IPropertyManagerPageNumberbox _len;
        private IPropertyManagerPageLabel _info, _attach, _endPos, _total, _warn;
        private IPropertyManagerPageCheckbox _merge;

        private StraightPage(ISldWorks app, IModelDoc2 model, TubeFeatureData d, IFeature f, IMacroFeatureData md) : base(app, model, d, f, md) { }

        public static void Create(ISldWorks app, IModelDoc2 model, IFace2 preselected)
        {
            var data = new TubeFeatureData { Kind = TubeFeatureKind.Straight, Length = UserSettings.Current.LastExtension };
            var page = new StraightPage(app, model, data, null, null);
            page.Build("Straight Extension");
            page.ShowPage();
            if (preselected != null)
            {
                string err = page.ResolveFromFace(preselected);
                var sd = ((ISelectionMgr)model.SelectionManager).CreateSelectData(); sd.Mark = FaceMark;
                ((IEntity)preselected).Select4(true, sd);
                if (err != null) { page.Message(err, true); page.Start = null; }
            }
            page.Refresh();
        }

        public static void Edit(ISldWorks app, IModelDoc2 model, IFeature f, IMacroFeatureData md, TubeFeatureData data)
        {
            var page = new StraightPage(app, model, data, f, md);
            page.ResolveForEdit();
            page.Build("Edit " + f.Name);
            page.ShowPage();
            page.Refresh();
        }

        private void Build(string title)
        {
            if (!CreatePage(title)) throw new InvalidOperationException("Could not create the PropertyManager page.");
            var g1 = Group(1, "Selected geometry");
            _face = Selection(g1, IdFace, "Tube end face", FaceMark, new[] { swSelectType_e.swSelFACES }, true);
            _info = Label(g1, IdInfo, "");
            _attach = Label(g1, IdAttach, "");

            var g2 = Group(2, "Extension");
            _cond = Combo(g2, IdCond, "End condition", new[] { "Blind (length)", "Up to point / vertex", "Up to plane / planar face" }, (int)Data.EndCondition);
            _len = Number(g2, IdLen, "Extension length", "Extension length", swNumberboxUnitType_e.swNumberBox_Length, 1e-5, 100, Math.Max(Data.Length, 1e-5), 0.001);
            _target = Selection(g2, IdTarget, "Target point, vertex, plane or planar face (value is captured, not associative)", TargetMark,
                new[] { swSelectType_e.swSelVERTICES, swSelectType_e.swSelDATUMPOINTS, swSelectType_e.swSelDATUMPLANES, swSelectType_e.swSelFACES }, true);
            _endPos = Label(g2, IdEndPos, "End position: -");

            var g3 = Group(3, "Options");
            _merge = Check(g3, IdMerge, "Merge with existing tube", "Merge into the selected tube body", Data.Merge);
            if (IsEdit) Enable(_merge, false);

            var g4 = Group(4, "Manufacturing information", expanded: false);
            _total = Label(g4, IdTotal, "");
            _warn = Label(g4, IdWarn, "");
        }

        protected override bool IsFaceBox(int id) => id == IdFace;

        public override bool OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            if (Id == IdTarget && SelType == (int)swSelectType_e.swSelFACES && (((IFace2)Selection).GetSurface() as ISurface)?.IsPlane() != true)
            { Message("Select a planar face.", true); return false; }
            return base.OnSubmitSelection(Id, Selection, SelType, ref ItemText);
        }

        protected override void OnChanged(int id)
        {
            switch (id)
            {
                case IdFace:
                    var face = Selected(FaceMark).OfType<IFace2>().FirstOrDefault();
                    if (face == null) { if (!IsEdit) Start = null; break; }
                    string err = ResolveFromFace(face);
                    if (err != null) { Message(err, true); Start = null; }
                    break;
                case IdLen: Data.Length = _len.Value; break;
                case IdCond: Data.EndCondition = (StraightEndCondition)Math.Max(0, (int)_cond.CurrentSelection); break;
                case IdMerge: Data.Merge = _merge.Checked; break;
                case IdTarget:
                    var obj = Selected(TargetMark).FirstOrDefault();
                    if (RefGeometry.TryPoint(obj, out Vec3 p)) { Data.EndCondition = StraightEndCondition.UpToPoint; Data.UpToPoint = p; }
                    else if (RefGeometry.TryPlane(obj, out Vec3 pp, out Vec3 n)) { Data.EndCondition = StraightEndCondition.UpToPlane; Data.UpToPoint = pp; Data.UpToPlaneNormal = n; }
                    Building = true; _cond.CurrentSelection = (short)Data.EndCondition; Building = false;
                    break;
            }
            Refresh();
        }

        private void Refresh()
        {
            bool blind = Data.EndCondition == StraightEndCondition.Blind;
            Enable(_len, blind);
            Show(_target, !blind);
            if (Profile != null && Start.HasValue)
            {
                _info.Caption = $"Ø{SwUtil.FormatLength(Model, Profile.OuterDiameter)} × {SwUtil.FormatLength(Model, Profile.WallThickness)} wall";
                _attach.Caption = AttachText;
            }
            UpdatePreview(UserSettings.Current.LivePreview, UserSettings.Current.ShowCentreline);
        }

        protected override List<PathPrimitive> CurrentPrimitives(out string error)
        {
            double len = TubeChainSolver.StraightLength(Data, Start.Value, out error);
            if (error != null) return null;
            return new List<PathPrimitive> { new StraightPrimitive(Start.Value.Origin, Start.Value.Tangent, len) };
        }

        protected override void AfterPreview(List<PathPrimitive> prims)
        {
            if (prims == null) return;
            var e = prims[0].EndPoint;
            _endPos.Caption = $"End position: {SwUtil.FormatLength(Model, e.X)}, {SwUtil.FormatLength(Model, e.Y)}, {SwUtil.FormatLength(Model, e.Z)}  (length {SwUtil.FormatLength(Model, prims[0].Length)})";
            _total.Caption = ManufacturingSummary(out string w);
            _warn.Caption = w;
            if (Data.EndCondition == StraightEndCondition.Blind)
            {
                Handles.ArrowDragged = len => { Data.Length = Math.Max(len, 1e-5); Building = true; _len.Value = Data.Length; Building = false; Refresh(); };
                Handles.ShowArrow(Start.Value.Origin, Start.Value.Tangent, prims[0].Length);
            }
        }

        protected override bool CanAccept(out string reason)
        {
            reason = Start == null ? "Select the end face of a tube." : null;
            if (reason == null) TubeChainSolver.StraightLength(Data, Start.Value, out reason);
            return reason == null;
        }

        protected override void SaveRemembered()
        {
            if (Data.EndCondition == StraightEndCondition.Blind) { UserSettings.Current.LastExtension = Data.Length; UserSettings.Current.Save(); }
        }
    }
}
