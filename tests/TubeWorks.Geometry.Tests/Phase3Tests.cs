using System;
using System.Collections.Generic;
using System.Linq;
using TubeWorks.Geometry;
using Xunit;

namespace TubeWorks.Geometry.Tests
{
    /// <summary>Phase 3: compensation, postprocessor, nesting, cost, report. Expected values are hand calculations.</summary>
    public class Phase3Tests
    {
        static double Rad(double d) => d * Math.PI / 180;

        static BendTable Table()
        {
            // Base 100 mm, bend 90° R50 + 100 mm, bend 60° R50 rotated 40° + 80 mm.
            var b = new TubeFeatureData { Kind = TubeFeatureKind.BaseTube, OuterDiameter = 0.025, WallThickness = 0.002, Axis = Vec3.UnitX, Length = 0.1 };
            var b1 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b.Id, BendRadius = 0.05, BendAngle = Rad(90), StraightAfter = 0.1 };
            var b2 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b1.Id, BendRadius = 0.05, BendAngle = Rad(60), PlaneRotation = Rad(40), StraightAfter = 0.08 };
            var r = TubeChainSolver.Solve(new[] { b, b1, b2 }.Select(d => new ChainInput { Data = d, FeatureName = d.Kind.ToString() }).ToList());
            return BendTable.Build(r.Paths[0].Primitives);
        }

        [Fact]
        public void Springback_Interpolation()
        {
            var pts = new List<SpringbackPoint> { new SpringbackPoint { CommandedDeg = 47, AchievedDeg = 45 }, new SpringbackPoint { CommandedDeg = 93, AchievedDeg = 90 } };
            Assert.Equal(47, Compensation.CommandFor(45, pts), 12);
            Assert.Equal(70, Compensation.CommandFor(67.5, pts), 12);           // midway: (47+93)/2
            Assert.Equal(93 + (120 - 90) * 46.0 / 45, Compensation.CommandFor(120, pts), 9); // extrapolated with slope 46/45
            Assert.Equal(60 * 93.0 / 90, Compensation.CommandFor(60, new[] { pts[1] }), 12); // single point: ratio
            Assert.Equal(30, Compensation.CommandFor(30, new SpringbackPoint[0]), 12);
        }

        [Fact]
        public void NoCalibration_GivesNominalValues_AndSaysSo()
        {
            var t = Table();
            var c = Compensation.Apply(t, new CompensationData());
            Assert.Equal(t.TotalDevelopedLength, c.CutLength, 12);
            Assert.All(c.Rows, r => Assert.Equal(r.TargetDeg, r.CommandDeg, 12));
            Assert.Contains(c.Notes, n => n.StartsWith("Springback: no calibration data"));
            Assert.Contains(c.Notes, n => n.StartsWith("Stretch: no data"));
        }

        [Fact]
        public void Compensation_CutLengthFormula()
        {
            var t = Table();
            var data = new CompensationData { NeutralAxisShift = -0.001, ElongationPerBend = 0.0005, ElongationPerDegree = 0.00001, TrimStart = 0.005, TrimEnd = 0.01, AngleOffsetDeg = 0.5,
                Springback = { new SpringbackPoint { CommandedDeg = 92, AchievedDeg = 90 } } };
            var c = Compensation.Apply(t, data);
            // straights 0.1+0.1+0.08 ; bends (0.049)(π/2 + π/3) ; elongation 2×0.0005 + 0.00001×150 ; trims 0.015
            double expected = 0.015 + 0.28 + 0.049 * (Math.PI / 2 + Math.PI / 3) - (0.001 + 0.0015);
            Assert.Equal(expected, c.CutLength, 12);
            Assert.Equal(0.1 + 0.005, c.Rows[0].Feed, 12);
            Assert.Equal(0.08 + 0.01, c.FinalFeed, 12);
            Assert.Equal(92 + 0.5, c.Rows[0].CommandDeg, 9);
            Assert.Equal(60 * 92.0 / 90 + 0.5, c.Rows[1].CommandDeg, 9);
            Assert.Empty(Compensation.Check(data));
            Assert.NotEmpty(Compensation.Check(new CompensationData { Springback = { new SpringbackPoint { CommandedDeg = 80, AchievedDeg = 90 } } }));
        }

        [Fact]
        public void Postprocessor_Template_OutputAndErrors()
        {
            var t = Table();
            var c = Compensation.Apply(t, new CompensationData());
            var post = PostTemplate.Parse(PostTemplate.ExampleLraCsv);
            string outText = post.Run(t, c, new Dictionary<string, string> { ["PART"] = "P1", ["OD"] = "25", ["WALL"] = "2", ["MATERIAL"] = "304", ["DATE"] = "2026-11-26" });
            Assert.Contains("# P1  OD 25 x 2  304", outText);
            Assert.Contains("1,100.00,0.00,90.00,50.00", outText);
            Assert.Contains("2,100.00,", outText);
            Assert.Contains("End,80.00,,,", outText);
            Assert.Contains("Cut length," + (t.TotalDevelopedLength * 1000).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), outText);
            var xyz = PostTemplate.Parse(PostTemplate.ExampleXyz).Run(t, c, new Dictionary<string, string> { ["PART"] = "P1", ["OD"] = "25", ["WALL"] = "2" });
            Assert.Contains("P1 150.000 0.000 0.000 R50.000", xyz); // vertex of bend 1 = start + T*R*tan(45°)
            Assert.Throws<FormatException>(() => PostTemplate.Parse("name=x\n[bend]\n{N} {NOPE}\n").Run(t, c, new Dictionary<string, string>()));
            Assert.Throws<FormatException>(() => PostTemplate.Parse("colour=red\n[bend]\n{N}\n"));
            var pos = PostTemplate.Parse("rotation=left\nrotationrange=0-360\n[bend]\n{R};\n");
            Assert.DoesNotContain("-", pos.Run(t, c, new Dictionary<string, string>()));
        }

        [Fact]
        public void Nesting_ValidAndUsesShortestFittingStock()
        {
            var parts = new[] { new NestPart { Name = "A", Length = 2.5, Quantity = 3 }, new NestPart { Name = "B", Length = 1.2, Quantity = 4 }, new NestPart { Name = "C", Length = 0.4, Quantity = 5 } };
            var stock = new[] { new NestStock { Length = 6.0, PricePerBar = 60 }, new NestStock { Length = 3.0, PricePerBar = 33 } };
            var s = new NestSettings { Kerf = 0.003, BarStartTrim = 0.01, BarEndTrim = 0.05 };
            var r = StockNesting.Nest(parts, stock, s);
            Assert.Empty(r.Errors);
            Assert.Equal(12, r.Bars.Sum(b => b.Parts.Count));
            foreach (var b in r.Bars)
            {
                double used = s.BarStartTrim + s.BarEndTrim + b.Parts.Sum(p => p.length) + s.Kerf * (b.Parts.Count - 1);
                Assert.Equal(used, b.Used, 12);
                Assert.True(used <= b.StockLength + 1e-12);
            }
            // Total part length 7.5 + 4.8 + 2.0 = 14.3 m -> at least 3 bars of 6 m; lower bound on stock = 14.3 + overheads.
            Assert.Equal(14.3, r.PartLength, 9);
            Assert.True(r.StockUsed >= 14.3);
            Assert.True(r.Utilisation > 0.8, $"utilisation {r.Utilisation}");
            Assert.NotNull(StockNesting.Cost(r, stock));
            var tooLong = StockNesting.Nest(new[] { new NestPart { Name = "X", Length = 5.98 } }, stock, s);
            Assert.Contains("longer than the longest usable stock", tooLong.Errors[0]);
            var limited = StockNesting.Nest(new[] { new NestPart { Name = "Y", Length = 5, Quantity = 2 } }, new[] { new NestStock { Length = 6, Available = 1 } }, new NestSettings());
            Assert.Single(limited.Bars); Assert.Contains("not enough stock", limited.Errors[0]);
        }

        [Fact]
        public void Nesting_MatchesOptimum_OnSmallRandomCases()
        {
            var rnd = new Random(7);
            for (int k = 0; k < 200; k++)
            {
                var lens = Enumerable.Range(0, 7).Select(_ => Math.Round(0.3 + rnd.NextDouble() * 2.5, 2)).ToList();
                var r = StockNesting.Nest(lens.Select((l, i) => new NestPart { Name = "P" + i, Length = l }), new[] { new NestStock { Length = 3.0 } }, new NestSettings());
                int opt = OptimalBins(lens, 3.0);
                // First-fit decreasing guarantee: at most 11/9·OPT + 6/9 bars.
                Assert.True(r.Bars.Count <= Math.Floor(11.0 / 9 * opt + 6.0 / 9 + 1e-9), $"case {k}: {r.Bars.Count} bars, optimum {opt}");
            }
        }

        static int OptimalBins(List<double> items, double cap)
        {
            int best = items.Count;
            var bins = new List<double>();
            void Go(int i)
            {
                if (bins.Count >= best) return;
                if (i == items.Count) { best = bins.Count; return; }
                for (int b = 0; b < bins.Count; b++)
                    if (bins[b] + items[i] <= cap + 1e-12) { bins[b] += items[i]; Go(i + 1); bins[b] -= items[i]; }
                bins.Add(items[i]); Go(i + 1); bins.RemoveAt(bins.Count - 1);
            }
            Go(0);
            return best;
        }

        [Fact]
        public void Cost_OnlyDefinedItems()
        {
            var t = Table();
            var c = Compensation.Apply(t, new CompensationData());
            var none = CostModel.Estimate(c, t, 10, new CostRates());
            Assert.Empty(none.Lines);
            Assert.Contains("material price", none.NotDefined);
            var rates = new CostRates { MaterialPerMetre = 12, HourlyRate = 90, SetupHours = 0.5, CutSeconds = 10, LoadUnloadSeconds = 20, SecondsPerBend = 8, SecondsPerRotation = 2, FeedSecondsPerMetre = 5 };
            var e = CostModel.Estimate(c, t, 10, rates);
            double cycle = 10 + 20 + 2 * 8 + 1 * 2 + 5 * c.CutLength; // bend 2 has a rotation
            Assert.Equal(cycle, e.CycleSeconds, 9);
            double perPart = c.CutLength * 12 + cycle / 3600 * 90 + 0.5 * 90 / 10;
            Assert.Equal(perPart, e.PerPart, 9);
            Assert.Equal(perPart * 10, e.Batch, 9);
            var fromNest = CostModel.Estimate(c, t, 10, rates, materialFromNest: 120);
            Assert.Equal(12, fromNest.Lines[0].PerPart, 12);
        }

        [Fact]
        public void Report_IsHtml_AndEscapesText()
        {
            var t = Table();
            var c = Compensation.Apply(t, new CompensationData());
            var rep = new ManufacturingReport("Tube <A&B>");
            rep.Part("Part \"1\"", TubeProfile.FromOdWall(0.025, 0.002), "304", t, c,
                new[] { new ValidationIssue { Severity = IssueSeverity.Warning, Message = "w <x>" } }, CostModel.Estimate(c, t, 2, new CostRates { MaterialPerMetre = 10 }));
            var nest = StockNesting.Nest(new[] { new NestPart { Name = "P1", Length = c.CutLength, Quantity = 2 } }, new[] { new NestStock { Length = 6 } }, new NestSettings());
            rep.Nest(nest, new NestSettings());
            string html = rep.Html();
            Assert.StartsWith("<!doctype html>", html);
            Assert.Contains("Tube &lt;A&amp;B&gt;", html);
            Assert.Contains("w &lt;x&gt;", html);
            Assert.Contains("Bars: 1.", html);
            Assert.EndsWith("</html>", html);
        }
    }
}

