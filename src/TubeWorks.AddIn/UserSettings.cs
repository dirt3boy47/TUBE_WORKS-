using System;
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
        /// <summary>
        /// Experimental: imprint bend start/finish lines on the Flat Pattern blank. Off by default since 0.4.0 because
        /// SOLIDWORKS 2026 SP4 closed during this step (crash report of 2026-11-26). Bend lines are always in the DXF, DWG and drawing.
        /// </summary>
        public bool MarkBendLinesOnBlank { get; set; }
        public string LastDrawingTemplate { get; set; } = "";
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

    /// <summary>Reads and writes the tube library file %APPDATA%\TubeWorks\library.xml.</summary>
    internal static class LibraryStore
    {
        public static string FilePath => Path.Combine(UserSettings.Folder, "library.xml");

        public static TubeLibrary Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath)) return TubeLibrary.ReadXml(s);
            }
            catch (Exception ex) { Log.Error("Load library", ex); }
            return new TubeLibrary();
        }

        public static void Save(TubeLibrary lib)
        {
            Directory.CreateDirectory(UserSettings.Folder);
            string tmp = FilePath + ".tmp";
            using (var s = File.Create(tmp)) lib.WriteXml(s);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
            else File.Move(tmp, FilePath);
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
