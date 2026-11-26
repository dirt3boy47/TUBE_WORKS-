using System;
using System.Collections.Generic;
using System.Linq;

namespace TubeWorks.Geometry
{
    /// <summary>One springback calibration measurement: the machine was commanded to <see cref="CommandedDeg"/> and the
    /// tube measured <see cref="AchievedDeg"/> after release.</summary>
    public sealed class SpringbackPoint
    {
        public double CommandedDeg { get; set; }
        public double AchievedDeg { get; set; }
    }

    /// <summary>
    /// Calibration data for one tube / tooling / machine combination. Every value is user data (measured or from the
    /// tooling supplier). Nothing is assumed: a missing value means "no compensation of that kind".
    /// </summary>
    public sealed class CompensationData
    {
        /// <summary>Springback calibration measurements. Empty = no springback compensation.</summary>
        public List<SpringbackPoint> Springback { get; set; } = new List<SpringbackPoint>();
        /// <summary>Material stretch per bend (m) that shortens the required cut length. Null = not applied.</summary>
        public double? ElongationPerBend { get; set; }
        /// <summary>Material stretch per degree of bend (m/°). Null = not applied.</summary>
        public double? ElongationPerDegree { get; set; }
        /// <summary>Neutral axis shift (m): developed bend length uses (CLR + shift) × angle. Null = centreline.</summary>
        public double? NeutralAxisShift { get; set; }
        /// <summary>Tooling calibration: correction added to every commanded bend angle (°), e.g. from a test bend. Null = none.</summary>
        public double? AngleOffsetDeg { get; set; }
        /// <summary>End trimming allowance added before the first and after the last straight (m).</summary>
        public double TrimStart { get; set; }
        public double TrimEnd { get; set; }

        public bool HasSpringback => Springback.Count > 0;
    }

    public sealed class CompensatedRow
    {
        public int Number;
        /// <summary>Feed before this bend (m), including the start trim for the first bend.</summary>
        public double Feed;
        public double Rotation;
        public double Clr;
        /// <summary>Angle the tube must have (design), degrees.</summary>
        public double TargetDeg;
        /// <summary>Angle to command on the machine, degrees.</summary>
        public double CommandDeg;
        /// <summary>Developed bend length used for the cut length (m).</summary>
        public double DevelopedBend;
        public double Elongation;
    }

