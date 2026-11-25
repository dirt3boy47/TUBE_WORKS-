using System;
using System.Collections.Generic;
using System.Linq;

namespace TubeWorks.Geometry
{
    /// <summary>One centreline primitive in the bent state and its counterpart in the (partly) straightened state.</summary>
    public sealed class UnbentPrimitive
    {
        public int Index { get; set; }
        public PathPrimitive Bent { get; set; }
        public PathPrimitive Unbent { get; set; }
        /// <summary>True when this primitive is a bend that was straightened.</summary>
        public bool Straightened { get; set; }
        /// <summary>
        /// Rigid motion from the bent state to the unbent state. Exact for every primitive that is not straightened
        /// (straight segments and kept bends move as rigid bodies). For a straightened bend it is the motion of its
        /// start cross-section only; material inside a straightened bend does not move rigidly.
        /// </summary>
        public RigidTransform BentToUnbent { get; set; }
    }

    public sealed class UnbendResult
    {
        public List<UnbentPrimitive> Items { get; } = new List<UnbentPrimitive>();
        public List<PathPrimitive> UnbentPath => Items.Select(i => i.Unbent).ToList();
    }

    /// <summary>
    /// Straightens selected bends of a tube path. The developed centreline length of each bend (R·θ) is kept:
    /// a straightened bend becomes a straight of length R·θ. The selected fixed primitive does not move.
    /// </summary>
    public static class TubeUnbender
    {
        /// <param name="path">Bent centreline, in order.</param>
        /// <param name="straighten">Predicate: true for each arc that must be straightened.</param>
        /// <param name="fixedIndex">Index of the primitive that stays in place.</param>
        public static UnbendResult Unbend(IReadOnlyList<PathPrimitive> path, Func<ArcPrimitive, bool> straighten, int fixedIndex)
        {
            if (path.Count == 0) throw new ArgumentException("The path is empty.");
            if (fixedIndex < 0 || fixedIndex >= path.Count) throw new ArgumentOutOfRangeException(nameof(fixedIndex));
            if (path[fixedIndex] is ArcPrimitive fa && straighten(fa))
                throw new ArgumentException("The fixed segment cannot be a bend that is straightened.");

            var res = new UnbendResult();
            var items = new UnbentPrimitive[path.Count];
            items[fixedIndex] = new UnbentPrimitive { Index = fixedIndex, Bent = path[fixedIndex], Unbent = path[fixedIndex], BentToUnbent = RigidTransform.Identity };

            // Forward from the fixed primitive.
            var m = RigidTransform.Identity;
            for (int i = fixedIndex + 1; i < path.Count; i++)
            {
                var prev = path[i - 1];
                if (prev is ArcPrimitive pa && straighten(pa)) m = m.Compose(StraightenMotion(pa, forward: true));
                items[i] = Make(i, path[i], m, straighten);
            }
            // Backward from the fixed primitive.
            m = RigidTransform.Identity;
            for (int i = fixedIndex - 1; i >= 0; i--)
            {
                var next = path[i + 1];
                if (next is ArcPrimitive na && straighten(na)) m = m.Compose(StraightenMotion(na, forward: false));
                items[i] = Make(i, path[i], m, straighten);
            }
            res.Items.AddRange(items);
            return res;
        }

        private static UnbentPrimitive Make(int i, PathPrimitive p, RigidTransform m, Func<ArcPrimitive, bool> straighten)
        {
            var it = new UnbentPrimitive { Index = i, Bent = p, BentToUnbent = m };
            if (p is ArcPrimitive a && straighten(a))
            {
                it.Straightened = true;
                it.Unbent = new StraightPrimitive(m.Apply(a.StartPoint), m.ApplyDir(a.StartTangent), a.Length) { FeatureId = a.FeatureId };
            }
            else it.Unbent = Transform(p, m);
            return it;
        }

