#if !TRIAL
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreditMonitor
{
    // A projection of quota samples, not another history store. Percentage points
    // are never converted to tokens, money, or quota from another provider.
    public sealed class RunwayObservation
    {
        public DateTime AtUtc, ResetAtUtc;
        public double Remaining;
    }

    public sealed class RunwayValidation
    {
        public string Status = "disabled", SelectedModel;
        public int ObservationCount, SegmentCount, EligibleTransitions, Abstentions, ForecastsEvaluated;
        public int ResetBreaks, GapBreaks, BalanceIncreaseBreaks, ObservedExhaustionTransitions;
        public int SelectionSamples, HeldOutSamples;
        public double? MeanAbsoluteErrorPoints, EmpiricalRangeCoverage, MinimumHorizonMinutes, MaximumHorizonMinutes;
        public List<RunwayModelValidation> Models = new List<RunwayModelValidation>();
    }

    public sealed class RunwayModelValidation
    {
        public string Model;
        public double SelectionMeanAbsoluteErrorPoints, HeldOutMeanAbsoluteErrorPoints;
    }

    public sealed class RunwayEstimate
    {
        public string Status = "disabled", Model, Reliability;
        public double? Remaining, RatePerMinute, RateLower, RateUpper;
        public DateTime? ObservedAtUtc, ResetAtUtc, ExhaustionAtUtc, EarliestExhaustionAtUtc, LatestExhaustionAtUtc;
        public TimeSpan? DataAge;
        public bool? ExhaustionBeforeReset, MayExhaustBeforeReset;
        public RunwayValidation Validation = new RunwayValidation();
    }

    public static class QuotaRunway
    {
        public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(15);
        const int MinimumSamples = 4;
        static readonly string[] Models = { "recent", "robust", "hybrid" };

        sealed class Rates { public double Recent, Robust, Low, High; }
        sealed class Outcome { public double[] Errors; public bool Covered; public double Minutes; }

        static List<RunwayObservation> ReadRows(IEnumerable<RunwayObservation> observations, DateTime nowUtc)
        {
            if (nowUtc.Kind != DateTimeKind.Utc || nowUtc == DateTime.MinValue) return null;
            var rows = observations == null ? new List<RunwayObservation>() : observations.Take(20001).ToList();
            if (rows.Count > 20000 || rows.Any(x => !Valid(x, nowUtc))) return null;
            rows = rows.OrderBy(x => x.AtUtc).ToList();
            if (rows.Zip(rows.Skip(1), (a, b) => a.AtUtc == b.AtUtc).Any(x => x)) return null;
            return rows;
        }

        // Historical diagnostics remain available after a reset or while the
        // latest segment is too sparse for a current forecast. No new data store.
        public static RunwayValidation Backtest(IEnumerable<RunwayObservation> observations, DateTime nowUtc, bool enabled = false)
        {
            if (!enabled) return new RunwayValidation();
            var rows = ReadRows(observations, nowUtc);
            return rows == null ? new RunwayValidation { Status = "invalid-data" } : ValidateHistory(rows);
        }

        // Explicit opt-in is required even for this development-only API. No UI,
        // telemetry, persistence, provider calls, alerts, or execution side effects.
        public static RunwayEstimate Estimate(IEnumerable<RunwayObservation> observations, DateTime nowUtc, bool enabled = false)
        {
            var result = new RunwayEstimate();
            if (!enabled) return result;
            result.Status = "insufficient-data";
            result.Reliability = "experimental-uncalibrated";
            var rows = ReadRows(observations, nowUtc);
            if (rows == null) { result.Status = "invalid-data"; return result; }
            result.Validation = ValidateHistory(rows);
            if (rows.Count == 0) return result;
            var last = rows[rows.Count - 1];
            result.Remaining = last.Remaining;
            result.ObservedAtUtc = last.AtUtc;
            result.ResetAtUtc = last.ResetAtUtc;
            result.DataAge = nowUtc - last.AtUtc;
            if (last.ResetAtUtc <= nowUtc) { result.Status = "reset-due"; return result; }
            if (result.DataAge > MaximumAge) { result.Status = "stale-data"; return result; }
            if (last.Remaining == 0)
            {
                result.Status = "exhausted";
                result.ExhaustionAtUtc = last.AtUtc;
                result.ExhaustionBeforeReset = result.MayExhaustBeforeReset = true;
                return result;
            }

            var segments = Segments(rows);
            var current = segments[segments.Count - 1];
            var rates = Fit(current, current.Count);
            if (rates == null) return result;
            int selected = Array.IndexOf(Models, result.Validation.SelectedModel);
            if (selected < 0) selected = 1;
            result.Model = Models[selected];
            result.RatePerMinute = Rate(rates, selected);
            result.RateLower = rates.Low;
            result.RateUpper = rates.High;
            result.Status = result.RatePerMinute == 0 ? "no-observed-consumption" : "experimental";
            result.ExhaustionAtUtc = Exhaustion(last, result.RatePerMinute.Value);
            result.EarliestExhaustionAtUtc = Exhaustion(last, rates.High);
            result.LatestExhaustionAtUtc = Exhaustion(last, rates.Low);
            result.ExhaustionBeforeReset = result.ExhaustionAtUtc.HasValue;
            result.MayExhaustBeforeReset = result.EarliestExhaustionAtUtc.HasValue;
            return result;
        }

        static RunwayValidation ValidateHistory(List<RunwayObservation> rows)
        {
            var result = new RunwayValidation { Status = "insufficient-validation", SelectedModel = "robust", ObservationCount = rows.Count };
            var segments = Segments(rows);
            result.SegmentCount = segments.Count;
            result.EligibleTransitions = rows.Count - segments.Count;
            result.ObservedExhaustionTransitions = segments.Sum(s => Enumerable.Range(1, s.Count - 1).Count(i => s[i - 1].Remaining > 0 && s[i].Remaining == 0));
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].ResetAtUtc != rows[i - 1].ResetAtUtc) result.ResetBreaks++;
                else if (rows[i].AtUtc - rows[i - 1].AtUtc > MaximumGap) result.GapBreaks++;
                else if (rows[i].Remaining > rows[i - 1].Remaining) result.BalanceIncreaseBreaks++;
            }
            var outcomes = new List<Outcome>();
            foreach (var segment in segments)
            {
                for (int i = MinimumSamples; i < segment.Count; i++)
                {
                    var fit = Fit(segment, i);
                    if (fit == null) continue;
                    var before = segment[i - 1];
                    var after = segment[i];
                    double minutes = (after.AtUtc - before.AtUtc).TotalMinutes;
                    double low = Math.Max(0, before.Remaining - fit.High * minutes);
                    double high = Math.Max(0, before.Remaining - fit.Low * minutes);
                    outcomes.Add(new Outcome {
                        Errors = Enumerable.Range(0, Models.Length).Select(m => Math.Abs(Math.Max(0, before.Remaining - Rate(fit, m) * minutes) - after.Remaining)).ToArray(),
                        Covered = after.Remaining >= low - 1e-9 && after.Remaining <= high + 1e-9,
                        Minutes = minutes
                    });
                }
            }

            // Select on the chronological first 2/3 only. The final 1/3 is held
            // out of selection. These diagnostics are next-observation errors,
            // not validation of exhaustion times or a calibrated confidence band.
            result.ForecastsEvaluated = outcomes.Count;
            result.Abstentions = result.EligibleTransitions - outcomes.Count;
            if (outcomes.Count > 0)
            {
                result.MinimumHorizonMinutes = outcomes.Min(x => x.Minutes);
                result.MaximumHorizonMinutes = outcomes.Max(x => x.Minutes);
            }
            if (outcomes.Count >= 12)
            {
                int split = outcomes.Count * 2 / 3;
                int selected = Enumerable.Range(0, Models.Length).OrderBy(m => outcomes.Take(split).Average(x => x.Errors[m])).First();
                var heldOut = outcomes.Skip(split).ToList();
                result.Status = "experimental-next-observation-only";
                result.SelectedModel = Models[selected];
                result.SelectionSamples = split;
                result.HeldOutSamples = heldOut.Count;
                result.MeanAbsoluteErrorPoints = heldOut.Average(x => x.Errors[selected]);
                result.EmpiricalRangeCoverage = heldOut.Count(x => x.Covered) / (double)heldOut.Count;
                for (int model = 0; model < Models.Length; model++)
                    result.Models.Add(new RunwayModelValidation { Model = Models[model],
                        SelectionMeanAbsoluteErrorPoints = outcomes.Take(split).Average(x => x.Errors[model]),
                        HeldOutMeanAbsoluteErrorPoints = heldOut.Average(x => x.Errors[model]) });
            }
            return result;
        }

        static bool Valid(RunwayObservation x, DateTime nowUtc)
        {
            return x != null && x.AtUtc.Kind == DateTimeKind.Utc && x.ResetAtUtc.Kind == DateTimeKind.Utc &&
                x.AtUtc > DateTime.MinValue && x.AtUtc <= nowUtc && x.ResetAtUtc > x.AtUtc && !Double.IsNaN(x.Remaining) && !Double.IsInfinity(x.Remaining) &&
                x.Remaining >= 0 && x.Remaining <= 100;
        }

        static List<List<RunwayObservation>> Segments(List<RunwayObservation> rows)
        {
            var segments = new List<List<RunwayObservation>>();
            foreach (var row in rows)
            {
                var previous = segments.Count == 0 ? null : segments[segments.Count - 1].Last();
                if (previous == null || previous.ResetAtUtc != row.ResetAtUtc || row.AtUtc - previous.AtUtc > MaximumGap || row.Remaining > previous.Remaining)
                    segments.Add(new List<RunwayObservation>());
                segments[segments.Count - 1].Add(row);
            }
            return segments;
        }

        static Rates Fit(List<RunwayObservation> segment, int count)
        {
            var last = segment[count - 1];
            // Bounded pairwise work even if a caller supplies dense observations.
            var rows = new List<RunwayObservation>();
            for (int i = count - 1; i >= 0 && rows.Count < 64 && last.AtUtc - segment[i].AtUtc <= TimeSpan.FromHours(1); i--) rows.Add(segment[i]);
            rows.Reverse();
            if (rows.Count < MinimumSamples || last.AtUtc - rows[0].AtUtc < TimeSpan.FromMinutes(15)) return null;
            var slopes = new List<double>();
            for (int i = 0; i < rows.Count - 1; i++)
                for (int j = i + 1; j < rows.Count; j++)
                {
                    double minutes = (rows[j].AtUtc - rows[i].AtUtc).TotalMinutes;
                    if (minutes >= 5) slopes.Add((rows[i].Remaining - rows[j].Remaining) / minutes);
                }
            if (slopes.Count == 0) return null;
            slopes.Sort();
            int middle = slopes.Count / 2;
            var recent = rows.First(x => last.AtUtc - x.AtUtc <= TimeSpan.FromMinutes(15));
            double recentRate = (recent.Remaining - last.Remaining) / (last.AtUtc - recent.AtUtc).TotalMinutes;
            // Same endpoint averaging principle as BurnRate.Observe; the longer
            // median pairwise slope tolerates rounded/stepwise quota updates.
            return new Rates {
                Recent = recentRate,
                Robust = slopes.Count % 2 == 0 ? (slopes[middle - 1] + slopes[middle]) / 2 : slopes[middle],
                Low = Math.Min(slopes[0], recentRate), High = Math.Max(slopes[slopes.Count - 1], recentRate)
            };
        }

        static double Rate(Rates rates, int model)
        {
            return model == 0 ? rates.Recent : model == 1 ? rates.Robust : (rates.Recent + rates.Robust) / 2;
        }

        static DateTime? Exhaustion(RunwayObservation last, double rate)
        {
            if (rate <= 0) return null;
            double minutes = last.Remaining / rate;
            // Reset censors the forecast. Never imply that this balance carries
            // beyond its window, or overflow DateTime on an almost zero rate.
            if (minutes >= (last.ResetAtUtc - last.AtUtc).TotalMinutes) return null;
            return last.AtUtc.AddMinutes(minutes);
        }
    }
}
#endif
