using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Pmp
{
    /// <summary>
    /// Shared logic of pages that continue a tube from an end face (Face Bend, Straight Extension):
    /// recognising the end face, finding the parent feature, preview, and creating or modifying the feature.
    /// </summary>
    public abstract class SegmentPage : PageBase
    {
        protected const int FaceMark = 1;
        protected readonly TubeFeatureData Data;
        protected readonly IFeature EditFeature;          // null when creating
        protected readonly IMacroFeatureData EditDef;
        private protected readonly Preview Preview;
        private protected readonly Handles Handles;
        private protected readonly BodyBuilder Builder;

        // Resolved start.
        protected TubeFrame? Start;
        protected TubeProfile Profile;
        protected IFace2 ForeignFace;                    // start face when the tube is not a TubeWorks tube
        protected IBody2 EditBody;
        protected string AttachText = "";
        /// <summary>TubeWorks features read once (names included) when the start is resolved; refreshes reuse it.</summary>
        private List<StoredFeature> _tree;
        private readonly string _editName;
        private TubeLibrary _library;
        private protected TubeLibrary Library => _library ?? (_library = LibraryStore.Load());

        private List<StoredFeature> Tree => _tree ?? (_tree = TubeFeatureStore.Scan(Model));
        private ChainResult SolveTree() { _tree = TubeFeatureStore.Scan(Model); return TubeFeatureStore.Solve(_tree); }

        protected bool IsEdit => EditFeature != null;

        protected SegmentPage(ISldWorks app, IModelDoc2 model, TubeFeatureData data, IFeature editFeature, IMacroFeatureData editDef)
            : base(app, model)
        {
            Data = data;
            EditFeature = editFeature;
            EditDef = editDef;
            Preview = new Preview(model);
            Handles = new Handles(app, model);
            Builder = new BodyBuilder(app) { VerifyGeometry = UserSettings.Current.VerifyGeometry };
            _editName = editFeature != null ? editFeature.Name : "(new)";
        }

        /// <summary>Recognise an end face and find what it belongs to. Returns an error message, or null.</summary>
        protected string ResolveFromFace(IFace2 face)
        {
            var end = FaceAnalyzer.Analyze(face, out string err);
            if (end == null) return err;
            var chain = SolveTree();
            double tol = Math.Max(1e-6, end.OuterDiameter * 1e-5);
            if (TubeChainSolver.TryMatchEnd(chain, end.Centre, end.OutwardNormal, tol, out var parent, out var pend) && parent.Data.Id != Data.Id)
            {
                if (IsEdit)
                {
                    int me = chain.Features.FindIndex(f => f.Data.Id == Data.Id), pi = chain.Features.IndexOf(parent);
                    if (me >= 0 && pi > me) return $"'{parent.FeatureName}' is below this feature in the tree. Select an end face of a feature above it.";
                }
                Data.ParentId = parent.Data.Id;
                Data.ParentEnd = pend;
                Start = pend == ParentEnd.Start ? parent.StartFrame.Reversed() : parent.EndFrame;
                Profile = parent.Profile;
                ForeignFace = null;
                AttachText = $"Continues: {parent.FeatureName} ({(pend == ParentEnd.Start ? "start" : "end")})";
                if (!parent.Profile.SameAs(end.Profile, 1e-6))
                    return "The selected face does not match the size of the TubeWorks feature it touches.";
            }
            else
            {
                // A tube that TubeWorks did not create: this feature becomes a root and keeps the face as a persistent reference.
                Data.ParentId = "";
                Data.ParentEnd = ParentEnd.End;
                Data.Origin = end.Centre;
                Data.Axis = end.OutwardNormal;
                Data.OuterDiameter = end.OuterDiameter;
                Data.WallThickness = end.WallThickness;
                Start = end.Frame;
                Profile = end.Profile;
                ForeignFace = face;
                AttachText = "Starts on an existing tube body";
            }
            EditBody = end.Body;
            return null;
        }

        /// <summary>Start of the feature being edited, from the solved chain.</summary>
        protected void ResolveForEdit()
        {
            var chain = SolveTree();
            var fs = chain.Find(Data.Id);
            if (fs != null && fs.Profile != null && fs.StartFrame.Tangent.Length > 0.5)
            {
                Start = fs.StartFrame;
                Profile = fs.Profile;
                var parent = chain.Find(Data.ParentId);
                AttachText = parent != null ? $"Continues: {parent.FeatureName}" : "Starts on an existing tube body";
            }
            if (Data.IsRoot)
            {
                EditDef.GetSelections3(out object objs, out object _, out object _, out object _, out object _);
                ForeignFace = SwUtil.Arr<IFace2>((object)objs).FirstOrDefault();
                if (ForeignFace != null) ResolveFromFace(ForeignFace);
            }
        }

        /// <summary>Centreline primitives of this feature for the current input, or null and a message.</summary>
        protected abstract List<PathPrimitive> CurrentPrimitives(out string error);

        protected virtual void AfterPreview(List<PathPrimitive> prims) { }

        protected void UpdatePreview(bool live, bool centreline)
        {
            Preview.Clear();
            if (Start == null) { Message("Select the end face of a tube."); return; }
            var prims = CurrentPrimitives(out string err);
            if (prims == null) { Message(err, true); AfterPreview(null); return; }
            Message(null);
            if (live)
            {
                var body = Builder.Build(prims, Profile, out err);
                if (body == null) Message(err, true); else Preview.Show(body);
            }
            if (centreline) Preview.Show(Builder.Centreline(prims), true);
            AfterPreview(prims);
        }

        /// <summary>Whole tube with this feature's current input: for developed length and validation.</summary>
        protected ChainResult SolveWithCurrent()
        {
            var list = Tree;
            var inputs = list.Where(s => s.Data.Id != Data.Id)
                .Select(s => new ChainInput { Data = s.Data, FeatureName = s.Name, Suppressed = s.Suppressed }).ToList();
            var mine = new ChainInput { Data = Data, FeatureName = _editName };
            int at = IsEdit ? list.FindIndex(s => s.Data.Id == Data.Id) : -1;
            if (at >= 0 && at <= inputs.Count) inputs.Insert(at, mine); else inputs.Add(mine);
            var anchors = new Dictionary<string, ForeignAnchor>(TubeFeatureStore.ForeignAnchors);
            if (Data.IsRoot && Start.HasValue) anchors[Data.Id] = new ForeignAnchor { Frame = Start.Value, Profile = Profile };
            return TubeChainSolver.Solve(inputs, anchors);
        }

        protected override void OnCancel()
        {
            Preview.Clear();
            Handles.RemoveAll();
            if (IsEdit) EditDef.ReleaseSelectionAccess();
        }

        protected override void OnOk()
        {
            Preview.Clear();
            Handles.RemoveAll();
            SaveRemembered();
            if (IsEdit)
            {
                // Keep exactly the references the feature needs: the start face of a root on a foreign tube, else none.
                if (ForeignFace != null && Data.IsRoot) EditDef.SetSelections(new object[] { ForeignFace }, new[] { 0 });
                else EditDef.SetSelections(new object[0], new int[0]);
                if (EditBody != null && Data.Merge) EditDef.EditBodies = new object[] { EditBody };
                if (!TubeFeatureStore.Modify(Model, EditFeature, EditDef, Data))
                    App.SendMsgToUser2("TubeWorks: SOLIDWORKS did not accept the changes.", (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            // Select exactly the references the feature keeps: the start face of a root on a foreign tube, else nothing.
            Model.ClearSelection2(true);
            if (Data.IsRoot && ForeignFace != null)
            {
                var sm = (ISelectionMgr)Model.SelectionManager;
                var sd = sm.CreateSelectData();
                sd.Mark = 0;
                ((IEntity)ForeignFace).Select4(false, sd);
            }
            var body = Data.Merge ? EditBody : null;
            var name = TubeFeatureStore.NextName(Model, Data.Kind);
            var feat = TubeFeatureStore.Insert(Model, Data, body, name);
            Model.ClearSelection2(true);
            if (feat == null)
            {
                App.SendMsgToUser2("TubeWorks: SOLIDWORKS could not create the feature. See %APPDATA%\\TubeWorks\\tubeworks.log.",
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }
            SelectNewEndFace(feat);
        }

        protected abstract void SaveRemembered();

        /// <summary>Select the new free end face, so that the operator can continue at once ("select next face").</summary>
        private void SelectNewEndFace(IFeature feat)
        {
            try
            {
                var chain = TubeFeatureStore.Solve(Model);
                var fs = chain.Find(Data.Id);
                if (fs == null || !fs.IsValid) return;
                foreach (var face in SwUtil.Arr<IFace2>((object)feat.GetFaces()))
                {
                    var end = FaceAnalyzer.Analyze(face, out string _);
                    if (end != null && end.Centre.AlmostEquals(fs.EndFrame.Origin, Math.Max(1e-6, end.OuterDiameter * 1e-5)))
                    {
                        ((IEntity)face).Select4(false, null);
                        return;
                    }
                }
            }
            catch (Exception ex) { Log.Error("SelectNewEndFace", ex); }
        }

        public override bool OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            if (SelType != (int)swSelectType_e.swSelFACES || !IsFaceBox(Id)) return true;
            var end = FaceAnalyzer.Analyze(Selection as IFace2, out string err);
            if (end == null) { Message(err, true); return false; }
            ItemText = $"Tube end Ø{end.OuterDiameter * 1000:0.###} × {end.WallThickness * 1000:0.###} mm";
            return true;
        }

        protected abstract bool IsFaceBox(int id);

        protected string ManufacturingSummary(out string warnings)
        {
            var chain = SolveWithCurrent();
            var path = chain.Paths.FirstOrDefault(p => p.FeatureIds.Contains(Data.Id));
            var issues = TubeValidator.Validate(chain, Library.RulesFor(Profile))
                .Where(i => i.FeatureId == Data.Id || (path != null && i.FeatureId == path.RootId && i.Code == "CLAMP")).ToList();
            warnings = issues.Count == 0 ? "No manufacturing warnings." : string.Join("\n", issues.Select(i => i.ToString()));
            return path == null ? "Total developed length: (tube has an error)" : "Total developed length: " + SwUtil.FormatLength(Model, path.DevelopedLength);
        }
    }
}