namespace TubeWorks.Geometry.Tests
{
    public class BendingLayoutTests
    {
        [Fact]
        public void Steps_MatchTheDrawingInTheScreenshot()
        {
            // Screenshot case: feed 50, bend 45° R100, bend 45° R100 rotated 35°, no straight between.
            var b = new TubeFeatureData { Kind = TubeFeatureKind.BaseTube, OuterDiameter = 0.065, WallThickness = 0.002, Axis = Vec3.UnitX, Length = 0.05 };
            var b1 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b.Id, BendRadius = 0.1, BendAngle = Math.PI / 4 };
            var b2 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b1.Id, BendRadius = 0.1, BendAngle = Math.PI / 4, PlaneRotation = 35 * Math.PI / 180 };
            var r = TubeChainSolver.Solve(new[] { b, b1, b2 }.Select(d => new ChainInput { Data = d }).ToList());
            var t = BendTable.Build(r.Paths[0].Primitives);
            var steps = BendingLayout.Steps(t);
            double arc = 0.1 * Math.PI / 4; // 78.54 mm
            Assert.Equal(0.05, steps[0].MarkStart, 12);
            Assert.Equal(0.05 + arc / 2, steps[0].MarkCentre, 12);
            Assert.Equal(0.05 + arc, steps[1].MarkStart, 12);           // 128.54 mm
            Assert.Equal(45, steps[1].DesignDeg, 9);
            Assert.Equal(0.05 + 2 * arc, t.TotalDevelopedLength, 12);    // 207.08 mm

