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
                foreach (var loop in SwUtil.Arr<ILoop2>((object)face.GetLoops())) loops.Add(CutAnalysis.LoopPoints(loop, chord));
            }
            if (faces == 0) { error = "The " + (surface == UnwrapSurface.Inside ? "inside" : "outside") + " cylindrical face was not found."; return null; }

            var table = BendTable.Build(path.Primitives);
            var marks = TubeUnbender.Marks(u, origin, axis, table.Rows);
            string title = $"TUBE OD {prof.OuterDiameter * 1000:0.###} x {prof.WallThickness * 1000:0.###}  L {path.DevelopedLength * 1000:0.###}  " +
                           $"{surface.ToString().ToUpperInvariant()} SURFACE  W {uw.Width * 1000:0.###}  SEAM {Angles.ToDeg(seamAngle):0.#}deg  {model.GetTitle()}";
            var pat = uw.Build(loops, marks, title);

            // Cuts: engraving (does not reach the bore) and cuts in bends (no exact transfer).
            var cuts = CutAnalysis.Analyze(c.Builder, path, u, actual, c.Name, out bool _, out error) ?? new List<CutInfo>();
            error = null;
            int approx = 0;
            foreach (var cut in cuts)
            {
                var layer = cut.Through ? PatternLayer.InternalCutouts : PatternLayer.Engraving;
                if (cut.RigidIndex < 0)
                {
                    // Not in the straightened body: develop its outline from the bent tube with the bending model (approximate).
                    foreach (var outline in CutAnalysis.OutlinesAtRadius(cut.Piece.Body, path.Primitives, faceR, chord))
                    {
                        var mapped = outline.Select(q => TubeUnbender.MapPoint(u, q).point).ToList();
                        pat.Polylines.AddRange(uw.MapLoop(mapped, layer).pieces);
                    }
                    approx++;
                    pat.Warnings.Add("APPROXIMATE (in a bend): " + cut.Description + ". Developed with the centreline bending model; the real position changes with stretch.");
                }
                else if (!cut.Through)
                {
                    // Exact outline is already in the pattern as a cut-out: move it to the engraving layer.
                    var box = cut.Piece.Samples.Concat(CutAnalysis.EdgeSamples(cut.Piece.Body, chord))
                        .Select(q => uw.Map(u.Items[cut.RigidIndex].BentToUnbent.Apply(q))).ToList();
                    double s0 = box.Min(q => q.s) - chord, s1 = box.Max(q => q.s) + chord;
                    double a0 = box.Min(q => q.angle) * refR - chord, a1 = box.Max(q => q.angle) * refR + chord;
                    foreach (var pl in pat.Polylines.Where(x => x.Layer == PatternLayer.InternalCutouts))
                        if (pl.Points.All(q => q.X >= s0 && q.X <= s1 && q.Y >= a0 && q.Y <= a1)) pl.Layer = PatternLayer.Engraving;
                }
            }
            pat.Simplify(1e-6);

            if (surface == UnwrapSurface.Neutral)
                pat.Warnings.Add("Neutral surface: cut outlines are taken from the outside surface and placed by angle on the mean diameter. " +
                                 "This is exact for radial cuts and approximate for others.");
            if (rep.MaterialAdded) pat.Warnings.Add("Material added to the tube (bosses, welds) is not in the pattern.");
            if (approx == 0 && rep.NotTransferred.Count > 0) foreach (var n in rep.NotTransferred) pat.Warnings.Add("Not in the pattern: " + n);
            pat.Warnings.Add("Bend markings show the nominal centreline development (R × angle). No stretch or springback compensation is applied.");
            return pat;
        }

    }
}
