using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn
{
    /// <summary>Remembered operator preferences. Stored in %APPDATA%\TubeWorks\settings.xml.</summary>
    public sealed class UserSettings
    {
        public double LastOuterDiameter { get; set; } = 0.025;
        public double LastWallThickness { get; set; } = 0.002;
        public double LastLength { get; set; } = 0.1;
        public double LastBendRadius { get; set; } = 0.05;
        public double LastBendAngle { get; set; } = Math.PI / 2;
        public double LastStraightAfter { get; set; } = 0;
        public double LastExtension { get; set; } = 0.1;
        public string LastMaterial { get; set; } = "";
        public bool LivePreview { get; set; } = true;
        public bool ShowCentreline { get; set; } = true;
        /// <summary>Check every created body against its analytic volume. Switch off only for diagnosis.</summary>
        public bool VerifyGeometry { get; set; } = true;
        public RotationSign RotationSign { get; set; } = RotationSign.RightHandAboutFeed;
        public AngleConvention AngleConvention { get; set; } = AngleConvention.BendAngle;
        public LengthUnit TableUnit { get; set; } = LengthUnit.Millimetre;

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TubeWorks");
        private static string FilePath => Path.Combine(Folder, "settings.xml");

        private static UserSettings _current;
        public static UserSettings Current => _current ?? (_current = Load());

        private static UserSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath))
                        return (UserSettings)new XmlSerializer(typeof(UserSettings)).Deserialize(s);
            }
            catch (Exception ex) { Log.Error("Load settings", ex); }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using (var s = File.Create(FilePath)) new XmlSerializer(typeof(UserSettings)).Serialize(s, this);
            }
            catch (Exception ex) { Log.Error("Save settings", ex); }
        }
    }

    /// <summary>One tube preset of the library: profile, material and optional manufacturing limits (metres).</summary>
    public sealed class TubePreset
    {
        public string Name { get; set; } = "";
        public string Material { get; set; } = "";
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        /// <summary>Optional: available centreline radii of the tooling.</summary>
        public List<double> BendRadii { get; set; } = new List<double>();
        public double? MinBendRadius { get; set; }
        public double? MinStraightBetweenBends { get; set; }
        public double? MinClampLength { get; set; }
        public string Machine { get; set; } = "";
        public string Tooling { get; set; } = "";
    }

    /// <summary>
    /// Tube library. Release 1 reads presets from %APPDATA%\TubeWorks\library.xml (see docs/library-example.xml).
    /// The library editor is planned for Phase 2. No limits are built in: a check runs only when its limit is defined.
    /// </summary>
    public sealed class TubeLibrary
    {
        public List<TubePreset> Presets { get; set; } = new List<TubePreset>();

        private static string FilePath => Path.Combine(UserSettings.Folder, "library.xml");

        public static TubeLibrary Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath))
                        return (TubeLibrary)new XmlSerializer(typeof(TubeLibrary)).Deserialize(s);
            }
            catch (Exception ex) { Log.Error("Load library", ex); }
            return new TubeLibrary();
        }

        /// <summary>Preset with the same profile (within 1 µm), or null.</summary>
        public TubePreset Match(TubeProfile p) =>
            Presets.Find(x => Math.Abs(x.OuterDiameter - p.OuterDiameter) < 1e-6 && Math.Abs(x.WallThickness - p.WallThickness) < 1e-6);

        public ManufacturingRules RulesFor(TubeProfile p)
        {
            var m = Match(p);
            var r = new ManufacturingRules();
            if (m != null) { r.MinBendRadius = m.MinBendRadius; r.MinStraightBetweenBends = m.MinStraightBetweenBends; r.MinClampLength = m.MinClampLength; }
            return r;
        }
    }

    internal static class Log
    {
        public static void Error(string where, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(UserSettings.Folder);
                File.AppendAllText(Path.Combine(UserSettings.Folder, "tubeworks.log"), $"{DateTime.Now:s} {where}: {ex}\r\n");
            }
            catch { }
        }
    }
}