            // With a start trim of 5 mm and springback 46:45, marks move by 5 mm and the command angle is 46°.
            var c = Compensation.Apply(t, new CompensationData { TrimStart = 0.005, Springback = { new SpringbackPoint { CommandedDeg = 46, AchievedDeg = 45 } } });
            var s2 = BendingLayout.Steps(t, c);
            Assert.Equal(0.055, s2[0].MarkStart, 12);
            Assert.Equal(46, s2[0].CommandDeg, 9);
            var pat = BendingLayout.Build(t, 0.065, c, 0.003);
            Assert.Contains(pat.Texts, x => x.Text.StartsWith("B2  MARK 133.5  ROTATE"));
            Assert.Contains(pat.Texts, x => x.Text.Contains("COMMAND 46°"));
            Assert.Equal(c.CutLength, pat.Length, 12);
        }
    }
}

namespace TubeWorks.Geometry.Tests
{
    public class BendingGuideTests
    {
        static BendTable ScreenshotTube(double rotDeg)
        {
            var b = new TubeFeatureData { Kind = TubeFeatureKind.BaseTube, OuterDiameter = 0.065, WallThickness = 0.002, Axis = Vec3.UnitX, Length = 0.05 };
            var b1 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b.Id, BendRadius = 0.1, BendAngle = Math.PI / 4, StraightAfter = 0.05 };
            var b2 = new TubeFeatureData { Kind = TubeFeatureKind.Bend, ParentId = b1.Id, BendRadius = 0.1, BendAngle = Math.PI / 4, PlaneRotation = rotDeg * Math.PI / 180, StraightAfter = 0.05 };
            var r = TubeChainSolver.Solve(new[] { b, b1, b2 }.Select(d => new ChainInput { Data = d }).ToList());
            return BendTable.Build(r.Paths[0].Primitives);
        }

