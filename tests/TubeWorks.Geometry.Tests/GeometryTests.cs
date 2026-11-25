using System;
using System.Collections.Generic;
using System.Linq;
using TubeWorks.Geometry;
using Xunit;

namespace TubeWorks.Geometry.Tests
{
    /// <summary>
    /// Expected values in these tests are hand-derived closed forms, independent of the engine code:
    ///   arc length = R * theta;  end of a bend in its plane = P0 + R*sin(theta)*T0 + R*(1 - cos(theta))*D.
    /// Units: metres and radians.
    /// </summary>
    public class GeometryTests
    {
        const double Tol = 1e-9;
        static double Rad(double d) => d * Math.PI / 180;

        static TubeFeatureData Base(double len, Vec3? axis = null, double od = 0.025, double wall = 0.002) => new TubeFeatureData
        {
            Kind = TubeFeatureKind.BaseTube, OuterDiameter = od, WallThickness = wall, Origin = Vec3.Zero, Axis = axis ?? Vec3.UnitX, Length = len,
        };

        static TubeFeatureData Bend(TubeFeatureData parent, double r, double angDeg, double rotDeg = 0, double after = 0, bool rev = false) => new TubeFeatureData
        {
            Kind = TubeFeatureKind.Bend, ParentId = parent.Id, BendRadius = r, BendAngle = Rad(angDeg), PlaneRotation = Rad(rotDeg), StraightAfter = after, ReverseDirection = rev,
        };

        static TubeFeatureData Straight(TubeFeatureData parent, double len) => new TubeFeatureData { Kind = TubeFeatureKind.Straight, ParentId = parent.Id, Length = len };

        static ChainResult Solve(params TubeFeatureData[] f) =>
            TubeChainSolver.Solve(f.Select((d, i) => new ChainInput { Data = d, FeatureName = $"F{i}" }).ToList());

        static void Near(Vec3 expected, Vec3 actual, double tol = Tol) =>
            Assert.True(expected.DistanceTo(actual) <= tol, $"expected {expected}, got {actual}");

        [Fact]
        public void StraightTube_DevelopedLengthEqualsLength()
        {
            var r = Solve(Base(1.0));
            Assert.Single(r.Paths);
            Assert.Equal(1.0, r.Paths[0].DevelopedLength, 12);
            var t = BendTable.Build(r.Paths[0].Primitives);
            Assert.Empty(t.Rows);
            Assert.Equal(1.0, t.FinalStraight, 12);
        }

        [Fact]
        public void Single90DegreeBend_EndPointTangentAndLength()
        {
            var b = Base(0.100);
            var bend = Bend(b, 0.050, 90, after: 0.200);
            var r = Solve(b, bend);
            var fs = r.Find(bend.Id);
            Assert.Null(fs.Error);
            // Tube along +X; reference direction for rotation 0 is +Y; bend toward +Y.
            // Arc end = (0.1 + 0.05, 0.05, 0); then 0.2 along +Y.
            Near(new Vec3(0.150, 0.050, 0), fs.Primitives[0].EndPoint);
            Near(Vec3.UnitY, fs.EndFrame.Tangent);
            Near(new Vec3(0.150, 0.250, 0), fs.EndFrame.Origin);
            double expected = 0.100 + 0.050 * Math.PI / 2 + 0.200;
            Assert.Equal(expected, r.Paths[0].DevelopedLength, 12);
        }

        [Fact]
        public void Single45DegreeBend()
        {
            var b = Base(0.100);
            var bend = Bend(b, 0.080, 45);
            var r = Solve(b, bend);
            double s = Math.Sin(Rad(45)), c = Math.Cos(Rad(45));
            Near(new Vec3(0.100 + 0.080 * s, 0.080 * (1 - c), 0), r.Find(bend.Id).EndFrame.Origin);
            Near(new Vec3(c, s, 0), r.Find(bend.Id).EndFrame.Tangent);
            Assert.Equal(0.100 + 0.080 * Rad(45), r.Paths[0].DevelopedLength, 12);
        }

