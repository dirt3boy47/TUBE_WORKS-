using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TubeWorks.Geometry
{
    public enum TubeFeatureKind
    {
        BaseTube = 0,
        Straight = 1,
        Bend = 2,
        FlatPattern = 3,
    }

    /// <summary>Which end of the parent feature this feature continues from.</summary>
    public enum ParentEnd
    {
        /// <summary>The free end of the parent (the end of its last segment).</summary>
        End = 0,
        /// <summary>The start face of a Base Tube (the tube continues backwards along -axis).</summary>
        Start = 1,
    }

    public enum StraightEndCondition
    {
        Blind = 0,
        UpToPoint = 1,
        UpToPlane = 2,
    }

    /// <summary>
    /// Persistent parameters of one TubeWorks feature. Stored as one string parameter of the SOLIDWORKS
    /// macro feature (see <see cref="Encode"/>). Lengths are metres, angles radians.
    /// Computed values (start/end positions, tangents, developed length) are NOT stored here: they are
    /// derived by <see cref="TubeChainSolver"/> on every rebuild, so they can never get out of date.
    /// </summary>
    public sealed class TubeFeatureData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Unique feature identifier (GUID, "N" format). Stable for the life of the feature.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public TubeFeatureKind Kind { get; set; }

        /// <summary>Id of the parent TubeWorks feature. Empty for a root (Base Tube, or a bend/straight on a non-TubeWorks tube).</summary>
        public string ParentId { get; set; } = "";
        public ParentEnd ParentEnd { get; set; } = ParentEnd.End;

        // ----- Profile (Base Tube; and roots on a foreign tube store the detected profile) -----
        public double OuterDiameter { get; set; }
        public double WallThickness { get; set; }
        public string Material { get; set; } = "";
        public string MaterialDatabase { get; set; } = "";

        // ----- Base Tube placement, or the anchor of a root on a foreign tube face -----
        public Vec3 Origin { get; set; } = Vec3.Zero;
        public Vec3 Axis { get; set; } = Vec3.UnitZ;

        /// <summary>Base Tube length or straight extension length.</summary>
        public double Length { get; set; }
        public StraightEndCondition EndCondition { get; set; } = StraightEndCondition.Blind;
        /// <summary>Target point (UpToPoint) or a point on the target plane (UpToPlane), captured when the feature was edited.</summary>
        public Vec3 UpToPoint { get; set; } = Vec3.Zero;
        public Vec3 UpToPlaneNormal { get; set; } = Vec3.UnitZ;

        // ----- Bend -----
        public double BendRadius { get; set; }
        public double BendAngle { get; set; }
        public double PlaneRotation { get; set; }
        public bool ReverseDirection { get; set; }
        public double StraightAfter { get; set; }

        // ----- Options -----
        public bool Merge { get; set; } = true;

        public bool IsRoot => string.IsNullOrEmpty(ParentId);

        public TubeProfile GetProfile() => TubeProfile.FromOdWall(OuterDiameter, WallThickness);

        public TubeFeatureData Clone() => Decode(Encode());

        // ------------------------------------------------------------------------------------------
        // Encoding: "key=value;key=value". Values use invariant culture "R" format. ';' '=' '\' are escaped.
        // Unknown keys are ignored, so later releases can add keys without breaking older files.
        // ------------------------------------------------------------------------------------------

        public string Encode()
        {
            var d = new List<KeyValuePair<string, string>>
            {
                Kv("v", SchemaVersion), Kv("id", Id), Kv("kind", (int)Kind), Kv("parent", ParentId), Kv("pend", (int)ParentEnd),
                Kv("od", OuterDiameter), Kv("wall", WallThickness), Kv("mat", Material), Kv("matdb", MaterialDatabase),
                KvV("org", Origin), KvV("axis", Axis), Kv("len", Length), Kv("endc", (int)EndCondition),
                KvV("upt", UpToPoint), KvV("upn", UpToPlaneNormal),
                Kv("r", BendRadius), Kv("ang", BendAngle), Kv("rot", PlaneRotation), Kv("rev", ReverseDirection ? 1 : 0),
                Kv("after", StraightAfter), Kv("merge", Merge ? 1 : 0),
            };
            var sb = new StringBuilder();
            foreach (var kv in d)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(Escape(kv.Value));
            }
            return sb.ToString();
        }

        public static TubeFeatureData Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("TubeWorks feature data is empty.");
            var map = Parse(text);
            var f = new TubeFeatureData();
            f.SchemaVersion = GetInt(map, "v", 1);
            if (f.SchemaVersion > CurrentSchemaVersion)
                throw new FormatException($"This feature was saved by a newer TubeWorks release (data version {f.SchemaVersion}). Update the add-in.");
            f.Id = Get(map, "id", f.Id);
            f.Kind = (TubeFeatureKind)GetInt(map, "kind", 0);
            f.ParentId = Get(map, "parent", "");
            f.ParentEnd = (ParentEnd)GetInt(map, "pend", 0);
            f.OuterDiameter = GetD(map, "od");
            f.WallThickness = GetD(map, "wall");
            f.Material = Get(map, "mat", "");
            f.MaterialDatabase = Get(map, "matdb", "");
            f.Origin = GetV(map, "org", Vec3.Zero);
            f.Axis = GetV(map, "axis", Vec3.UnitZ);
            f.Length = GetD(map, "len");
            f.EndCondition = (StraightEndCondition)GetInt(map, "endc", 0);
            f.UpToPoint = GetV(map, "upt", Vec3.Zero);
            f.UpToPlaneNormal = GetV(map, "upn", Vec3.UnitZ);
            f.BendRadius = GetD(map, "r");
            f.BendAngle = GetD(map, "ang");
            f.PlaneRotation = GetD(map, "rot");
            f.ReverseDirection = GetInt(map, "rev", 0) != 0;
            f.StraightAfter = GetD(map, "after");
            f.Merge = GetInt(map, "merge", 1) != 0;
            return f;
        }

        private static KeyValuePair<string, string> Kv(string k, string v) => new KeyValuePair<string, string>(k, v ?? "");
        private static KeyValuePair<string, string> Kv(string k, int v) => Kv(k, v.ToString(CultureInfo.InvariantCulture));
        private static KeyValuePair<string, string> Kv(string k, double v) => Kv(k, v.ToString("R", CultureInfo.InvariantCulture));
        private static KeyValuePair<string, string> KvV(string k, Vec3 v) =>
            Kv(k, string.Join(",", v.X.ToString("R", CultureInfo.InvariantCulture), v.Y.ToString("R", CultureInfo.InvariantCulture), v.Z.ToString("R", CultureInfo.InvariantCulture)));

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\s").Replace("=", "\\e");

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    sb.Append(c == 's' ? ';' : c == 'e' ? '=' : c);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                map[part.Substring(0, eq)] = Unescape(part.Substring(eq + 1));
            }
            return map;
        }

        private static string Get(Dictionary<string, string> m, string k, string def) => m.TryGetValue(k, out var v) ? v : def;
        private static int GetInt(Dictionary<string, string> m, string k, int def) =>
            m.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        private static double GetD(Dictionary<string, string> m, string k) =>
            m.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0.0;
        private static Vec3 GetV(Dictionary<string, string> m, string k, Vec3 def)
        {
            if (!m.TryGetValue(k, out var v)) return def;
            var p = v.Split(',');
            if (p.Length != 3) return def;
            return new Vec3(
                double.Parse(p[0], CultureInfo.InvariantCulture),
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture));
        }
    }
}