    public sealed class CompensatedData
    {
        public List<CompensatedRow> Rows { get; } = new List<CompensatedRow>();
        /// <summary>Feed after the last bend, including the end trim (m).</summary>
        public double FinalFeed;
        public double NominalLength;
        /// <summary>Length to cut from stock (m).</summary>
        public double CutLength;
        /// <summary>What was applied and from which data; also what was not applied.</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Applies user calibration data to a bend table.
    /// Model (documented, so that it can be checked against the machine):
    ///   commanded angle = inverse of the measured springback curve at the target angle (+ tooling angle offset);
    ///   developed bend  = (CLR + neutral axis shift) × target angle (rad);
    ///   elongation      = per-bend value + per-degree value × target angle;
    ///   cut length      = trim start + Σ straights + Σ developed bends − Σ elongation + trim end.
    /// Machines differ in how they count feed through the bend die; confirm with a test part before production.
    /// </summary>
    public static class Compensation
    {
        public static CompensatedData Apply(BendTable table, CompensationData c)
        {
            c = c ?? new CompensationData();
            var r = new CompensatedData { NominalLength = table.TotalDevelopedLength };
            double straight = table.TotalStraight;
            double bends = 0, elong = 0;
            foreach (var row in table.Rows)
            {
                double target = Angles.ToDeg(row.Angle);
                double cmd = c.HasSpringback ? CommandFor(target, c.Springback) : target;
                if (c.AngleOffsetDeg.HasValue) cmd += c.AngleOffsetDeg.Value;
                double dev = (row.Radius + (c.NeutralAxisShift ?? 0)) * row.Angle;
                double e = (c.ElongationPerBend ?? 0) + (c.ElongationPerDegree ?? 0) * target;
                bends += dev; elong += e;
                r.Rows.Add(new CompensatedRow
                {
                    Number = row.Number, Feed = row.Feed + (row.Number == 1 ? c.TrimStart : 0), Rotation = row.Rotation, Clr = row.Radius,
                    TargetDeg = target, CommandDeg = cmd, DevelopedBend = dev, Elongation = e,
                });
            }
            r.FinalFeed = table.FinalStraight + c.TrimEnd + (table.Rows.Count == 0 ? c.TrimStart : 0);
            r.CutLength = c.TrimStart + straight + bends - elong + c.TrimEnd;

            r.Notes.Add(c.HasSpringback ? $"Springback: commanded angles from {c.Springback.Count} calibration point(s), interpolated linearly." : "Springback: no calibration data, commanded angle = design angle.");
            r.Notes.Add(c.AngleOffsetDeg.HasValue ? $"Tooling angle offset {c.AngleOffsetDeg.Value:+0.###;-0.###;0}° added to every commanded angle." : "Tooling angle offset: not defined.");
            r.Notes.Add(c.NeutralAxisShift.HasValue ? $"Neutral axis shift {c.NeutralAxisShift.Value * 1000:+0.###;-0.###;0} mm applied to developed bend lengths." : "Neutral axis: centreline (no shift defined).");
            r.Notes.Add(c.ElongationPerBend.HasValue || c.ElongationPerDegree.HasValue ? $"Stretch: {elong * 1000:0.###} mm removed from the cut length." : "Stretch: no data, not applied.");
            r.Notes.Add(c.TrimStart > 0 || c.TrimEnd > 0 ? $"End trim allowance: {c.TrimStart * 1000:0.###} mm start, {c.TrimEnd * 1000:0.###} mm end." : "End trim allowance: none.");
            return r;
        }

        /// <summary>
        /// Commanded angle that gives <paramref name="targetDeg"/> after springback: piecewise-linear interpolation of the
        /// calibration points (achieved → commanded), with linear extrapolation from the nearest segment. With one point the
        /// ratio commanded/achieved is used.
        /// </summary>
        public static double CommandFor(double targetDeg, IReadOnlyList<SpringbackPoint> pts)
        {
            var p = pts.Where(x => x.AchievedDeg > 0 && x.CommandedDeg > 0).OrderBy(x => x.AchievedDeg).ToList();
            if (p.Count == 0) return targetDeg;
            if (p.Count == 1) return targetDeg * p[0].CommandedDeg / p[0].AchievedDeg;
            int i = 0;
            while (i < p.Count - 2 && targetDeg > p[i + 1].AchievedDeg) i++;
            var a = p[i]; var b = p[i + 1];
            if (Math.Abs(b.AchievedDeg - a.AchievedDeg) < 1e-12) return targetDeg * a.CommandedDeg / a.AchievedDeg;
            double t = (targetDeg - a.AchievedDeg) / (b.AchievedDeg - a.AchievedDeg);
            return a.CommandedDeg + t * (b.CommandedDeg - a.CommandedDeg);
        }

        /// <summary>Checks calibration data; returns messages (empty = usable).</summary>
        public static List<string> Check(CompensationData c)
        {
            var e = new List<string>();
            foreach (var p in c.Springback)
            {
                if (!(p.CommandedDeg > 0) || !(p.AchievedDeg > 0)) e.Add("Springback points need commanded and achieved angles greater than 0°.");
                else if (p.AchievedDeg > p.CommandedDeg) e.Add($"Springback point {p.CommandedDeg}°→{p.AchievedDeg}°: the achieved angle is larger than the commanded angle. Check the data.");
            }
            var dup = c.Springback.GroupBy(p => p.AchievedDeg).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) e.Add("Two springback points have the same achieved angle: " + string.Join(", ", dup) + "°.");
            if (c.TrimStart < 0 || c.TrimEnd < 0) e.Add("Trim allowances cannot be negative.");
            return e;
        }
    }
}