        [Fact]
        public void PlaneRotationAndReverse()
        {
            var b = Base(0.1);
            // Rotation 90 deg: RefY = T x RefX = X x Y = +Z, so the bend goes toward +Z.
            var r1 = Solve(b, Bend(b, 0.05, 90, rotDeg: 90));
            Near(Vec3.UnitZ, r1.Features[1].EndFrame.Tangent);
            // Reverse: toward -Y.
            var r2 = Solve(b, Bend(b, 0.05, 90, rev: true));
            Near(-Vec3.UnitY, r2.Features[1].EndFrame.Tangent);
        }

        [Fact]
        public void TwoBends_DifferentPlanes_Rotation90()
        {
            var b = Base(0.2);
            var b1 = Bend(b, 0.05, 90, after: 0.1);
            var b2 = Bend(b1, 0.05, 90, rotDeg: 0, after: 0.2);
            var r = Solve(b, b1, b2);
            Assert.All(r.Features, f => Assert.Null(f.Error));
            // After b1 the tube points +Y. T is parallel to global Y, so the reference direction is the projection
            // of global +Z, which is +Z. Rotation 0 therefore bends toward +Z (a different plane from b1).
            Near(Vec3.UnitZ, r.Find(b2.Id).EndFrame.Tangent);
            var t = BendTable.Build(r.Paths[0].Primitives);
            Assert.Equal(2, t.Rows.Count);
            Assert.Equal(0.1, t.Rows[1].Feed, 12);
            // First bend plane normal = X x Y = +Z; second bend: T=+Y, D=+Z, normal = Y x Z = +X. Rotation about +Y from Z to X = +90 deg.
            Assert.Equal(90, Angles.ToDeg(t.Rows[1].Rotation), 9);
            Assert.Equal(0.2 + 0.05 * Math.PI / 2 * 2 + 0.1 + 0.2, t.TotalDevelopedLength, 12);
        }

        [Fact]
        public void TwoBendsSamePlane_CoplanarBack_RotationZero()
        {
            // Make a U: second bend direction must be -X after the first. Solve rotation with the plane helper.
            var b = Base(0.2);
            var b1 = Bend(b, 0.05, 90, after: 0.1);
            var start2 = new TubeFrame(new Vec3(0.25, 0.15, 0), Vec3.UnitY);
            double rot = BendMath.PlaneRotationFor(start2, -Vec3.UnitX);
            var b2 = Bend(b1, 0.05, 90, rotDeg: Angles.ToDeg(rot), after: 0.2);
            var r = Solve(b, b1, b2);
            Near(-Vec3.UnitX, r.Find(b2.Id).EndFrame.Tangent);
            var t = BendTable.Build(r.Paths[0].Primitives);
            Assert.Equal(0, Angles.ToDeg(t.Rows[1].Rotation), 9);
            // S-shape (second bend toward +X) = 180 deg rotation.
            double rotS = BendMath.PlaneRotationFor(start2, Vec3.UnitX);
            var r2 = Solve(b, b1, Bend(b1, 0.05, 90, rotDeg: Angles.ToDeg(rotS)));
            Assert.Equal(180, Math.Abs(Angles.ToDeg(BendTable.Build(r2.Paths[0].Primitives).Rows[1].Rotation)), 9);
        }

        [Fact]
        public void Compound3DTube_EndPointMatchesIndependentStepByStepCalculation()
        {
            var b = Base(0.3, axis: new Vec3(1, 1, 0).Normalized());
            var b1 = Bend(b, 0.06, 60, rotDeg: 30, after: 0.15);
            var b2 = Bend(b1, 0.06, 75, rotDeg: -110, after: 0.05);
            var s3 = Straight(b2, 0.12);
            var b3 = Bend(s3, 0.09, 120, rotDeg: 45, after: 0.2);
            var r = Solve(b, b1, b2, s3, b3);
            Assert.All(r.Features, f => Assert.Null(f.Error));

            // Independent: rotate the tangent with Rodrigues about N = T x D.
            Vec3 p = new Vec3(1, 1, 0).Normalized() * 0.3, t = new Vec3(1, 1, 0).Normalized();
            double total = 0.3;
            foreach (var (R, A, Rot, After) in new[] { (0.06, 60.0, 30.0, 0.15), (0.06, 75.0, -110.0, 0.05 + 0.12), (0.09, 120.0, 45.0, 0.2) })
            {
                var f = new TubeFrame(p, t);
                Vec3 d = f.ReferenceX * Math.Cos(Rad(Rot)) + f.ReferenceY * Math.Sin(Rad(Rot));
                Vec3 c = p + d * R;
                Vec3 n = t.Cross(d);
                Vec3 radial = (p - c).RotateAbout(n, Rad(A));
                p = c + radial; t = t.RotateAbout(n, Rad(A));
                p = p + t * After;
                total += R * Rad(A) + After;
            }
            Near(p, r.Find(b3.Id).EndFrame.Origin, 1e-9);
            Near(t, r.Find(b3.Id).EndFrame.Tangent, 1e-9);
            Assert.Equal(total, r.Paths[0].DevelopedLength, 10);
            Assert.Equal(3, BendTable.Build(r.Paths[0].Primitives).Rows.Count);
        }