        [Fact]
        public void Svg_IsWellFormed_AndHasColouredZonesMarksAndLabels()
        {
            var t = ScreenshotTube(35);
            string svg = BendingGuide.Svg(t, null, TubeProfile.FromOdWall(0.065, 0.002));
            var x = System.Xml.Linq.XDocument.Parse(svg); // throws when not well-formed
            Assert.Equal("svg", x.Root.Name.LocalName);
            Assert.Contains(BendingGuide.Red, svg); Assert.Contains(BendingGuide.Blue, svg); Assert.Contains(BendingGuide.Green, svg);
            // Bend starts 50 and 50+78.54+50 = 178.5 mm; ends 128.5 and 257.1 mm; cut length 307.1 mm.
            foreach (var v in new[] { ">50<", ">128.5<", ">178.5<", ">257.1<", ">307.1<" }) Assert.Contains(v, svg);
            Assert.Contains("turn tube 35° ACW", x.Root.Value); // the encoder writes ° as &#176; in the file
        }

        [Fact]
        public void Instructions_TurnDirectionFollowsTheGeometry()
        {
            // The rotation in the bend table is right-hand about the direction from S to the far end.
            // Seen from S looking along that direction, right-hand is clockwise; the tube is turned the other way.
            var plus = BendingGuide.Instructions(ScreenshotTube(35), null);
            Assert.Contains(plus, s => s.Contains("must point 35° clockwise from bend 1") && s.Contains("turn the tube 35° anticlockwise"));
            var minus = BendingGuide.Instructions(ScreenshotTube(-35), null);
            Assert.Contains(minus, s => s.Contains("must point 35° anticlockwise from bend 1") && s.Contains("turn the tube 35° clockwise"));
            var same = BendingGuide.Instructions(ScreenshotTube(0), null);
            Assert.Contains(same, s => s.Contains("Do not turn the tube"));
            Assert.Contains(plus, s => s.StartsWith("Cut the tube to 307.1 mm"));
            Assert.Contains(plus, s => s.Contains("B2 at 178.5 mm"));
        }

