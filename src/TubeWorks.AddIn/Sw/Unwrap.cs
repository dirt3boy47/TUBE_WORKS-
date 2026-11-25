using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using TubeWorks.AddIn.Features;
using TubeWorks.Geometry;
using BendTable = TubeWorks.Geometry.BendTable;

namespace TubeWorks.AddIn.Sw
{
    /// <summary>
    /// Unwrap Tube Surface: straighten the actual tube in memory (with exact transfer of cuts in straight segments),
    /// take the boundary loops of its outside or inside cylindrical face, and develop them into a 2D pattern.
    /// The model is not changed.
    /// </summary>
    internal static class Unwrap
    {
        public static Pattern2D Run(ISldWorks app, IModelDoc2 model, UnwrapSurface surface, double seamAngle, out string error)
        {
            error = null;
            var all = TubeFeatureStore.Scan(model);
            if (StateFeatureBase.ActiveUnbends(all).Any()) { error = "An Unbend is active. Use Rebend first."; return null; }
            var chain = TubeFeatureStore.Solve(all);
            var path = chain.Paths.FirstOrDefault(p => p.RootIsBaseTube);
            if (path == null) { error = "Unwrap needs a valid tube that starts with a Base Tube feature."; return null; }
            var flat = all.FirstOrDefault(x => x.Data.Kind == TubeFeatureKind.FlatPattern && !x.Suppressed);
            if (flat != null) { error = "Switch to the bent state first (Flatten Tube)."; return null; }

            var actual = TubeFeatureStore.TubeBody(all, path);
            var c = new StateFeatureBase.Context
            {
                Features = all, Path = path, Math = (IMathUtility)app.GetMathUtility(),
                Builder = new BodyBuilder(app) { VerifyGeometry = UserSettings.Current.VerifyGeometry },
                Name = id => all.FirstOrDefault(x => x.Data.Id == id)?.Feature.Name ?? id,
            };
            var u = TubeUnbender.ForFeature(path, null);
            var rep = new TransferReport();
            var straight = StateFeatureBase.BentToUnbent(c, u, actual, rep, null, out error);
            if (straight == null) return null;

            var up = u.UnbentPath;
            Vec3 origin = up[0].StartPoint, axis = up[0].StartTangent;
            var prof = path.Profile;
            double faceR = surface == UnwrapSurface.Inside ? prof.InnerRadius : prof.OuterRadius;
            double refR = SurfaceUnwrapper.ReferenceRadius(prof, surface);
            var uw = new SurfaceUnwrapper(origin, axis, path.DevelopedLength, refR, seamAngle);

            var loops = new List<IReadOnlyList<Vec3>>();
            double chord = Math.Max(1e-6, prof.OuterDiameter / 400);
            int faces = 0;
            foreach (var face in SwUtil.Arr<IFace2>((object)straight.GetFaces()))
            {
                var surf = face.GetSurface() as ISurface;
                if (surf == null || !surf.IsCylinder()) continue;
                var cp = SwUtil.D((object)surf.CylinderParams); // origin (3), axis (3), radius
                if (cp == null || cp.Length < 7 || Math.Abs(cp[6] - faceR) > Math.Max(1e-7, faceR * 1e-6)) continue;
                faces++;
                foreach (var loop in SwUtil.Arr<ILoop2>((object)face.GetLoops())) loops.Add(LoopPoints(loop, chord));
            }
            if (faces == 0) { error = "The " + (surface == UnwrapSurface.Inside ? "inside" : "outside") + " cylindrical face was not found."; return null; }

            var table = BendTable.Build(path.Primitives);
            var marks = TubeUnbender.Marks(u, origin, axis, table.Rows);
            string title = $"TUBE OD {prof.OuterDiameter * 1000:0.###} x {prof.WallThickness * 1000:0.###}  L {path.DevelopedLength * 1000:0.###}  " +
                           $"{surface.ToString().ToUpperInvariant()} SURFACE  W {uw.Width * 1000:0.###}  SEAM {Angles.ToDeg(seamAngle):0.#}deg  {model.GetTitle()}";
            var pat = uw.Build(loops, marks, title);

            if (surface == UnwrapSurface.Neutral)
                pat.Warnings.Add("Neutral surface: cut outlines are taken from the outside surface and placed by angle on the mean diameter. " +
                                 "This is exact for radial cuts and approximate for others.");
            foreach (var n in rep.NotTransferred) pat.Warnings.Add("Not in the pattern: " + n);
            if (rep.MaterialAdded) pat.Warnings.Add("Material added to the tube (bosses, welds) is not in the pattern.");
            pat.Warnings.Add("Bend markings show the nominal centreline development (R × angle). No stretch or springback compensation is applied.");
            return pat;
        }

        /// <summary>Points of a loop in order. Each edge is sampled along its curve; edges are chained by nearest end points.</summary>
        private static List<Vec3> LoopPoints(ILoop2 loop, double chord)
        {
            var chains = new List<List<Vec3>>();
            foreach (var e in SwUtil.Arr<IEdge>((object)loop.GetEdges()))
            {
                var curve = e.GetCurve() as ICurve;
                var cpd = e.GetCurveParams3();
                if (curve == null || cpd == null) continue;
                double t0 = cpd.UMinValue, t1 = cpd.UMaxValue;
                // Length estimate from end points of 16 sub-intervals, then a sample spacing of about 'chord'.
                int n = 64;
                var pts = new List<Vec3>();
                for (int i = 0; i <= n; i++)
                {
                    var p = SwUtil.D((object)curve.Evaluate2(t0 + (t1 - t0) * i / n, 0));
                    if (p != null && p.Length >= 3) pts.Add(SwUtil.V(p));
                }
                double len = 0;
                for (int i = 1; i < pts.Count; i++) len += pts[i].DistanceTo(pts[i - 1]);
                int m = Math.Max(8, Math.Min(2000, (int)Math.Ceiling(len / chord)));
                if (m > n)
                {
                    pts.Clear();
                    for (int i = 0; i <= m; i++)
                    {
                        var p = SwUtil.D((object)curve.Evaluate2(t0 + (t1 - t0) * i / m, 0));
                        if (p != null && p.Length >= 3) pts.Add(SwUtil.V(p));
                    }
                }
                if (pts.Count > 1) chains.Add(pts);
            }
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
    }
}