        [Fact]
        public void EditingUpstreamBend_MovesDownstreamFeatures()
        {
            var b = Base(0.1);
            var b1 = Bend(b, 0.05, 90, after: 0.1);
            var b2 = Bend(b1, 0.05, 45);
            var before = Solve(b, b1, b2).Find(b2.Id).EndFrame;
            b1.BendAngle = Rad(30);
            var after = Solve(b, b1, b2).Find(b2.Id);
            Assert.Null(after.Error);
            Assert.False(before.Origin.AlmostEquals(after.EndFrame.Origin, 1e-6));
            // Downstream start = upstream end, always.
            Near(Solve(b, b1, b2).Find(b1.Id).EndFrame.Origin, after.StartFrame.Origin);
        }

        [Fact]
        public void CrossSectionPropagatesToAllSegments()
        {
            var b = Base(0.1, od: 0.0381, wall: 0.00165);
            var b1 = Bend(b, 0.1, 90);
            var s = Straight(b1, 0.2);
            var r = Solve(b, b1, s);
            Assert.All(r.Features, f => { Assert.Equal(0.0381, f.Profile.OuterDiameter, 12); Assert.Equal(0.00165, f.Profile.WallThickness, 12); });
            Assert.Equal(0.0381 - 2 * 0.00165, r.Features[2].Profile.InnerDiameter, 12);
        }

        [Fact]
        public void MissingParent_SuppressedParent_Branching_AreReported()
        {
            var b = Base(0.1);
            var orphan = Bend(new TubeFeatureData(), 0.05, 90);
            Assert.Contains("missing", Solve(b, orphan).Find(orphan.Id).Error);

            var b1 = Bend(b, 0.05, 90);
            var r = TubeChainSolver.Solve(new List<ChainInput>
            {
                new ChainInput { Data = b, FeatureName = "Base" },
                new ChainInput { Data = b1, FeatureName = "Bend1", Suppressed = true },
                new ChainInput { Data = Straight(b1, 0.1), FeatureName = "S" },
            });
            Assert.Contains("suppressed", r.Features[2].Error);

            var twin = Bend(b, 0.05, 45);
            Assert.Contains("branch", Solve(b, b1, twin).Find(twin.Id).Error);
        }

        [Fact]
        public void BendFromBaseTubeStartFace_GoesBackwards_AndPathIsContinuous()
        {
            var b = Base(0.2);
            var back = Bend(b, 0.05, 90); back.ParentEnd = ParentEnd.Start;
            var front = Bend(b, 0.05, 90);
            var r = Solve(b, back, front);
            Near(-Vec3.UnitX, r.Find(back.Id).StartFrame.Tangent);
            var path = r.Paths.Single().Primitives;
            for (int i = 1; i < path.Count; i++)
            {
                Near(path[i - 1].EndPoint, path[i].StartPoint, 1e-12);
                Near(path[i - 1].EndTangent, path[i].StartTangent, 1e-12);
            }
            Assert.Equal(0.2 + 2 * 0.05 * Math.PI / 2, r.Paths[0].DevelopedLength, 12);
        }

        [Fact]
        public void RadiusNotGreaterThanHalfOd_IsAnError()
        {
            var b = Base(0.1, od: 0.02);
            Assert.NotNull(Solve(b, Bend(b, 0.010, 90)).Features[1].Error);
            Assert.Null(Solve(b, Bend(b, 0.0101, 90)).Features[1].Error);
        }

