using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    internal static class ProgIds
    {
        public const string BaseTube = "TubeWorks.BaseTubeFeature";
        public const string Straight = "TubeWorks.StraightFeature";
        public const string Bend = "TubeWorks.FaceBendFeature";
        public const string FlatPattern = "TubeWorks.FlatPatternFeature";
        public const string Unbend = "TubeWorks.UnbendFeature";
        public const string Rebend = "TubeWorks.RebendFeature";

        public static string For(TubeFeatureKind k) =>
            k == TubeFeatureKind.BaseTube ? BaseTube : k == TubeFeatureKind.Straight ? Straight : k == TubeFeatureKind.Bend ? Bend
            : k == TubeFeatureKind.Unbend ? Unbend : k == TubeFeatureKind.Rebend ? Rebend : FlatPattern;

        public static bool IsOurs(string progId) =>
            progId == BaseTube || progId == Straight || progId == Bend || progId == FlatPattern || progId == Unbend || progId == Rebend;
    }

    internal sealed class StoredFeature
    {
        public IFeature Feature;
        public IMacroFeatureData Definition;
        public TubeFeatureData Data;
        public bool Suppressed;
        /// <summary>Feature name, read once when the tree is scanned (only for TubeWorks features).</summary>
        public string Name = "";
    }

    /// <summary>Reads and writes TubeWorks parameters stored in macro features, and solves the tube chain of a part.</summary>
    internal static class TubeFeatureStore
    {
        public const string ParamName = "TubeWorksData";

        /// <summary>
        /// Start frames of roots that sit on a non-TubeWorks tube, measured from the live face when the root regenerates
        /// or is edited. Key = feature Id. If a value is missing, the anchor stored in the feature data is used.
        /// </summary>
        public static readonly ConcurrentDictionary<string, ForeignAnchor> ForeignAnchors = new ConcurrentDictionary<string, ForeignAnchor>();

        public static TubeFeatureData Read(IMacroFeatureData md)
        {
            md.GetStringByName(ParamName, out string text);
            return TubeFeatureData.Decode(text);
        }

        public static StoredFeature TryRead(IFeature f)
        {
            try
            {
                if (f == null || f.GetTypeName2() != "MacroFeature") return null;
                var md = f.GetDefinition() as IMacroFeatureData;
                if (md == null || !ProgIds.IsOurs(md.GetProgId())) return null;
                return new StoredFeature { Feature = f, Definition = md, Data = Read(md), Suppressed = SwUtil.IsSuppressed(f), Name = f.Name ?? "" };
            }
            catch { return null; }
        }

        /// <summary>
        /// All TubeWorks features in tree order. When <paramref name="stopAfterId"/> is given, scanning stops after the
        /// TubeWorks feature with that Id. Only TubeWorks features are asked for their name: other features are only
        /// tested for their type, which keeps API calls on foreign features to a minimum.
        /// </summary>
        public static List<StoredFeature> Scan(IModelDoc2 model, string stopAfterId = null)
        {
            var list = new List<StoredFeature>();
            var f = model.FirstFeature() as IFeature;
            while (f != null)
            {
                var s = TryRead(f);
                if (s != null) list.Add(s);
                if (stopAfterId != null && s != null && s.Data.Id == stopAfterId) break;
                f = f.GetNextFeature() as IFeature;
            }
            return list;
        }

        public static ChainResult Solve(IEnumerable<StoredFeature> features) =>
            TubeChainSolver.Solve(
                features.Select(s => new ChainInput { Data = s.Data, FeatureName = s.Name, Suppressed = s.Suppressed }).ToList(),
                ForeignAnchors);

        public static ChainResult Solve(IModelDoc2 model) => Solve(Scan(model));

        public static StoredFeature FindById(IEnumerable<StoredFeature> list, string id) => list.FirstOrDefault(s => s.Data.Id == id);

        /// <summary>Base name and running number: "Bend 01", "Straight 02".</summary>
        public static string NextName(IModelDoc2 model, TubeFeatureKind kind)
        {
            string stem = kind == TubeFeatureKind.BaseTube ? "Base Tube" : kind == TubeFeatureKind.Straight ? "Straight" : kind == TubeFeatureKind.Bend ? "Bend"
                : kind == TubeFeatureKind.Unbend ? "Tube Unbend" : kind == TubeFeatureKind.Rebend ? "Tube Rebend" : "Tube Flat Pattern";
            if (kind == TubeFeatureKind.FlatPattern) return stem;
            var names = new HashSet<string>(Scan(model).Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            if (kind == TubeFeatureKind.BaseTube && !names.Contains(stem)) return stem;
            for (int i = 1; ; i++)
            {
                string n = $"{stem} {i:00}";
                if (!names.Contains(n)) return n;
            }
        }

        private static string[] IconFiles(TubeFeatureKind kind)
        {
            string dir = Path.Combine(Path.GetDirectoryName(typeof(TubeFeatureStore).Assembly.Location) ?? "", "Icons");
            string stem = kind.ToString().ToLowerInvariant();
            return new[]
            {
                Path.Combine(dir, $"feat_{stem}.bmp"),
                Path.Combine(dir, $"feat_{stem}_suppressed.bmp"),
                Path.Combine(dir, $"feat_{stem}_highlighted.bmp"),
            };
        }

        /// <summary>
        /// Insert a TubeWorks macro feature. The current selection set becomes the feature's persistent selections,
        /// so the caller must select exactly the references the feature keeps (the start face for roots on a
        /// foreign tube, nothing otherwise) before calling.
        /// </summary>
        public static IFeature Insert(IModelDoc2 model, TubeFeatureData data, IBody2 editBody, string name)
        {
            var fm = model.FeatureManager;
            object editBodies = editBody != null ? new object[] { editBody } : null;
            int options = data.Kind == TubeFeatureKind.FlatPattern
                ? (int)swMacroFeatureOptions_e.swMacroFeatureAlwaysAtEnd
                : (int)swMacroFeatureOptions_e.swMacroFeatureByDefault;
            var feat = fm.InsertMacroFeature3(
                name.Replace(" ", ""), ProgIds.For(data.Kind), null,
                new[] { ParamName }, new[] { (int)swMacroFeatureParamType_e.swMacroFeatureParamTypeString }, new[] { data.Encode() },
                null, null, editBodies, IconFiles(data.Kind), options) as IFeature;
            if (feat != null)
            {
                try { feat.Name = name; } catch { /* name in use: keep the generated name */ }
            }
            return feat;
        }

        /// <summary>Write new parameters to a feature that is open for editing (AccessSelections was called).</summary>
        public static bool Modify(IModelDoc2 model, IFeature feature, IMacroFeatureData md, TubeFeatureData data)
        {
            md.SetStringByName(ParamName, data.Encode());
            return feature.ModifyDefinition(md, model, null);
        }

        /// <summary>The tube body that a feature end belongs to: the body that the feature's macro feature produced.</summary>
        public static IBody2 BodyOf(IFeature f)
        {
            try
            {
                var faces = SwUtil.Arr<IFace2>((object)f.GetFaces());
                return faces.Length > 0 ? faces[0].GetBody() as IBody2 : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The current body of a tube: the body of the last feature (segment, Unbend, Rebend) of that tube that still owns faces.
        /// After an Unbend/Rebend the segment features own no faces any more, so they are skipped.
        /// </summary>
        public static IBody2 TubeBody(IReadOnlyList<StoredFeature> all, TubePath path)
        {
            var ids = new HashSet<string>(path.FeatureIds);
            var state = new HashSet<string>();
            foreach (var s in all)
                if ((s.Data.Kind == TubeFeatureKind.Unbend && s.Data.ParentId == path.RootId) || (s.Data.Kind == TubeFeatureKind.Rebend && state.Contains(s.Data.ParentId)))
                    state.Add(s.Data.Id);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i];
                if (s.Suppressed || !(ids.Contains(s.Data.Id) || state.Contains(s.Data.Id))) continue;
                var b = BodyOf(s.Feature);
                if (b != null) return b;
            }
            return null;
        }

        /// <summary>Insert the Tube Flat Pattern for a tube (it always stays at the end of the tree).</summary>
        public static IFeature InsertFlatPattern(IModelDoc2 model, IReadOnlyList<StoredFeature> all, TubePath path)
        {
            var body = TubeBody(all, path);
            if (body == null) return null;
            model.ClearSelection2(true);
            return Insert(model, new TubeFeatureData { Kind = TubeFeatureKind.FlatPattern, ParentId = path.RootId }, body, "Tube Flat Pattern");
        }

        /// <summary>Suppress the flat pattern (return to the bent state). Returns true when it was unsuppressed before.</summary>
        public static bool EnsureBentState(IModelDoc2 model)
        {
            bool changed = false;
            foreach (var s in Scan(model).Where(x => x.Data.Kind == TubeFeatureKind.FlatPattern && !x.Suppressed))
            {
                s.Feature.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                changed = true;
            }
            if (changed) model.ForceRebuild3(false);
            return changed;
        }
    }
}