        /// <summary>
        /// Motion (in the bent coordinates of the arc) that straightens the arc and carries the material beyond it.
        /// Forward: material after the arc. Rotation by -θ about the bend axis through the end point, then translation
        /// so that the end lies at start + T0·Rθ. Backward: the mirror case for material before the arc.
        /// </summary>
        private static RigidTransform StraightenMotion(ArcPrimitive a, bool forward)
        {
            if (forward)
            {
                Vec3 target = a.StartPoint + a.StartTangent * a.Length;
                var rot = RigidTransform.Rotation(a.EndPoint, a.PlaneNormal, -a.Angle);
                return RigidTransform.Translation(target - a.EndPoint).Compose(rot);
            }
            else
            {
                Vec3 target = a.EndPoint - a.EndTangent * a.Length;
                var rot = RigidTransform.Rotation(a.StartPoint, a.PlaneNormal, a.Angle);
                return RigidTransform.Translation(target - a.StartPoint).Compose(rot);
            }
        }

        public static PathPrimitive Transform(PathPrimitive p, RigidTransform m)
        {
            if (p is StraightPrimitive s) return new StraightPrimitive(m.Apply(s.StartPoint), m.ApplyDir(s.Direction), s.Length) { FeatureId = s.FeatureId };
            var a = (ArcPrimitive)p;
            return new ArcPrimitive(m.Apply(a.StartPoint), m.ApplyDir(a.StartTangent), m.ApplyDir(a.BendDirection), a.Radius, a.Angle) { FeatureId = a.FeatureId };
        }

        /// <summary>Unbend as defined by an Unbend feature (or all bends with the first segment fixed, for the flat pattern).</summary>
        public static UnbendResult ForFeature(TubePath path, TubeFeatureData unbend)
        {
            var prims = path.Primitives;
            var ids = new HashSet<string>(unbend?.BendIds ?? new List<string>());
            Func<ArcPrimitive, bool> pick = a => ids.Count == 0 || ids.Contains(a.FeatureId);
            int fixedIndex = 0;
            if (unbend != null && !string.IsNullOrEmpty(unbend.FixedFeatureId))
            {
                var own = Enumerable.Range(0, prims.Count).Where(i => prims[i].FeatureId == unbend.FixedFeatureId).ToList();
                if (own.Count == 0) throw new InvalidOperationException("The fixed segment of the Unbend feature no longer exists. Edit the Unbend feature.");
                fixedIndex = own[Math.Max(0, Math.Min(own.Count - 1, unbend.FixedPrimitive))];
            }
            else
            {
                // First primitive that is not straightened.
                fixedIndex = Enumerable.Range(0, prims.Count).FirstOrDefault(i => !(prims[i] is ArcPrimitive a && pick(a)));
            }
            if (prims[fixedIndex] is ArcPrimitive fa && pick(fa))
                throw new InvalidOperationException("The fixed segment is a bend that is being straightened. Select a straight segment.");
            return Unbend(prims, pick, fixedIndex);
        }

        /// <summary>Bend markings for a fully straightened tube, measured from the start of the unbent path.</summary>
        public static List<BendMark> Marks(UnbendResult u, Vec3 origin, Vec3 axis, IReadOnlyList<BendRow> rows)
        {
            var frame = new TubeFrame(origin, axis);
            var marks = new List<BendMark>();
            int n = 0;
            foreach (var it in u.Items.Where(i => i.Straightened))
            {
                var a = (ArcPrimitive)it.Bent;
                double s0 = (it.Unbent.StartPoint - origin).Dot(frame.Tangent);
                Vec3 d = it.BentToUnbent.ApplyDir(a.BendDirection);
                var row = rows != null && n < rows.Count ? rows[n] : null;
                n++;
                marks.Add(new BendMark
                {
                    Number = n, StartS = s0, EndS = s0 + a.Length, Radius = a.Radius, Angle = a.Angle, Rotation = row?.Rotation ?? 0,
                    InsideAngle = Math.Atan2(d.Dot(frame.ReferenceY), d.Dot(frame.ReferenceX)),
                });
            }
            return marks;
        }

