using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TubeWorks.Geometry;
using Xunit;

namespace TubeWorks.Geometry.Tests
{
    /// <summary>Phase 2: unbend/rebend transforms, surface unwrap, DXF, library. Units: metres, radians.</summary>
    public class Phase2Tests
    {
        static double Rad(double d) => d * Math.PI / 180;
        static void Near(Vec3 e, Vec3 a, double tol = 1e-9) => Assert.True(e.DistanceTo(a) <= tol, $"expected {e}, got {a}");

        static (TubePath path, TubeFeatureData b, TubeFeatureData b1, TubeFeatureData b2) TwoBendTube()
        {
            var b = new TubeFeatureData { Kind = TubeFeatureKind.BaseTube, OuterDiameter = 0.025, WallThickness = 0.002, Axis = Vec3.UnitX, Length = 0.1 };
            var b1 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b.Id, BendRadius = 0.05, BendAngle = Rad(90), StraightAfter = 0.1 };
            var b2 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b1.Id, BendRadius = 0.05, BendAngle = Rad(60), PlaneRotation = Rad(40), StraightAfter = 0.08 };
            var r = TubeChainSolver.Solve(new[] { b, b1, b2 }.Select(d => new ChainInput { Data = d, FeatureName = d.Kind.ToString() }).ToList());
            return (r.Paths.Single(), b, b1, b2);
        }

        [Fact]
        public void RigidTransform_ComposeAndInverse()
        {
            var a = RigidTransform.Rotation(new Vec3(1, 2, 3), new Vec3(1, 1, 0).Normalized(), 0.7);
            var b = RigidTransform.Translation(new Vec3(-4, 5, 0.5)).Compose(RigidTransform.Rotation(Vec3.Zero, Vec3.UnitZ, -1.1));
            var p = new Vec3(0.3, -0.2, 0.9);
            Near(a.Apply(b.Apply(p)), a.Compose(b).Apply(p), 1e-12);
            Near(p, a.Inverse().Apply(a.Apply(p)), 1e-12);
            Assert.Equal(1.0, a.C0.Cross(a.C1).Dot(a.C2), 12); // proper rotation
        }

        [Fact]
        public void UnbendAll_GivesStraightTubeOfDevelopedLength()
        {
            var (path, b, _, _) = TwoBendTube();
            var u = TubeUnbender.ForFeature(path, new TubeFeatureData { Kind = TubeFeatureKind.Unbend });
            var up = u.UnbentPath;
            // Continuous, collinear along the first segment, same total length.
            Vec3 axis = path.Primitives[0].StartTangent;
            for (int i = 0; i < up.Count; i++)
            {
                Assert.IsType<StraightPrimitive>(up[i]);
                Near(axis, up[i].StartTangent, 1e-12);
                if (i > 0) Near(up[i - 1].EndPoint, up[i].StartPoint, 1e-12);
            }
            Assert.Equal(path.DevelopedLength, up.Sum(p => p.Length), 12);
            Near(path.Primitives[0].StartPoint + axis * path.DevelopedLength, up.Last().EndPoint, 1e-12);
        }

        [Fact]
        public void UnbendOne_KeepsOtherBend_AndMovesStraightsRigidly()
        {
            var (path, _, b1, b2) = TwoBendTube();
            var u = TubeUnbender.ForFeature(path, new TubeFeatureData { Kind = TubeFeatureKind.Unbend, BendIds = { b1.Id } });
            Assert.Equal(1, u.Items.Count(i => i.Straightened));
            var kept = u.Items.Single(i => i.Bent is ArcPrimitive a && a.FeatureId == b2.Id);
            Assert.False(kept.Straightened);
            Assert.Equal(Rad(60), ((ArcPrimitive)kept.Unbent).Angle, 12);
            // Every non-straightened primitive is moved by its own rigid transform: check its end points.
            foreach (var it in u.Items.Where(i => !i.Straightened))
            {
                Near(it.BentToUnbent.Apply(it.Bent.StartPoint), it.Unbent.StartPoint, 1e-12);
                Near(it.BentToUnbent.Apply(it.Bent.EndPoint), it.Unbent.EndPoint, 1e-12);
            }
            // Path stays tangent-continuous.
            var up = u.UnbentPath;
            for (int i = 1; i < up.Count; i++) { Near(up[i - 1].EndPoint, up[i].StartPoint, 1e-12); Near(up[i - 1].EndTangent, up[i].StartTangent, 1e-12); }
        }

        [Fact]
        public void Unbend_FixedLastSegment_DoesNotMoveIt()
        {
            var (path, _, _, b2) = TwoBendTube();
            var u = TubeUnbender.ForFeature(path, new TubeFeatureData { Kind = TubeFeatureKind.Unbend, FixedFeatureId = b2.Id, FixedPrimitive = 1 });
            var last = u.Items.Last();
            Assert.True(last.BentToUnbent.AlmostEquals(RigidTransform.Identity, 1e-12));
            Near(path.Primitives.Last().EndPoint, u.UnbentPath.Last().EndPoint, 1e-12);
            Assert.Equal(path.DevelopedLength, u.UnbentPath.Sum(p => p.Length), 12);
            Assert.Throws<InvalidOperationException>(() => TubeUnbender.ForFeature(path, new TubeFeatureData { Kind = TubeFeatureKind.Unbend, FixedFeatureId = b2.Id, FixedPrimitive = 0 }));
        }

        [Fact]
        public void HoleTransfer_RoundTripIsExact_ForStraightRegions_AndRefusedInBends()
        {
            var (path, _, _, _) = TwoBendTube();
            var u = TubeUnbender.ForFeature(path, null);
            // A "hole" on the middle straight (after bend 1): points on the outer wall.
            var mid = (StraightPrimitive)path.Primitives[2];
            var c = mid.PointAt(0.05);
            var side = mid.Direction.Cross(Vec3.UnitZ).Normalized() * 0.0125;
            var hole = new[] { c + side + mid.Direction * 0.003, c + side - mid.Direction * 0.003, c + side + Vec3.UnitZ * 0.003 };
            int k = TubeUnbender.ClassifyRigid(u, hole);
            Assert.Equal(2, k);
            var moved = hole.Select(u.Items[k].BentToUnbent.Apply).ToList();
            Assert.Equal(2, TubeUnbender.ClassifyRigidUnbent(u, moved));
            var back = moved.Select(u.Items[k].BentToUnbent.Inverse().Apply).ToList();
            for (int i = 0; i < hole.Length; i++) Near(hole[i], back[i], 1e-12);
            // Same position measured along the developed length: 0.1 + 0.05*pi/2 + 0.05.
            var axis = path.Primitives[0].StartTangent;
            Assert.Equal(0.1 + 0.05 * Math.PI / 2 + 0.05, (u.Items[k].BentToUnbent.Apply(c) - path.Primitives[0].StartPoint).Dot(axis), 12);
            // A point inside bend 1 cannot be moved rigidly.
            var arc = (ArcPrimitive)path.Primitives[1];
            Assert.Equal(-1, TubeUnbender.ClassifyRigid(u, new[] { arc.PointAtAngle(arc.Angle / 2) + arc.PlaneNormal * 0.01 }));
            // Two points in different straights also cannot.
            Assert.Equal(-1, TubeUnbender.ClassifyRigid(u, new[] { path.Primitives[0].PointAt(0.05), mid.PointAt(0.05) }));
        }

        [Fact]
        public void Unwrap_Width_AndExactMapping()
        {
            var p = TubeProfile.FromOdWall(0.0254, 0.00165);
            double r = SurfaceUnwrapper.ReferenceRadius(p, UnwrapSurface.Outside);
            var uw = new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.5, r, 0);
            Assert.Equal(Math.PI * 0.0254, uw.Width, 12);
            Assert.Equal(Math.PI * (0.0254 - 0.00165), new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.5, SurfaceUnwrapper.ReferenceRadius(p, UnwrapSurface.Neutral), 0).Width, 12);
            // Point at 90° from the reference (RefX = +Y for axis Z... projection of Y), axial 0.2.
            var f = new TubeFrame(Vec3.Zero, Vec3.UnitZ);
            var q = f.ReferenceY * r + Vec3.UnitZ * 0.2;
            var (s, a) = uw.Map(q);
            Assert.Equal(0.2, s, 12);
            Assert.Equal(Math.PI / 2, a, 12);
        }

        [Fact]
        public void Unwrap_HoleCrossingSeam_IsSplitIntoTwoPieces_AtBothEdges()
        {
            double r = 0.01;
            var uw = new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.3, r, 0);
            var f = new TubeFrame(Vec3.Zero, Vec3.UnitZ);
            // Round-ish hole centred on the seam (angle 0) at s = 0.1, angular half-width 0.3 rad, axial half-width 0.004.
            var loop = new List<Vec3>();
            for (int i = 0; i < 64; i++)
            {
                double t = 2 * Math.PI * i / 64;
                double ang = 0.3 * Math.Cos(t), s = 0.1 + 0.004 * Math.Sin(t);
                loop.Add(f.ReferenceX * (r * Math.Cos(ang)) + f.ReferenceY * (r * Math.Sin(ang)) + Vec3.UnitZ * s);
            }
            var (pieces, winding) = uw.MapLoop(loop, PatternLayer.InternalCutouts);
            Assert.Equal(0, winding);
            Assert.Equal(2, pieces.Count);
            var ys = pieces.SelectMany(p => p.Points).Select(q => q.Y).ToList();
            Assert.All(ys, y => Assert.InRange(y, -1e-12, uw.Width + 1e-12));
            Assert.Contains(pieces, p => p.Points.All(q => q.Y <= 0.3 * r + 1e-9));
            Assert.Contains(pieces, p => p.Points.All(q => q.Y >= uw.Width - 0.3 * r - 1e-9));

            // Not on the seam: one closed polyline.
            var uw2 = new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.3, r, Math.PI);
            var (p2, w2) = uw2.MapLoop(loop, PatternLayer.InternalCutouts);
            Assert.Single(p2); Assert.True(p2[0].Closed); Assert.Equal(0, w2);
        }

        [Fact]
        public void Unwrap_EndCircle_IsRecognisedAsPlainEnd()
        {
            double r = 0.01;
            var uw = new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.3, r, 0.2);
            var f = new TubeFrame(Vec3.Zero, Vec3.UnitZ);
            List<Vec3> Circle(double s) => Enumerable.Range(0, 48).Select(i => f.ReferenceX * (r * Math.Cos(i * Math.PI / 24)) + f.ReferenceY * (r * Math.Sin(i * Math.PI / 24)) + Vec3.UnitZ * s).ToList();
            var pat = uw.Build(new[] { Circle(0), Circle(0.3) }, new BendMark[0], "t");
            var outer = pat.Polylines.Where(p => p.Layer == PatternLayer.OuterProfile).ToList();
            Assert.Equal(4, outer.Count); // rectangle
            Assert.Contains(outer, p => p.Points.All(q => Math.Abs(q.X) < 1e-12));
            Assert.Contains(outer, p => p.Points.All(q => Math.Abs(q.X - 0.3) < 1e-12));
        }

        [Fact]
        public void Unwrap_InteriorFaceBoundaryCircle_IsIgnored_AndMergeCollinearJoinsStraights()
        {
            double r = 0.01;
            var uw = new SurfaceUnwrapper(Vec3.Zero, Vec3.UnitZ, 0.3, r, 0);
            var f = new TubeFrame(Vec3.Zero, Vec3.UnitZ);
            List<Vec3> Circle(double s) => Enumerable.Range(0, 48).Select(i => f.ReferenceX * (r * Math.Cos(i * Math.PI / 24)) + f.ReferenceY * (r * Math.Sin(i * Math.PI / 24)) + Vec3.UnitZ * s).ToList();
            var pat = uw.Build(new[] { Circle(0), Circle(0.1), Circle(0.3) }, new BendMark[0], "t");
            Assert.Equal(4, pat.Polylines.Count(p => p.Layer == PatternLayer.OuterProfile));
            var (path, _, _, _) = TwoBendTube();
            var merged = TubeUnbender.MergeCollinear(TubeUnbender.ForFeature(path, null).UnbentPath);
            Assert.Single(merged);
            Assert.Equal(path.DevelopedLength, merged[0].Length, 12);
        }

        [Fact]
        public void Dxf_IsWellFormed_AndUsesLayersAndMillimetres()
        {
            var (path, _, _, _) = TwoBendTube();
            var u = TubeUnbender.ForFeature(path, null);
            var up = u.UnbentPath;
            var prof = path.Profile;
            double r = SurfaceUnwrapper.ReferenceRadius(prof, UnwrapSurface.Outside);
            var uw = new SurfaceUnwrapper(up[0].StartPoint, up[0].StartTangent, path.DevelopedLength, r, 0);
            var marks = TubeUnbender.Marks(u, up[0].StartPoint, up[0].StartTangent, BendTable.Build(path.Primitives).Rows);
            Assert.Equal(2, marks.Count);
            Assert.Equal(0.1, marks[0].StartS, 12);
            Assert.Equal(0.1 + 0.05 * Math.PI / 2, marks[0].EndS, 12);
            var pat = uw.Build(new List<IReadOnlyList<Vec3>>(), marks, "TEST");
            string dxf = DxfWriter.Write(pat);
            Assert.StartsWith("0\r\nSECTION\r\n2\r\nHEADER", dxf);
            Assert.EndsWith("0\r\nEOF\r\n", dxf);
            foreach (var n in new[] { "OUTER_PROFILE", "INTERNAL_CUTOUTS", "BEND_LINES", "CENTRELINE", "ENGRAVING", "CONSTRUCTION", "TEXT" }) Assert.Contains("\r\n" + n + "\r\n", dxf);
            // Developed length in mm appears as the right end line X.
            string mm = (path.DevelopedLength * 1000).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("\r\n10\r\n" + mm + "\r\n", dxf);
            var outFile = Path.Combine(Path.GetTempPath(), "tubeworks_test_pattern.dxf");
            File.WriteAllText(outFile, dxf, Encoding.ASCII);
        }

        [Fact]
        public void Library_CsvAndXmlRoundTrip_AndChecks()
        {
            var lib = new TubeLibrary();
            lib.Presets.Add(new TubePreset { Name = "A, \"quoted\"", Material = "304", OuterDiameter = 0.0254, WallThickness = 0.00165, BendRadii = { 0.0381, 0.0508 }, StockLengths = { 6.0 }, MinBendRadius = 0.0381, MaxBendAngleDeg = 185 });
            lib.Presets.Add(new TubePreset { Name = "B", OuterDiameter = 0.02, WallThickness = 0.0015 });
            var csv = lib.ToCsv();
            var back = TubeLibrary.FromCsv(csv);
            Assert.Equal(csv, back.ToCsv());
            Assert.Null(back.Presets[1].MinBendRadius);
            Assert.Equal(0.0508, back.Presets[0].BendRadii[1], 12);
            using (var ms = new MemoryStream()) { lib.WriteXml(ms); ms.Position = 0; Assert.Equal(csv, TubeLibrary.ReadXml(ms).ToCsv()); }
            Assert.Empty(lib.Check());
            Assert.Equal(Rad(185), lib.RulesFor(TubeProfile.FromOdWall(0.0254, 0.00165)).MaxBendAngle.Value, 12);
            lib.Presets.Add(new TubePreset { Name = "b", OuterDiameter = 0.02, WallThickness = 0.02, MinBendRadius = 0.005 });
            Assert.Equal(3, lib.Check().Count); // wall, min radius, duplicate name
            Assert.Throws<FormatException>(() => TubeLibrary.FromCsv("Name,OD (mm),Wall (mm)\nX,25;4,1"));
        }

        [Fact]
        public void Codec_UnbendFields()
        {
            var d = new TubeFeatureData { Kind = TubeFeatureKind.Unbend, BendIds = { "a", "b" }, FixedFeatureId = "f", FixedPrimitive = 1 };
            var e = TubeFeatureData.Decode(d.Encode());
            Assert.Equal(new[] { "a", "b" }, e.BendIds);
            Assert.Equal("f", e.FixedFeatureId); Assert.Equal(1, e.FixedPrimitive); Assert.True(e.IsStateFeature);
        }
    }
}
