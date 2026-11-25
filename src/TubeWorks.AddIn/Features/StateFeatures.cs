using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using TubeWorks.AddIn.Sw;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Features
{
    /// <summary>Common logic of features that change the state of a whole tube (Unbend, Rebend, Flat Pattern).</summary>
    [ComVisible(false)]
    public abstract class StateFeatureBase : TubeMacroFeatureBase
    {
        internal sealed class Context
        {
            public List<StoredFeature> Features;
            public TubePath Path;
            public BodyBuilder Builder;
            public IMathUtility Math;
            public Func<string, string> Name;
        }

        /// <summary>Unbend features that are active (not suppressed and not closed by a Rebend) among the given features.</summary>
        internal static List<StoredFeature> ActiveUnbends(IEnumerable<StoredFeature> list)
        {
            var all = list.ToList();
            var closed = new HashSet<string>(all.Where(x => x.Data.Kind == TubeFeatureKind.Rebend && !x.Suppressed).Select(x => x.Data.ParentId));
            return all.Where(x => x.Data.Kind == TubeFeatureKind.Unbend && !x.Suppressed && !closed.Contains(x.Data.Id)).ToList();
        }

        internal static Context Prepare(ISldWorks sw, IModelDoc2 model, IFeature feat, string rootId, out string error)
        {
            error = null;
            var list = TubeFeatureStore.Scan(model, feat.Name);
            var chain = TubeFeatureStore.Solve(list);
            var path = chain.Paths.FirstOrDefault(p => p.RootId == rootId);
            if (path == null) { error = "the tube was not found or has an error."; return null; }
            if (!path.RootIsBaseTube) { error = "this needs a tube that starts with a Base Tube feature."; return null; }
            return new Context
            {
                Features = list, Path = path, Math = (IMathUtility)sw.GetMathUtility(),
                Builder = new BodyBuilder(sw) { VerifyGeometry = UserSettings.Current.VerifyGeometry },
                Name = id => list.FirstOrDefault(x => x.Data.Id == id)?.Feature.Name ?? id,
            };
        }

        internal static FeatureSolution Solution(TubeFeatureData me, IReadOnlyList<PathPrimitive> prims)
        {
            var fs = new FeatureSolution { Data = me, StartFrame = new TubeFrame(prims[0].StartPoint, -prims[0].StartTangent), EndFrame = new TubeFrame(prims[prims.Count - 1].EndPoint, prims[prims.Count - 1].EndTangent) };
            fs.Primitives.AddRange(prims);
            return fs;
        }

        /// <summary>
        /// Bent state → unbent state: nominal unbent body minus the cuts of the actual bent body that can be moved rigidly.
        /// Cuts in bends are reported and returned in <paramref name="heldBack"/> (bent-state bodies).
        /// </summary>
        internal static IBody2 BentToUnbent(Context c, UnbendResult u, IBody2 actualBent, TransferReport rep, List<IBody2> heldBack, out string error)
        {
            error = null;
            var nominalBent = c.Builder.Build(c.Path.Primitives, c.Path.Profile, out error);
            if (nominalBent == null) return null;
            var unbent = c.Builder.Build(TubeUnbender.MergeCollinear(u.UnbentPath), c.Path.Profile, out error);
            if (unbent == null) return null;
            if (actualBent == null) return unbent;

            var pieces = MaterialTransfer.Removed(nominalBent, actualBent, out bool added);
            rep.MaterialAdded = added;
            var moved = new List<IBody2>();
            foreach (var p in pieces)
            {
                int k = TubeUnbender.ClassifyRigid(u, p.Samples);
                if (k < 0)
                {
                    rep.NotTransferred.Add(MaterialTransfer.Describe(c.Path.Primitives, p, c.Name));
                    heldBack?.Add(p.Body);
                    continue;
                }
                var body = (IBody2)p.Body.Copy();
                body.ApplyTransform(Transforms.Create(c.Math, u.Items[k].BentToUnbent));
                moved.Add(body);
                rep.Transferred++;
            }
            return MaterialTransfer.Subtract(unbent, moved, rep, out error);
        }
    }

    /// <summary>
    /// Tube Unbend: straightens the selected bends (or all) about a fixed segment. Each straightened bend keeps its
    /// developed centreline length R·θ. Cuts in straight segments move with their segment; cuts in bends are reported.
    /// Model holes, slots and cut-outs after this feature, then add Rebend.
    /// </summary>
    [ComVisible(true)]
    [Guid("D7264B5E-B280-47AC-80F0-DB513BC1434F")]
    [ProgId(ProgIds.Unbend)]
    public sealed class UnbendFeature : StateFeatureBase
    {
        protected internal override object RegenerateCore(ISldWorks sw, IModelDoc2 model, IFeature feat, IMacroFeatureData md, TubeFeatureData me)
        {
            var c = Prepare(sw, model, feat, me.ParentId, out string err);
            if (c == null) return "TubeWorks Unbend: " + err;
            if (ActiveUnbends(c.Features.Where(x => x.Data.Id != me.Id)).Any())
                return "TubeWorks Unbend: an earlier Unbend is still active. Add a Rebend before this Unbend.";
            UnbendResult u;
            try { u = TubeUnbender.ForFeature(c.Path, me); }
            catch (InvalidOperationException ex) { return "TubeWorks Unbend: " + ex.Message; }
            if (!u.Items.Any(i => i.Straightened)) return "TubeWorks Unbend: no bend selected.";

            var rep = new TransferReport();
            var held = new List<IBody2>();
            var actual = SwUtil.Arr<IBody2>(md.EditBodies).FirstOrDefault();
            var body = BentToUnbent(c, u, actual, rep, held, out err);
            if (body == null) return "TubeWorks Unbend: " + err;
            MaterialTransfer.Reports[me.Id] = rep;
            MaterialTransfer.HeldBack[me.Id] = held;
            UserIds.Assign(md, body, Solution(me, u.UnbentPath));
            return body;
        }
    }

    /// <summary>
    /// Tube Rebend: bends the tube of an Unbend feature back. Cuts made in the straightened state (and the cuts the
    /// Unbend moved) go back with the inverse rigid motion of their straight segment. Cuts that lie in a straightened
    /// bend are reported and not applied, because their exact bent shape is not defined by a rigid motion.
    /// </summary>
    [ComVisible(true)]
    [Guid("2474FF55-C85F-42B1-B606-2B15F2A01D59")]
    [ProgId(ProgIds.Rebend)]
    public sealed class RebendFeature : StateFeatureBase
    {
        protected internal override object RegenerateCore(ISldWorks sw, IModelDoc2 model, IFeature feat, IMacroFeatureData md, TubeFeatureData me)
        {
            var list = TubeFeatureStore.Scan(model, feat.Name);
            var unbend = list.FirstOrDefault(x => x.Data.Id == me.ParentId);
            if (unbend == null) return "TubeWorks Rebend: its Unbend feature is missing.";
            if (unbend.Suppressed) return "TubeWorks Rebend: its Unbend feature is suppressed.";
            if (!MaterialTransfer.HeldBack.TryGetValue(unbend.Data.Id, out var held))
                return "TubeWorks Rebend: rebuild the whole part (Ctrl+Q) so that the Unbend data is available.";

            var c = Prepare(sw, model, feat, unbend.Data.ParentId, out string err);
            if (c == null) return "TubeWorks Rebend: " + err;
            UnbendResult u;
            try { u = TubeUnbender.ForFeature(c.Path, unbend.Data); }
            catch (InvalidOperationException ex) { return "TubeWorks Rebend: " + ex.Message; }

            var nominalUnbent = c.Builder.Build(u.UnbentPath, c.Path.Profile, out err);
            var nominalBent = nominalUnbent == null ? null : c.Builder.Build(c.Path.Primitives, c.Path.Profile, out err);
            if (nominalBent == null) return "TubeWorks Rebend: " + err;

            var rep = new TransferReport();
            var tools = new List<IBody2>();
            var actual = SwUtil.Arr<IBody2>(md.EditBodies).FirstOrDefault();
            if (actual != null)
            {
                var unbentPath = u.UnbentPath;
                foreach (var p in MaterialTransfer.Removed(nominalUnbent, actual, out bool added))
                {
                    rep.MaterialAdded |= added;
                    int k = TubeUnbender.ClassifyRigidUnbent(u, p.Samples);
                    if (k < 0) { rep.NotTransferred.Add(MaterialTransfer.Describe(unbentPath, p, c.Name)); continue; }
                    var body = (IBody2)p.Body.Copy();
                    body.ApplyTransform(Transforms.Create(c.Math, u.Items[k].BentToUnbent.Inverse()));
                    tools.Add(body);
                    rep.Transferred++;
                }
            }
            // Cuts that were in bends before the Unbend come back unchanged.
            tools.AddRange(held);
            var result = MaterialTransfer.Subtract(nominalBent, tools, rep, out err);
            if (result == null) return "TubeWorks Rebend: " + err;
            MaterialTransfer.Reports[me.Id] = rep;
            UserIds.Assign(md, result, Solution(me, c.Path.Primitives));
            return result;
        }
    }

    /// <summary>
    /// Tube Flat Pattern: replaces the bent tube with the straight manufacturing blank (all bends straightened about
    /// the first segment). Cuts in straight segments are kept in position; cuts in bends are reported.
    /// Suppress it to return to the bent design. It always stays at the end of the tree.
    /// </summary>
    [ComVisible(true)]
    [Guid("884ACEBF-82D5-431A-A0D4-2D1B5492F504")]
    [ProgId(ProgIds.FlatPattern)]
    public sealed class FlatPatternFeature : StateFeatureBase
    {
        protected internal override object RegenerateCore(ISldWorks sw, IModelDoc2 model, IFeature feat, IMacroFeatureData md, TubeFeatureData me)
        {
            var c = Prepare(sw, model, feat, me.ParentId, out string err);
            if (c == null) return "TubeWorks Flat Pattern: " + err;
            if (ActiveUnbends(c.Features).Any()) return "TubeWorks Flat Pattern: an Unbend is active. Add a Rebend first.";
            var u = TubeUnbender.ForFeature(c.Path, null);
            var rep = new TransferReport();
            var body = BentToUnbent(c, u, SwUtil.Arr<IBody2>(md.EditBodies).FirstOrDefault(), rep, null, out err);
            if (body == null) return "TubeWorks Flat Pattern: " + err;
            MaterialTransfer.Reports[me.Id] = rep;
            UserIds.Assign(md, body, Solution(me, u.UnbentPath));
            return body;
        }
    }
}
