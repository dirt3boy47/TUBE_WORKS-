using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using TubeWorks.Geometry;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>One cut of the actual tube, analysed against the fully straightened tube.</summary>
    internal sealed class CutInfo
    {
        public RemovedPiece Piece;
        /// <summary>Index of the primitive whose rigid motion moves the cut exactly; -1 when the cut touches a bend.</summary>
        public int RigidIndex;
        /// <summary>True when the cut reaches the bore (a hole, slot or notch); false for a shallow cut (engraving, marking).</summary>
        public bool Through;
        /// <summary>Developed-length interval of the cut, from the start of the tube.</summary>
        public double S0, S1;
        public string Description;
    }

    /// <summary>Cut detection shared by Unwrap, Manufacturing Check and the Unbend page.</summary>
    internal static class CutAnalysis
    {
        public static List<CutInfo> Analyze(BodyBuilder builder, TubePath path, UnbendResult fullUnbend, IBody2 actual, Func<string, string> name, out bool added, out string error)
        {
            added = false; error = null;
            var list = new List<CutInfo>();
            if (actual == null) return list;
            var nominal = builder.Build(path.Primitives, path.Profile, out error);
            if (nominal == null) return null;
            var prims = path.Primitives;
            var up = fullUnbend.UnbentPath;
            Vec3 origin = up[0].StartPoint, axis = up[0].StartTangent;
            double chord = Math.Max(1e-6, path.Profile.OuterDiameter / 200);
            double tolR = Math.Max(1e-6, path.Profile.WallThickness * 0.02);

            foreach (var p in MaterialTransfer.Removed(nominal, actual, out added))
            {
                var edge = EdgeSamples(p.Body, chord);
                var all = p.Samples.Concat(edge).ToList();
                var info = new CutInfo { Piece = p, RigidIndex = TubeUnbender.ClassifyRigid(fullUnbend, all) };
                double minR = edge.Count > 0 ? edge.Min(q => TubeUnbender.Locate(prims, q).radial) : 0;
                info.Through = minR <= path.Profile.InnerRadius + tolR;
                var s = (edge.Count > 0 ? edge : all).Select(q => (TubeUnbender.MapPoint(fullUnbend, q).point - origin).Dot(axis)).ToList();
                info.S0 = s.Min(); info.S1 = s.Max();
                info.Description = (info.Through ? "Cut" : "Engraving") + $" at {info.S0 * 1000:0.#}–{info.S1 * 1000:0.#} mm developed";
                list.Add(info);
            }
            return list;
        }

        /// <summary>Sample points of every edge of a body.</summary>
        public static List<Vec3> EdgeSamples(IBody2 body, double chord)
        {
            var pts = new List<Vec3>();
            foreach (var e in SwUtil.Arr<IEdge>((object)body.GetEdges())) pts.AddRange(EdgePoints(e, chord));
            return pts;
        }

        public static List<Vec3> EdgePoints(IEdge e, double chord)
        {
            var pts = new List<Vec3>();
            var curve = e.GetCurve() as ICurve;
            var cpd = e.GetCurveParams3();
            if (curve == null || cpd == null) return pts;
            double t0 = cpd.UMinValue, t1 = cpd.UMaxValue;
            int n = 64;
            for (int i = 0; i <= n; i++)
            {
                var p = SwUtil.D((object)curve.Evaluate2(t0 + (t1 - t0) * i / n, 0));
                if (p != null && p.Length >= 3) pts.Add(SwUtil.V(p));
            }
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += pts[i].DistanceTo(pts[i - 1]);
            int m = Math.Max(8, Math.Min(2000, (int)Math.Ceiling(len / chord)));
            if (m <= n) return pts;
            pts.Clear();
            for (int i = 0; i <= m; i++)
            {
                var p = SwUtil.D((object)curve.Evaluate2(t0 + (t1 - t0) * i / m, 0));
                if (p != null && p.Length >= 3) pts.Add(SwUtil.V(p));
            }
            return pts;
        }

        /// <summary>Points of a loop in order. Each edge is sampled along its curve; edges are chained by nearest end points.</summary>
        public static List<Vec3> LoopPoints(ILoop2 loop, double chord)
        {
            var chains = SwUtil.Arr<IEdge>((object)loop.GetEdges()).Select(e => EdgePoints(e, chord)).Where(c => c.Count > 1).ToList();
            var result = new List<Vec3>();
            if (chains.Count == 0) return result;
            result.AddRange(chains[0]);
            chains.RemoveAt(0);
            while (chains.Count > 0)
            {
                var end = result[result.Count - 1];
                int best = 0; bool rev = false; double bd = double.MaxValue;
                for (int i = 0; i < chains.Count; i++)
                {
                    double d0 = chains[i][0].DistanceTo(end), d1 = chains[i][chains[i].Count - 1].DistanceTo(end);
                    if (d0 < bd) { bd = d0; best = i; rev = false; }
                    if (d1 < bd) { bd = d1; best = i; rev = true; }
                }
                var next = chains[best];
                if (rev) next.Reverse();
                result.AddRange(next.Skip(1));
                chains.RemoveAt(best);
            }
            if (result.Count > 2 && result[0].DistanceTo(result[result.Count - 1]) < 1e-9) result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>
        /// Outline loops of a cut on a surface of the BENT tube at radius <paramref name="radius"/> from the centreline:
        /// the loops of the cut body's faces whose points all lie at that radius.
        /// </summary>
        public static List<List<Vec3>> OutlinesAtRadius(IBody2 piece, IReadOnlyList<PathPrimitive> bentPath, double radius, double chord)
        {
            var res = new List<List<Vec3>>();
            double tol = Math.Max(2e-6, radius * 1e-4);
            foreach (var f in SwUtil.Arr<IFace2>((object)piece.GetFaces()))
            {
                var loops = SwUtil.Arr<ILoop2>((object)f.GetLoops()).Select(l => LoopPoints(l, chord)).Where(l => l.Count > 2).ToList();
                if (loops.Count == 0) continue;
                if (loops.All(l => l.All(q => Math.Abs(TubeUnbender.Locate(bentPath, q).radial - radius) <= tol))) res.AddRange(loops);
            }
            return res;
        }
    }
}
