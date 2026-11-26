using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using SolidWorks.Interop.sldworks;
using TubeWorks.AddIn.Features;
using TubeWorks.Geometry;
using BendTable = TubeWorks.Geometry.BendTable;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>Shop data for Phase 3: cost rates, saw data, batch options. Stored in %APPDATA%\TubeWorks\production.xml. No values are built in.</summary>
    public sealed class ProductionSettings
    {
        [TypeConverter(typeof(ExpandableObjectConverter)), Description("Hourly rate, set-up and process times. Leave a value empty when you do not know it: it is then left out of the estimate.")]
        public CostRates Rates { get; set; } = new CostRates();
        [TypeConverter(typeof(ExpandableObjectConverter)), Description("Saw kerf and unusable bar ends, in metres.")]
        public NestSettings Saw { get; set; } = new NestSettings();
        [Description("Postprocessor used by CNC Output and Batch (file name, or a built-in name).")]
        public string Postprocessor { get; set; } = "Generic LRA (CSV)";
        [Browsable(false)] public string BatchFolder { get; set; } = "";
        [Browsable(false)] public bool BatchPost { get; set; } = true;
        [Browsable(false)] public bool BatchDxf { get; set; } = true;
        [Browsable(false)] public bool BatchStep { get; set; }
        [Browsable(false)] public bool BatchTable { get; set; } = true;

        public static string FilePath => Path.Combine(UserSettings.Folder, "production.xml");
        private static ProductionSettings _current;
        public static ProductionSettings Current => _current ?? (_current = Load());

        private static ProductionSettings Load()
        {
            try { if (File.Exists(FilePath)) using (var s = File.OpenRead(FilePath)) return (ProductionSettings)new XmlSerializer(typeof(ProductionSettings)).Deserialize(s); }
            catch (Exception ex) { Log.Error("Load production settings", ex); }
            return new ProductionSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(UserSettings.Folder);
            using (var s = File.Create(FilePath)) new XmlSerializer(typeof(ProductionSettings)).Serialize(s, this);
        }
    }

    /// <summary>Postprocessor templates: two generic built-ins plus *.twpost files in %APPDATA%\TubeWorks\post.</summary>
    internal static class PostStore
    {
        public static string Folder => Path.Combine(UserSettings.Folder, "post");

        public static Dictionary<string, string> All()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Generic LRA (CSV)"] = PostTemplate.ExampleLraCsv,
                ["Generic XYZ"] = PostTemplate.ExampleXyz,
            };
            try
            {
                if (!Directory.Exists(Folder))
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(Path.Combine(Folder, "example-lra.twpost"), PostTemplate.ExampleLraCsv.Replace("\n", "\r\n"));
                }
                foreach (var f in Directory.GetFiles(Folder, "*.twpost")) d[Path.GetFileName(f)] = File.ReadAllText(f);
            }
            catch (Exception ex) { Log.Error("Postprocessor folder", ex); }
            return d;
        }
    }

    /// <summary>Everything needed to make a part: bend data, calibration, checks.</summary>
    internal sealed class PartResult
    {
        public string Name, PathName, Material = "";
        public TubePath Path;
        public BendTable Table;
        public TubePreset Preset;
        public CompensatedData Comp;
        public List<ValidationIssue> Issues = new List<ValidationIssue>();
        public string Error;
        public Dictionary<string, string> Fields => new Dictionary<string, string>
        {
            ["PART"] = Name, ["MATERIAL"] = Material,
            ["OD"] = (Path.Profile.OuterDiameter * 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["WALL"] = (Path.Profile.WallThickness * 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["DATE"] = DateTime.Now.ToString("yyyy-MM-dd"),
        };
    }

    internal static class PartAnalysis
    {
        public static PartResult Run(ISldWorks app, IModelDoc2 model, TubeLibrary lib, bool withCuts)
        {
            var r = new PartResult { Name = System.IO.Path.GetFileNameWithoutExtension(model.GetTitle()), PathName = model.GetPathName() };
            try
            {
                var all = TubeFeatureStore.Scan(model);
                if (StateFeatureBase.ActiveUnbends(all).Any()) { r.Error = "an Unbend is active (use Rebend)"; return r; }
                var chain = TubeFeatureStore.Solve(all);
                r.Path = chain.Paths.FirstOrDefault(p => p.RootIsBaseTube) ?? chain.Paths.FirstOrDefault();
                if (r.Path == null)
                {
                    var bad = chain.Features.FirstOrDefault(f => f.Error != null);
                    r.Error = bad != null ? $"{bad.FeatureName}: {bad.Error}" : "no TubeWorks tube";
                    return r;
                }
                r.Material = (model as IPartDoc)?.GetMaterialPropertyName2("", out string _) ?? "";
                r.Table = BendTable.Build(r.Path.Primitives);
                r.Preset = lib.Match(r.Path.Profile);
                r.Comp = Compensation.Apply(r.Table, r.Preset?.Compensation);
                if (r.Preset == null) r.Comp.Notes.Insert(0, "No tube library preset for this tube: no calibration, tooling or stock data.");
                var rules = lib.RulesFor(r.Path.Profile);
                r.Issues.AddRange(TubeValidator.Validate(chain, rules));
                if (withCuts && r.Path.RootIsBaseTube)
                {
                    var actual = TubeFeatureStore.TubeBody(all, r.Path);
                    var cuts = CutAnalysis.Analyze(new BodyBuilder(app) { VerifyGeometry = false }, r.Path, TubeUnbender.ForFeature(r.Path, null), actual, id => id, out bool _, out string _);
                    if (cuts != null) r.Issues.AddRange(TubeValidator.ValidateCuts(r.Path, cuts.Select(c => new CutRange(c.S0, c.S1, c.Description)), rules));
                }
                if (!r.Path.RootIsBaseTube) r.Comp.Notes.Add("The tube starts on a body that TubeWorks did not create; that body is not in the lengths.");
            }
            catch (Exception ex) { Log.Error("PartAnalysis", ex); r.Error = ex.Message; }
            return r;
        }

        public static List<NestStock> Stock(TubePreset p) =>
            p == null ? new List<NestStock>() :
            p.StockLengths.Select((l, i) => new NestStock { Length = l, PricePerBar = i < p.StockPrices.Count ? p.StockPrices[i] : (p.PricePerMetre.HasValue ? p.PricePerMetre * l : null) }).ToList();
    }
}