        [Fact]
        public void SheetLayout_FitsWidth_HatchStaysInsideZones_AndHasBubbles()
        {
            var t = ScreenshotTube(35);
            var p = TubeProfile.FromOdWall(0.065, 0.002);
            var pat = BendingGuide.Sheet(t, null, p, 0.36);
            var pts = pat.Polylines.SelectMany(x => x.Points).ToList();
            Assert.True(pts.Min(q => q.X) >= -1e-9 && pts.Max(q => q.X) <= 0.36 + 1e-9);
            Assert.Equal(2, pat.Circles.Count);
            // Bend zone hatch lines lie between the bend start and end marks.
            double k = 0.36 / t.TotalDevelopedLength;
            var steps = BendingLayout.Steps(t);
            var bendHatch = pat.Polylines.Where(x => x.Layer == PatternLayer.BendLines && Math.Abs(x.Points[1].Y - x.Points[0].Y) > 1e-9 && Math.Abs(x.Points[1].X - x.Points[0].X) > 1e-9).ToList();
            Assert.NotEmpty(bendHatch);
            Assert.All(bendHatch, h => Assert.Contains(steps, s => h.Points.All(q => q.X >= s.MarkStart * k - 1e-9 && q.X <= (s.MarkStart + s.BendLength) * k + 1e-9)));
            Assert.Contains(pat.Polylines, x => x.Layer == PatternLayer.GuideFirstStraight);
            Assert.Contains(pat.Texts, x => x.Text == "178.5");
            Assert.Contains(pat.Texts, x => x.Text.StartsWith("TURN TUBE 35° ACW"));
            Assert.All(BendingGuide.Wrap(string.Join(" ", BendingGuide.Instructions(t, null)), 60), l => Assert.True(l.Length <= 60 || !l.Contains(' ')));
        }

        [Fact]
        public void RightHandRotation_LooksClockwiseFromS()
        {
            // Independent check of the wording: rotate +Y by +90° right-hand about +X (direction from S along the tube).
            // An observer at S looks along +X; with "up" = +Y and their right = -Z (right-handed frame: right = forward × up... ),
            // +Y rotated right-hand about +X goes to +Z, which is on the observer's LEFT... verify numerically below.
            var up = Vec3.UnitY.RotateAbout(Vec3.UnitX, Math.PI / 2);
            Assert.True(up.AlmostEquals(Vec3.UnitZ, 1e-12));
            // Observer looking along +X with up = +Y: their right-hand side is forward × up = X × Y = +Z.
            var right = Vec3.UnitX.Cross(Vec3.UnitY);
            Assert.True(right.AlmostEquals(Vec3.UnitZ, 1e-12)); // top moved to the right = clockwise as seen by that observer
        }
    }
}
