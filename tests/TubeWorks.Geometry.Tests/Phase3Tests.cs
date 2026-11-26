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