        /// <summary>Join consecutive collinear straights into one (for building one continuous cylinder face).</summary>
        public static List<PathPrimitive> MergeCollinear(IReadOnlyList<PathPrimitive> prims)
        {
            var r = new List<PathPrimitive>();
            foreach (var p in prims)
            {
                if (p is StraightPrimitive s && r.Count > 0 && r[r.Count - 1] is StraightPrimitive last
                    && last.Direction.AlmostEquals(s.Direction, 1e-12) && last.EndPoint.AlmostEquals(s.StartPoint, 1e-12))
                    r[r.Count - 1] = new StraightPrimitive(last.StartPoint, last.Direction, last.Length + s.Length) { FeatureId = last.FeatureId };
                else r.Add(p);
            }
            return r;
        }

        // ------------------------------------------------------------------ region classification

        /// <summary>Closest primitive to a point, with the axial parameter s along it (unclamped) and the radial distance.</summary>
        public static (int index, double s, double radial) Locate(IReadOnlyList<PathPrimitive> path, Vec3 p)
        {
            int best = -1; double bestD = double.MaxValue, bestS = 0, bestR = 0;
            for (int i = 0; i < path.Count; i++)
            {
                var (s, d, inside) = Project(path[i], p);
                // Prefer the primitive whose axial range contains the point; break ties by distance.
                double score = d + (inside ? 0 : 1e6);
                if (score < bestD) { bestD = score; best = i; bestS = s; bestR = d; }
            }
            return (best, bestS, bestR);
        }

        private static (double s, double dist, bool inside) Project(PathPrimitive pr, Vec3 p)
        {
            const double eps = 1e-9;
            if (pr is StraightPrimitive st)
            {
                double s = (p - st.StartPoint).Dot(st.Direction);
                double sc = Math.Max(0, Math.Min(st.Length, s));
                return (s, p.DistanceTo(st.PointAt(sc)), s >= -eps && s <= st.Length + eps);
            }
            var a = (ArcPrimitive)pr;
            Vec3 v = p - a.Center;
            Vec3 inPlane = v - a.PlaneNormal * v.Dot(a.PlaneNormal);
            double ang = Math.Atan2(inPlane.Dot(a.StartTangent), inPlane.Dot(-a.BendDirection));
            if (ang < -Math.PI / 2) ang += 2 * Math.PI; // keep [-90°, 270°) so that slightly negative angles stay near 0
            double angC = Math.Max(0, Math.Min(a.Angle, ang));
            return (ang * a.Radius, p.DistanceTo(a.PointAtAngle(angC)), ang >= -eps && ang <= a.Angle + eps);
        }

        /// <summary>
        /// Classify a region (sample points of a cut) of the tube. Returns the index of a primitive whose rigid
        /// motion is valid for every point, or -1 when a point lies in a straightened bend or the points lie in parts
        /// that move differently (then the region cannot be moved rigidly and an exact transfer is not possible).
        /// </summary>
        public static int ClassifyRigid(UnbendResult u, IEnumerable<Vec3> bentPoints)
        {
            var path = u.Items.Select(i => i.Bent).ToList();
            int first = -1;
            foreach (var p in bentPoints)
            {
                var (i, _, _) = Locate(path, p);
                if (i < 0 || u.Items[i].Straightened) return -1;
                if (first < 0) first = i;
                else if (!u.Items[i].BentToUnbent.AlmostEquals(u.Items[first].BentToUnbent, 1e-9)) return -1;
            }
            return first;
        }

        /// <summary>Same as <see cref="ClassifyRigid(UnbendResult, IEnumerable{Vec3})"/> for points given in the unbent state.</summary>
        public static int ClassifyRigidUnbent(UnbendResult u, IEnumerable<Vec3> unbentPoints)
        {
            var path = u.Items.Select(i => i.Unbent).ToList();
            int first = -1;
            foreach (var p in unbentPoints)
            {
                var (i, _, _) = Locate(path, p);
                if (i < 0 || u.Items[i].Straightened) return -1;
                if (first < 0) first = i;
                else if (!u.Items[i].BentToUnbent.AlmostEquals(u.Items[first].BentToUnbent, 1e-9)) return -1;
            }
            return first;
        }
    }
}