        [Fact]
        public void EndpointSolver_RoundTrip()
        {
            var rnd = new Random(12345);
            for (int k = 0; k < 500; k++)
            {
                var start = new TubeFrame(new Vec3(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()),
                    new Vec3(rnd.NextDouble() - .5, rnd.NextDouble() - .5, rnd.NextDouble() - .5));
                double R = 0.02 + rnd.NextDouble() * 0.1;
                Vec3 q = start.Origin + new Vec3(rnd.NextDouble() - .5, rnd.NextDouble() - .5, rnd.NextDouble() - .5) * 2;
                var sol = BendSolver.SolveForEndpoint(start, R, q);
                if (!sol.Success) { Assert.NotNull(sol.Conflict); Assert.NotEmpty(sol.Alternatives); continue; }
                var prims = BendMath.BuildBend(start, R, sol.Angle, sol.PlaneRotation, false, sol.StraightAfter);
                Near(q, prims.Last().EndPoint, 1e-9);
            }
        }

        [Fact]
        public void EndpointSolver_InsideBendCircle_OffersMaxRadius()
        {
            var start = new TubeFrame(Vec3.Zero, Vec3.UnitX);
            var q = new Vec3(0.02, 0.02, 0); // a=0.02, b=0.02 -> Rmax = (a^2+b^2)/(2b) = 0.02
            var sol = BendSolver.SolveForEndpoint(start, 0.05, q);
            Assert.False(sol.Success);
            var alt = sol.Alternatives.First(a => a.Radius.HasValue);
            Assert.Equal(0.02, alt.Radius.Value, 12);
            var exact = BendSolver.SolveForEndpoint(start, alt.Radius.Value, q);
            Assert.True(exact.Success);
            Assert.Equal(0, exact.StraightAfter, 9);
            Assert.Equal(90, Angles.ToDeg(exact.Angle), 6);
        }

        [Fact]
        public void EndpointSolver_OnAxis_SuggestsStraight()
        {
            var sol = BendSolver.SolveForEndpoint(new TubeFrame(Vec3.Zero, Vec3.UnitX), 0.05, new Vec3(0.3, 0, 0));
            Assert.False(sol.Success);
            Assert.Contains("Straight Extension", sol.Alternatives[0].Description);
        }

        [Fact]
        public void DirectionSolver()
        {
            var start = new TubeFrame(Vec3.Zero, Vec3.UnitX);
            var sol = BendSolver.SolveForDirection(start, 0.05, new Vec3(0, 0, -1), 0, 0);
            Assert.True(sol.Success);
            Assert.Equal(90, Angles.ToDeg(sol.Angle), 9);
            var p = BendMath.BuildBend(start, 0.05, sol.Angle, sol.PlaneRotation, false, 0);
            Near(-Vec3.UnitZ, p.Last().EndTangent);
        }

        [Fact]
        public void PlaneRotationForPlane_PutsBendInPlane()
        {
            var start = new TubeFrame(Vec3.Zero, Vec3.UnitX);
            double? rot = BendSolver.PlaneRotationForPlane(start, Vec3.UnitY, 0); // XZ plane
            Assert.True(rot.HasValue);
            var p = BendMath.BuildBend(start, 0.05, Rad(90), rot.Value, false, 0);
            Assert.Equal(0, p.Last().EndPoint.Y, 12);
            Assert.Null(BendSolver.PlaneRotationForPlane(start, Vec3.UnitX, 0)); // axis not in plane
        }

        [Fact]
        public void Codec_RoundTrip_AndUnknownKeysIgnored()
        {
            var d = Bend(Base(0.1), 0.0525, 33.3, rotDeg: -12.5, after: 0.017, rev: true);
            d.Material = "AISI 304; \"special\" = yes";
            var e = TubeFeatureData.Decode(d.Encode() + ";future=1");
            Assert.Equal(d.Encode(), e.Encode());
            Assert.Equal(d.Material, e.Material);
            Assert.Throws<FormatException>(() => TubeFeatureData.Decode("v=99;id=x"));
        }

