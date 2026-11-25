using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>Creates SOLIDWORKS MathTransforms from engine transforms. The array layout is checked once with a test point.</summary>
    internal static class Transforms
    {
        private static bool? _transposed;

        public static MathTransform Create(IMathUtility mu, RigidTransform t)
        {
            if (_transposed == null) Calibrate(mu);
            return (MathTransform)mu.CreateTransform(t.ToSwArray(_transposed.Value));
        }

        private static void Calibrate(IMathUtility mu)
        {
            var test = RigidTransform.Translation(new Vec3(0.3, -0.2, 0.1)).Compose(RigidTransform.Rotation(Vec3.Zero, new Vec3(1, 2, 3).Normalized(), 1.0));
            var p = new Vec3(0.5, 0.7, -0.4);
            Vec3 expected = test.Apply(p);
            foreach (bool tr in new[] { false, true })
            {
                var xf = (MathTransform)mu.CreateTransform(test.ToSwArray(tr));
                var mp = (IMathPoint)mu.CreatePoint(p.ToArray());
                var r = SwUtil.D(((IMathPoint)mp.MultiplyTransform(xf)).ArrayData);
                if (r != null && SwUtil.V(r).AlmostEquals(expected, 1e-9)) { _transposed = tr; return; }
            }
            throw new InvalidOperationException("Could not determine the SOLIDWORKS MathTransform layout.");
        }
    }

    /// <summary>Material removed from a tube (holes, slots, notches, cut-outs), with sample points for classification.</summary>
    internal sealed class RemovedPiece
    {
        public IBody2 Body;
        public List<Vec3> Samples = new List<Vec3>();
        public double Volume;
    }

    /// <summary>Result of moving cuts between the bent and the straightened state. Shown by the Unbend/Rebend/Flatten commands.</summary>
    internal sealed class TransferReport
    {
        public int Transferred;
        public List<string> NotTransferred = new List<string>();
        public bool MaterialAdded;
        public override string ToString()
        {
            var lines = new List<string> { $"{Transferred} cut(s) transferred exactly (rigid motion of a straight segment)." };
            if (NotTransferred.Count > 0)
            {
                lines.Add($"{NotTransferred.Count} cut(s) NOT transferred:");
                lines.AddRange(NotTransferred.Select(s => "  • " + s));
            }
            if (MaterialAdded) lines.Add("Material added after the tube features (bosses, welds) is not transferred.");
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Finds cut material by comparing the nominal tube body (built from the stored parameters) with the actual body,
    /// and moves each cut with the rigid motion of the straight segment that contains it. A cut that touches a bend,
    /// or spans parts that move differently, is not moved: there is no exact rigid mapping for it. It is reported.
    /// </summary>
    internal static class MaterialTransfer
    {
        public const double MinVolume = 1e-11; // 0.01 mm³: ignore boolean slivers

        /// <summary>Reports by feature Id, for the commands to show after a rebuild.</summary>
        public static readonly ConcurrentDictionary<string, TransferReport> Reports = new ConcurrentDictionary<string, TransferReport>();

        /// <summary>
        /// Bent-state cuts that an Unbend could not transfer (they lie in a bend). Rebend restores them unchanged.
        /// Key = Unbend feature Id. Filled when the Unbend feature rebuilds in this session.
        /// </summary>
        public static readonly ConcurrentDictionary<string, List<IBody2>> HeldBack = new ConcurrentDictionary<string, List<IBody2>>();

        public static List<RemovedPiece> Removed(IBody2 nominal, IBody2 actual, out bool added)
        {
            var pieces = new List<RemovedPiece>();
            int err;
            var cut = SwUtil.Arr<IBody2>(((IBody2)nominal.Copy()).Operations2((int)swBodyOperationType_e.SWBODYCUT, actual.Copy(), out err));
            foreach (var b in cut)
            {
                var mp = SwUtil.D(b.GetMassProperties(1.0));
                double vol = mp != null && mp.Length > 3 ? mp[3] : 0;
                if (vol < MinVolume) continue;
                pieces.Add(new RemovedPiece { Body = b, Volume = vol, Samples = Samples(b) });
            }
            var extra = SwUtil.Arr<IBody2>(((IBody2)actual.Copy()).Operations2((int)swBodyOperationType_e.SWBODYCUT, nominal.Copy(), out err));
            added = extra.Any(b => { var m = SwUtil.D(b.GetMassProperties(1.0)); return m != null && m.Length > 3 && m[3] >= MinVolume; });
            return pieces;
        }

        /// <summary>Vertices and bounding-box corners of a body.</summary>
        private static List<Vec3> Samples(IBody2 b)
        {
            var list = new List<Vec3>();
            foreach (var v in SwUtil.Arr<IVertex>(b.GetVertices()))
            {
                var p = SwUtil.D(v.GetPoint());
                if (p != null) list.Add(SwUtil.V(p));
            }
            var box = SwUtil.D(b.GetBodyBox());
            if (box != null && box.Length >= 6)
                for (int i = 0; i < 8; i++)
                    list.Add(new Vec3((i & 1) == 0 ? box[0] : box[3], (i & 2) == 0 ? box[1] : box[4], (i & 4) == 0 ? box[2] : box[5]));
            return list;
        }

        /// <summary>Where a piece is, for messages: "at 123.4 mm along the tube, in Bend 02".</summary>
        public static string Describe(IReadOnlyList<PathPrimitive> path, RemovedPiece piece, Func<string, string> featureName)
        {
            var c = piece.Samples.Count > 0 ? piece.Samples.Aggregate(Vec3.Zero, (a, b) => a + b) / piece.Samples.Count : Vec3.Zero;
            var (i, s, _) = TubeUnbender.Locate(path, c);
            if (i < 0) return "cut of unknown position";
            double along = path.Take(i).Sum(p => p.Length) + Math.Max(0, s);
            string where = path[i] is ArcPrimitive ? $"in {featureName(path[i].FeatureId)} (bend region)" : $"spans a bend or several segments near {featureName(path[i].FeatureId)}";
            return $"cut of {piece.Volume * 1e9:0.#} mm³ at {along * 1000:0.#} mm along the tube, {where}";
        }

        /// <summary>Subtract pieces from a body, one by one. Returns null and an error when the modeler fails.</summary>
        public static IBody2 Subtract(IBody2 body, IEnumerable<IBody2> tools, TransferReport report, out string error)
        {
            error = null;
            foreach (var t in tools)
            {
                int err;
                var res = SwUtil.Arr<IBody2>(body.Operations2((int)swBodyOperationType_e.SWBODYCUT, t.Copy(), out err));
                if (res.Length == 0) { error = $"Applying a transferred cut failed (modeler error {err})."; return null; }
                // A cut can split the tube; keep the largest part and report it.
                body = res.OrderByDescending(b => { var m = SwUtil.D(b.GetMassProperties(1.0)); return m != null && m.Length > 3 ? m[3] : 0; }).First();
                if (res.Length > 1) report?.NotTransferred.Add("A cut divides the tube into several bodies; only the largest body is kept.");
            }
            return body;
        }
    }
}
