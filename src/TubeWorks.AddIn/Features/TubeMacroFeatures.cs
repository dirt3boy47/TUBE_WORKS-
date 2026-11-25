using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Features
{
    /// <summary>
    /// Common rebuild logic of TubeWorks macro features. SOLIDWORKS calls Regenerate on every rebuild, in tree order.
    /// The feature re-solves the chain from stored parameters, creates its own segment as a temporary body, merges it
    /// into the body it edits, and returns the result. A string return value is shown by SOLIDWORKS as the rebuild error.
    /// </summary>
    [ComVisible(false)]
    public abstract class TubeMacroFeatureBase : ISwComFeature
    {
        public object Edit(object app, object modelDoc, object feature)
        {
            try
            {
                TubeWorksAddIn.Instance?.EditFeature((IModelDoc2)modelDoc, (IFeature)feature);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Edit", ex);
                return false;
            }
        }

        public object Security(object app, object modelDoc, object feature) =>
            (int)swMacroFeatureSecurityOptions_e.swMacroFeatureSecurityByDefault;

        public object Regenerate(object app, object modelDoc, object feature)
        {
            try
            {
                var sw = (ISldWorks)app;
                var model = (IModelDoc2)modelDoc;
                var feat = (IFeature)feature;
                var md = (IMacroFeatureData)feat.GetDefinition();
                var me = TubeFeatureStore.Read(md);
                return RegenerateCore(sw, model, feat, md, me);
            }
            catch (Exception ex)
            {
                Log.Error("Regenerate", ex);
                return "TubeWorks: " + ex.Message;
            }
        }

        protected virtual object RegenerateCore(ISldWorks sw, IModelDoc2 model, IFeature feat, IMacroFeatureData md, TubeFeatureData me)
        {
            if (me.IsRoot && me.Kind != TubeFeatureKind.BaseTube)
            {
                // A root on a non-TubeWorks tube: measure the start face now.
                md.GetSelections3(out object objs, out object _, out object _, out object _, out object _);
                var face = SwUtil.Arr<IFace2>(objs).FirstOrDefault();
                if (face == null)
                    return "TubeWorks: the start face of this feature is missing. Edit the feature and select the tube end face again.";
                var end = FaceAnalyzer.Analyze(face, out string ferr);
                if (end == null) return "TubeWorks: " + ferr;
                TubeFeatureStore.ForeignAnchors[me.Id] = new ForeignAnchor { Frame = end.Frame, Profile = end.Profile };
            }

            var list = TubeFeatureStore.Scan(model, feat.Name);
            var self = TubeFeatureStore.FindById(list, me.Id);
            if (self != null) self.Data = me; else list.Add(new StoredFeature { Feature = feat, Definition = md, Data = me });
            var chain = TubeFeatureStore.Solve(list);
            var fs = chain.Find(me.Id);
            if (fs == null) return "TubeWorks: the feature could not be solved.";
            if (fs.Error != null) return "TubeWorks: " + fs.Error;

            var builder = new BodyBuilder(sw) { VerifyGeometry = UserSettings.Current.VerifyGeometry };
            var seg = builder.Build(fs.Primitives, fs.Profile, out string err);
            if (seg == null) return "TubeWorks: " + err;

            IBody2 result = seg;
            var edit = SwUtil.Arr<IBody2>(md.EditBodies);
            if (me.Merge && edit.Length > 0)
            {
                var copy = edit[0].Copy2(true) as IBody2;
                result = builder.Union(copy, seg, out err);
                if (result == null) return "TubeWorks: " + err;
            }
            UserIds.Assign(md, result, fs);
            return result;
        }
    }

    [ComVisible(true)]
    [Guid("36B34ABD-9100-42D6-A57B-21C565C8EE75")]
    [ProgId(ProgIds.BaseTube)]
    public sealed class BaseTubeFeature : TubeMacroFeatureBase { }

    [ComVisible(true)]
    [Guid("7471ED4A-7F98-45A2-ACA5-1FAD4A599639")]
    [ProgId(ProgIds.Straight)]
    public sealed class StraightFeature : TubeMacroFeatureBase { }

    [ComVisible(true)]
    [Guid("F584E8D8-909C-4AD3-84CF-B8530D4B9C82")]
    [ProgId(ProgIds.Bend)]
    public sealed class FaceBendFeature : TubeMacroFeatureBase { }

    /// <summary>
    /// Tube Flat Pattern: when unsuppressed it replaces the bent tube body with a straight blank of the developed
    /// centreline length. Suppress it to return to the bent design. It always stays at the end of the tree.
    /// </summary>
    [ComVisible(true)]
    [Guid("884ACEBF-82D5-431A-A0D4-2D1B5492F504")]
    [ProgId(ProgIds.FlatPattern)]
    public sealed class FlatPatternFeature : TubeMacroFeatureBase
    {
        protected override object RegenerateCore(ISldWorks sw, IModelDoc2 model, IFeature feat, IMacroFeatureData md, TubeFeatureData me)
        {
            var chain = TubeFeatureStore.Solve(TubeFeatureStore.Scan(model, feat.Name));
            var path = chain.Paths.FirstOrDefault(p => p.RootId == me.ParentId);
            if (path == null) return "TubeWorks: the tube for this flat pattern was not found or has an error.";
            if (!path.RootIsBaseTube) return "TubeWorks: in this release the flat pattern needs a tube that starts with a Base Tube feature.";
            var first = path.Primitives[0];
            var blank = new StraightPrimitive(first.StartPoint, first.StartTangent, path.DevelopedLength) { FeatureId = me.Id };
            var builder = new BodyBuilder(sw) { VerifyGeometry = UserSettings.Current.VerifyGeometry };
            var body = builder.Build(new[] { blank }, path.Profile, out string err);
            if (body == null) return "TubeWorks: " + err;
            var fs = new FeatureSolution { Data = me, StartFrame = new TubeFrame(blank.StartPoint, -blank.Direction), EndFrame = new TubeFrame(blank.EndPoint, blank.Direction) };
            fs.Primitives.Add(blank);
            UserIds.Assign(md, body, fs);
            return body;
        }
    }

    /// <summary>
    /// SOLIDWORKS needs user ids on the faces and edges of macro feature bodies so that other features can keep
    /// references to them through rebuilds. The free end face always gets id (1, 0) and the start face (2, 0);
    /// other faces and edges are numbered in modeler order.
    /// </summary>
    internal static class UserIds
    {
        public static void Assign(IMacroFeatureData md, IBody2 body, FeatureSolution fs)
        {
            md.GetEntitiesNeedUserId(body, out object facesObj, out object edgesObj);
            var faces = SwUtil.Arr<IFace2>(facesObj);
            var edges = SwUtil.Arr<IEdge>(edgesObj);
            int next = 10;
            foreach (var f in faces)
            {
                int id = Role(f, fs);
                md.SetFaceUserId((Face2)f, id > 0 ? id : next++, 0);
            }
            int e = 1;
            foreach (var ed in edges) md.SetEdgeUserId((Edge)ed, e++, 0);
        }

        private static int Role(IFace2 face, FeatureSolution fs)
        {
            try
            {
                var end = FaceAnalyzer.Analyze(face, out string _);
                if (end == null) return 0;
                double tol = Math.Max(1e-7, end.OuterDiameter * 1e-6);
                if (end.Centre.AlmostEquals(fs.EndFrame.Origin, tol)) return 1;
                if (end.Centre.AlmostEquals(fs.StartFrame.Origin, tol)) return 2;
            }
            catch { }
            return 0;
        }
    }
}