        [Fact]
        public void Validator_SelfIntersection_Detected()
        {
            // Base along +X (0.2), bend 270 deg R=0.05 toward +Y, then straight 0.3 down across the base leg.
            var b = Base(0.2, od: 0.02);
            var b1 = Bend(b, 0.05, 270, after: 0.3);
            var issues = TubeValidator.Validate(Solve(b, b1), new ManufacturingRules());
            Assert.Contains(issues, i => i.Code == "SELF_INTERSECTION" && i.Severity == IssueSeverity.Error);
            Assert.Contains(issues, i => i.Code == "MAX_ANGLE");
        }

        [Fact]
        public void Validator_TightButValidUBend_NotReported()
        {
            var b = Base(0.2, od: 0.02);
            var b1 = Bend(b, 0.012, 180, after: 0.2); // legs 0.024 apart > OD
            var issues = TubeValidator.Validate(Solve(b, b1), new ManufacturingRules());
            Assert.DoesNotContain(issues, i => i.Code == "SELF_INTERSECTION");
            Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Validator_UsesOnlyDefinedLimits()
        {
            var b = Base(0.02, od: 0.02);
            var b1 = Bend(b, 0.03, 90);
            var b2 = Bend(b1, 0.03, 90, rotDeg: 90);
            var none = TubeValidator.Validate(Solve(b, b1, b2), new ManufacturingRules { MaxBendAngle = null });
            Assert.All(none, i => Assert.Equal("NO_STRAIGHT", i.Code));
            var rules = new ManufacturingRules { MinBendRadius = 0.04, MinClampLength = 0.05 };
            var some = TubeValidator.Validate(Solve(b, b1, b2), rules);
            Assert.Contains(some, i => i.Code == "MIN_RADIUS");
            Assert.Contains(some, i => i.Code == "CLAMP");
        }

        [Fact]
        public void BendTable_Csv_UsesInvariantCultureAndConventions()
        {
            var b = Base(0.1);
            var b1 = Bend(b, 0.05, 90, after: 0.1);
            var t = BendTable.Build(Solve(b, b1).Paths[0].Primitives);
            var o = new BendTableOptions { AngleConvention = AngleConvention.IncludedAngle };
            string lra = t.ToLraCsv(o);
            Assert.Contains("1,100.000,0.000,90.000,50.000", lra);
            string csv = t.ToCsv(new BendTableOptions());
            Assert.Contains("Total developed length,278.540", csv); // 100 + 50*pi/2 (78.5398) + 100
            string xyz = t.ToXyzCsv(new BendTableOptions());
            Assert.Contains("V1,150.000,0.000,0.000,50.000", xyz);
        }

        [Fact]
        public void StraightUpToPointAndPlane()
        {
            var b = Base(0.1);
            var s = Straight(b, 0); s.EndCondition = StraightEndCondition.UpToPoint; s.UpToPoint = new Vec3(0.35, 0.2, 0);
            Assert.Equal(0.35, Solve(b, s).Find(s.Id).EndFrame.Origin.X, 12);
            var s2 = Straight(b, 0); s2.EndCondition = StraightEndCondition.UpToPlane; s2.UpToPoint = new Vec3(0.5, 0, 0); s2.UpToPlaneNormal = new Vec3(1, 1, 0);
            Assert.Equal(0.5, Solve(b, s2).Find(s2.Id).EndFrame.Origin.X, 12);
            s2.UpToPlaneNormal = Vec3.UnitY;
            Assert.NotNull(Solve(b, s2).Find(s2.Id).Error);
        }

        [Fact]
        public void TryMatchEnd_FindsParentByFaceCentre()
        {
            var b = Base(0.1);
            var b1 = Bend(b, 0.05, 90);
            var r = Solve(b, b1);
            Assert.True(TubeChainSolver.TryMatchEnd(r, new Vec3(0.15, 0.05, 0), Vec3.UnitY, 1e-6, out var f, out var end));
            Assert.Equal(b1.Id, f.Data.Id); Assert.Equal(ParentEnd.End, end);
            Assert.True(TubeChainSolver.TryMatchEnd(r, Vec3.Zero, -Vec3.UnitX, 1e-6, out f, out end));
            Assert.Equal(b.Id, f.Data.Id); Assert.Equal(ParentEnd.Start, end);
        }
    }
}
