#if !TRIAL
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreditMonitor.Insights
{
    public enum TaskQuotaOrigin { Unknown, Observed, Synthetic }
    public enum TaskQuotaAttribution { Unknown, Concurrent, ReportedExclusive }

    public sealed class TaskQuotaFeatures
    {
        public string TaskId, Model, Effort, Category, Size, QuotaBasis;
        public QuotaPlanScope Scope;
        public TaskQuotaOrigin Origin;
    }

    public sealed class TaskQuotaObservation
    {
        public TaskQuotaFeatures Features;
        public QuotaPlanReading Before, After;
        public DateTime StartedAtUtc, FinishedAtUtc;
        public bool Complete;
        public TaskQuotaAttribution Attribution;
    }

    public sealed class TaskQuotaPrediction
    {
        public string Status = "disabled";
        public string Basis = "shadow-only-observable-quota-change; attribution-reported-not-proven; range-not-confidence";
        public TaskQuotaFeatures Features;
        public DateTime PredictedAtUtc;
        public int TrainingSamples, ExcludedRecords;
        public double? PredictedPoints, EmpiricalLowerPoints, EmpiricalUpperPoints;
    }

    public sealed class TaskQuotaOutcome
    {
        public string Status;
        public TaskQuotaOrigin Origin;
        public double? ObservedPoints, AbsoluteErrorPoints;
        public bool? WithinEmpiricalRange;
    }

    public sealed class TaskQuotaCalibration
    {
        public string Status = "disabled";
        public TaskQuotaOrigin Origin;
        public int Records, Scored, Abstained;
        public double? MeanAbsoluteErrorPoints, EmpiricalRangeCoverage;
    }

    // In-memory records from separately authorized observations. No automatic
    // collection, conversation parsing, telemetry, persistence or token conversion.
    public static class TaskQuotaEstimator
    {
        public const int MinimumTrainingSamples = 12;
        const int MaximumRecords = 2000, MaximumTraining = 128;

        static bool FeaturesValid(TaskQuotaFeatures f)
        {
            return f != null && QuotaBudget.Text(f.TaskId) && QuotaBudget.Text(f.Model) && QuotaBudget.Text(f.Effort) && QuotaBudget.Text(f.Category) && QuotaBudget.Text(f.Size) && QuotaBudget.Text(f.QuotaBasis) && QuotaBudget.Scope(f.Scope) && (f.Origin == TaskQuotaOrigin.Observed || f.Origin == TaskQuotaOrigin.Synthetic);
        }
        static bool Comparable(TaskQuotaFeatures a, TaskQuotaFeatures b)
        {
            return FeaturesValid(a) && FeaturesValid(b) && a.Scope.Provider == b.Scope.Provider && a.Scope.Account == b.Scope.Account && a.Scope.Window == b.Scope.Window && a.Model == b.Model && a.Effort == b.Effort && a.Category == b.Category && a.Size == b.Size && a.QuotaBasis == b.QuotaBasis && a.Origin == b.Origin;
        }
        static TaskQuotaFeatures Copy(TaskQuotaFeatures f)
        {
            return new TaskQuotaFeatures { TaskId = f.TaskId, Model = f.Model, Effort = f.Effort, Category = f.Category, Size = f.Size, QuotaBasis = f.QuotaBasis, Origin = f.Origin,
                Scope = new QuotaPlanScope { Provider = f.Scope.Provider, Account = f.Scope.Account, Window = f.Scope.Window, ResetAtUtc = f.Scope.ResetAtUtc } };
        }
        static string WindowKey(TaskQuotaFeatures f)
        {
            return String.Join("\n", new[] { f.Scope.Provider, f.Scope.Account, f.Scope.Window, f.Origin.ToString() });
        }
        static string TaskKey(TaskQuotaFeatures f) { return WindowKey(f) + "\n" + f.TaskId; }
        static HashSet<string> OverlappingTasks(IEnumerable<TaskQuotaObservation> source)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in source.Where(x => x != null && FeaturesValid(x.Features) && x.StartedAtUtc.Kind == DateTimeKind.Utc && x.FinishedAtUtc.Kind == DateTimeKind.Utc && x.FinishedAtUtc > x.StartedAtUtc).GroupBy(x => WindowKey(x.Features)))
            {
                TaskQuotaObservation furthest = null;
                foreach (var row in group.OrderBy(x => x.StartedAtUtc))
                {
                    if (furthest != null && row.StartedAtUtc < furthest.FinishedAtUtc && row.Features.TaskId != furthest.Features.TaskId) { result.Add(TaskKey(row.Features)); result.Add(TaskKey(furthest.Features)); }
                    if (furthest == null || row.FinishedAtUtc > furthest.FinishedAtUtc) furthest = row;
                }
            }
            return result;
        }
        static string Quality(TaskQuotaObservation row, DateTime nowUtc)
        {
            if (row == null || !FeaturesValid(row.Features)) return "invalid-features";
            if (!row.Complete) return "incomplete-observation";
            if (row.Attribution != TaskQuotaAttribution.ReportedExclusive) return row.Attribution == TaskQuotaAttribution.Concurrent ? "concurrent-attribution" : "unknown-attribution";
            if (row.StartedAtUtc.Kind != DateTimeKind.Utc || row.FinishedAtUtc.Kind != DateTimeKind.Utc || row.StartedAtUtc == DateTime.MinValue || row.FinishedAtUtc <= row.StartedAtUtc) return "invalid-timestamps";
            if (row.Before == null || row.After == null || !QuotaBudget.Scope(row.Before.Scope) || !QuotaBudget.Scope(row.After.Scope) || !QuotaBudget.Same(row.Features.Scope, row.Before.Scope) || !QuotaBudget.Same(row.Before.Scope, row.After.Scope)) return "reset-or-scope-changed";
            var before = row.Before; var after = row.After;
            if (before.Connected != true || before.Authenticated != true || after.Connected != true || after.Authenticated != true) return "connection-unknown";
            if (!before.RemainingPoints.HasValue || !after.RemainingPoints.HasValue || !QuotaBudget.Points(before.RemainingPoints.Value) || !QuotaBudget.Points(after.RemainingPoints.Value)) return "quota-unknown";
            if (before.ObservedAtUtc.Kind != DateTimeKind.Utc || after.ObservedAtUtc.Kind != DateTimeKind.Utc || before.ObservedAtUtc == DateTime.MinValue || before.ObservedAtUtc > row.StartedAtUtc || after.ObservedAtUtc < row.FinishedAtUtc || row.StartedAtUtc - before.ObservedAtUtc > QuotaBudget.MaximumReadingAge || after.ObservedAtUtc - row.FinishedAtUtc > QuotaBudget.MaximumReadingAge) return "observation-timing-incomplete";
            if (after.ObservedAtUtc > nowUtc) return "not-yet-observed";
            if (after.ObservedAtUtc >= before.Scope.ResetAtUtc) return "reset-boundary";
            if (after.RemainingPoints > before.RemainingPoints) return "balance-increased";
            return null;
        }

        public static TaskQuotaPrediction Predict(TaskQuotaFeatures features, IEnumerable<TaskQuotaObservation> observations, DateTime atUtc, bool experimental = false)
        {
            var result = new TaskQuotaPrediction();
            if (!experimental) return result;
            result.Status = "unsupported-or-unknown";
            if (!FeaturesValid(features) || atUtc.Kind != DateTimeKind.Utc || atUtc == DateTime.MinValue || features.Scope.ResetAtUtc <= atUtc) return result;
            result.Features = Copy(features); result.PredictedAtUtc = atUtc;
            var source = observations == null ? new List<TaskQuotaObservation>() : observations.Take(MaximumRecords + 1).ToList();
            if (source.Count > MaximumRecords) { result.Status = "history-limit"; return result; }
            // Cut at the prediction time BEFORE duplicate analysis. Future rows
            // cannot invalidate or improve the historical training set.
            var past = source.Where(x => x != null && x.After != null && x.After.ObservedAtUtc.Kind == DateTimeKind.Utc && x.After.ObservedAtUtc <= atUtc).ToList();
            var overlapping = OverlappingTasks(past);
            var candidates = past.Where(x => Comparable(features, x.Features) && x.Features.TaskId != features.TaskId).ToList();
            var duplicateIds = new HashSet<string>(candidates.GroupBy(x => x.Features.TaskId).Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.Ordinal);
            var valid = candidates.Where(x => !duplicateIds.Contains(x.Features.TaskId) && !overlapping.Contains(TaskKey(x.Features)) && Quality(x, atUtc) == null).OrderByDescending(x => x.After.ObservedAtUtc).Take(MaximumTraining).ToList();
            result.TrainingSamples = valid.Count;
            result.ExcludedRecords = candidates.Count(x => duplicateIds.Contains(x.Features.TaskId) || overlapping.Contains(TaskKey(x.Features)) || Quality(x, atUtc) != null);
            result.Status = "insufficient-attributable-data";
            if (valid.Count < MinimumTrainingSamples) return result;
            var deltas = valid.Select(x => x.Before.RemainingPoints.Value - x.After.RemainingPoints.Value).OrderBy(x => x).ToList();
            int middle = deltas.Count / 2;
            result.Status = features.Origin == TaskQuotaOrigin.Synthetic ? "synthetic-shadow-only" : "experimental-shadow-only";
            result.PredictedPoints = deltas.Count % 2 == 0 ? (deltas[middle - 1] + deltas[middle]) / 2 : deltas[middle];
            result.EmpiricalLowerPoints = deltas[0]; result.EmpiricalUpperPoints = deltas[deltas.Count - 1];
            return result;
        }

        public static TaskQuotaOutcome Observe(TaskQuotaPrediction prediction, TaskQuotaObservation observation, DateTime nowUtc)
        {
            var result = new TaskQuotaOutcome { Status = "no-prediction", Origin = observation == null || observation.Features == null ? TaskQuotaOrigin.Unknown : observation.Features.Origin };
            if (prediction == null || !prediction.PredictedPoints.HasValue || !QuotaBudget.Points(prediction.PredictedPoints.Value) || !prediction.EmpiricalLowerPoints.HasValue || !prediction.EmpiricalUpperPoints.HasValue || !QuotaBudget.Points(prediction.EmpiricalLowerPoints.Value) || !QuotaBudget.Points(prediction.EmpiricalUpperPoints.Value) || prediction.EmpiricalLowerPoints > prediction.PredictedPoints || prediction.EmpiricalUpperPoints < prediction.PredictedPoints || !FeaturesValid(prediction.Features) || prediction.Status != (prediction.Features.Origin == TaskQuotaOrigin.Synthetic ? "synthetic-shadow-only" : "experimental-shadow-only")) return result;
            if (nowUtc.Kind != DateTimeKind.Utc || nowUtc == DateTime.MinValue) { result.Status = "invalid-timestamps"; return result; }
            string quality = Quality(observation, nowUtc);
            if (quality != null) { result.Status = quality; return result; }
            if (!Comparable(prediction.Features, observation.Features) || prediction.Features.TaskId != observation.Features.TaskId || !QuotaBudget.Same(prediction.Features.Scope, observation.Features.Scope)) { result.Status = "identity-mismatch"; return result; }
            if (prediction.PredictedAtUtc.Kind != DateTimeKind.Utc || prediction.PredictedAtUtc == DateTime.MinValue || prediction.PredictedAtUtc > observation.StartedAtUtc) { result.Status = "prediction-after-task-start"; return result; }
            result.Status = "observable-outcome-recorded";
            result.ObservedPoints = observation.Before.RemainingPoints.Value - observation.After.RemainingPoints.Value;
            result.AbsoluteErrorPoints = Math.Abs(result.ObservedPoints.Value - prediction.PredictedPoints.Value);
            result.WithinEmpiricalRange = result.ObservedPoints >= prediction.EmpiricalLowerPoints && result.ObservedPoints <= prediction.EmpiricalUpperPoints;
            return result;
        }

        public static TaskQuotaCalibration Backtest(IEnumerable<TaskQuotaObservation> observations, DateTime nowUtc, TaskQuotaOrigin origin = TaskQuotaOrigin.Observed, bool experimental = false)
        {
            var result = new TaskQuotaCalibration { Origin = origin };
            if (!experimental) return result;
            result.Status = "unsupported-or-unknown";
            if (nowUtc.Kind != DateTimeKind.Utc || nowUtc == DateTime.MinValue || (origin != TaskQuotaOrigin.Observed && origin != TaskQuotaOrigin.Synthetic)) return result;
            var source = observations == null ? new List<TaskQuotaObservation>() : observations.Take(MaximumRecords + 1).ToList();
            if (source.Count > MaximumRecords) { result.Status = "history-limit"; return result; }
            var cohort = source.Where(x => x != null && x.Features != null && x.Features.Origin == origin).OrderBy(x => x.StartedAtUtc).ToList();
            var overlapping = OverlappingTasks(cohort.Where(x => x.After != null && x.After.ObservedAtUtc.Kind == DateTimeKind.Utc && x.After.ObservedAtUtc <= nowUtc));
            var duplicateKeys = new HashSet<string>(cohort.Where(x => FeaturesValid(x.Features)).GroupBy(x => TaskKey(x.Features)).Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.Ordinal);
            var scores = new List<TaskQuotaOutcome>();
            result.Records = cohort.Count;
            foreach (var row in cohort)
            {
                if (Quality(row, nowUtc) != null || duplicateKeys.Contains(TaskKey(row.Features)) || overlapping.Contains(TaskKey(row.Features))) { result.Abstained++; continue; }
                var prediction = Predict(row.Features, source, row.StartedAtUtc, true);
                var outcome = Observe(prediction, row, nowUtc);
                if (outcome.AbsoluteErrorPoints.HasValue) scores.Add(outcome); else result.Abstained++;
            }
            result.Scored = scores.Count;
            result.Status = scores.Count == 0 ? "insufficient-attributable-data" : origin == TaskQuotaOrigin.Synthetic ? "synthetic-shadow-only" : "experimental-shadow-only";
            if (scores.Count > 0)
            {
                result.MeanAbsoluteErrorPoints = scores.Average(x => x.AbsoluteErrorPoints.Value);
                result.EmpiricalRangeCoverage = scores.Count(x => x.WithinEmpiricalRange == true) / (double)scores.Count;
            }
            return result;
        }
    }
}
#endif
