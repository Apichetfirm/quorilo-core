#if !TRIAL
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreditMonitor.Insights
{
    public sealed class QuotaPlanScope
    {
        public string Provider, Account, Window;
        public DateTime ResetAtUtc;
    }

    public sealed class QuotaPlanReading
    {
        public QuotaPlanScope Scope;
        public double? RemainingPoints;
        public DateTime ObservedAtUtc;
        public bool? Connected, Authenticated;
    }

    public sealed class QuotaAllocation
    {
        public string CategoryId, Name;
        public double Points;
    }

    public sealed class QuotaBudgetPlan
    {
        public QuotaPlanScope Scope;
        public List<QuotaAllocation> Allocations = new List<QuotaAllocation>();
    }

    public sealed class QuotaBudgetAssessment
    {
        public string Status = "disabled";
        public string Basis = "user-defined-allocations-not-measured-project-consumption";
        public double AllocatedPoints;
        public double? MeasuredRemainingPoints, UnallocatedPoints, ShortfallPoints;
        public TimeSpan? DataAge;
        public List<QuotaAllocation> Allocations = new List<QuotaAllocation>();

        public double? AvailableForCategory(string categoryId)
        {
            if (Status != "ready" || !MeasuredRemainingPoints.HasValue) return null;
            double otherReservations = Allocations.Where(a => a.CategoryId != categoryId).Sum(a => a.Points);
            return Math.Max(0, MeasuredRemainingPoints.Value - otherReservations);
        }
    }

    // Pure planning calculations. No store, collection, licensing, provider call,
    // notification, dispatch or automatic allocation/consumption attribution.
    public static class QuotaBudget
    {
        public static readonly TimeSpan MaximumReadingAge = TimeSpan.FromSeconds(90);
        internal static bool Text(string value) { return !String.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(Char.IsControl); }
        internal static bool Scope(QuotaPlanScope scope) { return scope != null && Text(scope.Provider) && Text(scope.Account) && Text(scope.Window) && scope.ResetAtUtc.Kind == DateTimeKind.Utc && scope.ResetAtUtc > DateTime.MinValue; }
        internal static bool Same(QuotaPlanScope a, QuotaPlanScope b) { return a.Provider == b.Provider && a.Account == b.Account && a.Window == b.Window && a.ResetAtUtc == b.ResetAtUtc; }
        internal static bool Points(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 100; }

        public static QuotaBudgetAssessment Assess(QuotaBudgetPlan plan, QuotaPlanReading reading, DateTime nowUtc, bool experimental = false)
        {
            var result = new QuotaBudgetAssessment();
            if (!experimental) return result;
            result.Status = "invalid-plan";
            if (nowUtc.Kind != DateTimeKind.Utc || nowUtc == DateTime.MinValue || plan == null || !Scope(plan.Scope) || plan.Allocations == null || plan.Allocations.Count > 100) return result;
            if (plan.Allocations.Any(a => a == null || !Text(a.CategoryId) || !Text(a.Name) || a.Name.Length > 80 || !Points(a.Points)) || plan.Allocations.Select(a => a.CategoryId).Distinct(StringComparer.Ordinal).Count() != plan.Allocations.Count) return result;
            double allocated = plan.Allocations.Sum(a => a.Points);
            if (allocated > 100 + 1e-9) return result;
            result.AllocatedPoints = Math.Min(100, allocated);
            result.Allocations = plan.Allocations.Select(a => new QuotaAllocation { CategoryId = a.CategoryId, Name = a.Name, Points = a.Points }).ToList();
            if (plan.Scope.ResetAtUtc <= nowUtc) { result.Status = "plan-reset-due"; return result; }
            if (reading == null || !Scope(reading.Scope)) { result.Status = "unknown"; return result; }
            if (!Same(plan.Scope, reading.Scope)) { result.Status = "scope-mismatch"; return result; }
            if (reading.Connected == false || reading.Authenticated == false) { result.Status = "disconnected"; return result; }
            if (reading.Connected != true || reading.Authenticated != true || !reading.RemainingPoints.HasValue) { result.Status = "unknown"; return result; }
            if (!Points(reading.RemainingPoints.Value) || reading.ObservedAtUtc.Kind != DateTimeKind.Utc || reading.ObservedAtUtc == DateTime.MinValue || reading.ObservedAtUtc > nowUtc) { result.Status = "invalid-reading"; return result; }
            result.DataAge = nowUtc - reading.ObservedAtUtc;
            if (result.DataAge > MaximumReadingAge) { result.Status = "stale"; return result; }
            result.MeasuredRemainingPoints = reading.RemainingPoints;
            result.UnallocatedPoints = Math.Max(0, reading.RemainingPoints.Value - result.AllocatedPoints);
            result.ShortfallPoints = Math.Max(0, result.AllocatedPoints - reading.RemainingPoints.Value);
            result.Status = reading.RemainingPoints == 0 ? "exhausted" : result.ShortfallPoints > 1e-9 ? "overallocated" : "ready";
            if (result.ShortfallPoints <= 1e-9) result.ShortfallPoints = 0;
            return result;
        }
    }

    public sealed class AiPlanningRequest
    {
        public string CategoryId, RequiredCapability;
        public DateTime DeadlineUtc;
        public List<string> PreferredProviders = new List<string>();
    }

    public sealed class AiPlanningOption
    {
        public string Id, Provider, Account, Model;
        // Null means unknown, not that every capability/window is supported.
        public List<string> SupportedCapabilities, RequiredWindows;
        public List<QuotaPlanReading> Readings = new List<QuotaPlanReading>();
        public List<QuotaBudgetPlan> Budgets = new List<QuotaBudgetPlan>();
    }

    public sealed class AiPlanningDecision
    {
        public string OptionId, Provider, Account, Model, Status, Reason;
        public DateTime? RecheckAtUtc;
        public List<QuotaBudgetAssessment> Windows = new List<QuotaBudgetAssessment>();
    }

    public sealed class AiPlanningResult
    {
        public string Status = "disabled";
        public string Basis = "explicit-capabilities-current-quota-user-preferences; task-cost-and-model-quality-not-estimated";
        public AiPlanningDecision Suggested;
        public DateTime? SuggestedAtUtc, RecheckAtUtc;
        public List<AiPlanningDecision> Alternatives = new List<AiPlanningDecision>();
        public List<AiPlanningDecision> Decisions = new List<AiPlanningDecision>();
    }

    public static class SmartAiPlanner
    {
        public static AiPlanningResult Plan(AiPlanningRequest request, IEnumerable<AiPlanningOption> options, DateTime nowUtc, bool experimental = false)
        {
            var result = new AiPlanningResult();
            if (!experimental) return result;
            result.Status = "invalid-request";
            if (request == null || nowUtc.Kind != DateTimeKind.Utc || nowUtc == DateTime.MinValue || request.DeadlineUtc.Kind != DateTimeKind.Utc || request.DeadlineUtc <= nowUtc || !QuotaBudget.Text(request.CategoryId) || !QuotaBudget.Text(request.RequiredCapability) || request.PreferredProviders == null || request.PreferredProviders.Count > 100 || request.PreferredProviders.Any(x => !QuotaBudget.Text(x))) return result;
            var source = options == null ? new List<AiPlanningOption>() : options.Take(101).ToList();
            if (source.Count > 100 || source.Any(x => x == null || !QuotaBudget.Text(x.Id)) || source.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != source.Count) return result;
            foreach (var option in source) result.Decisions.Add(Inspect(request, option, nowUtc));
            // Respect user preference and supplied option order. Percentages from
            // different subscriptions are NOT comparable task capacity or quality.
            var ready = result.Decisions.Where(x => x.Status == "ready").OrderBy(x => {
                int index = request.PreferredProviders.IndexOf(x.Provider); return index < 0 ? Int32.MaxValue : index;
            }).ToList();
            if (ready.Count > 0)
            {
                result.Status = "suggestion"; result.Suggested = ready[0]; result.SuggestedAtUtc = nowUtc;
                result.Alternatives = ready.Skip(1).ToList(); return result;
            }
            var rechecks = result.Decisions.Where(x => x.RecheckAtUtc.HasValue && x.RecheckAtUtc < request.DeadlineUtc).ToList();
            result.Status = rechecks.Count > 0 ? "recheck-after-reported-reset" : "no-supported-option";
            if (rechecks.Count > 0) result.RecheckAtUtc = rechecks.Min(x => x.RecheckAtUtc);
            return result;
        }

        static AiPlanningDecision Inspect(AiPlanningRequest request, AiPlanningOption option, DateTime nowUtc)
        {
            var result = new AiPlanningDecision { OptionId = option.Id, Provider = option.Provider, Account = option.Account, Model = option.Model, Status = "unknown", Reason = "capability-or-required-windows-unknown" };
            if (!QuotaBudget.Text(option.Provider) || !QuotaBudget.Text(option.Account) || !QuotaBudget.Text(option.Model)) return result;
            if (option.SupportedCapabilities == null || option.RequiredWindows == null || option.RequiredWindows.Count == 0) return result;
            if (option.SupportedCapabilities.Count > 64 || option.SupportedCapabilities.Any(x => !QuotaBudget.Text(x)) || option.RequiredWindows.Count > 16 || option.RequiredWindows.Any(x => !QuotaBudget.Text(x)) || option.RequiredWindows.Distinct(StringComparer.Ordinal).Count() != option.RequiredWindows.Count || option.Readings == null || option.Readings.Count > 16 || option.Budgets == null || option.Budgets.Count > 16) { result.Reason = "invalid-option"; return result; }
            if (!option.SupportedCapabilities.Contains(request.RequiredCapability)) { result.Status = "unsupported"; result.Reason = "required-capability-not-supported"; return result; }
            if (option.Readings.Any(x => x == null || !QuotaBudget.Scope(x.Scope) || x.Scope.Provider != option.Provider || x.Scope.Account != option.Account) || option.Readings.Select(x => x.Scope.Window).Distinct(StringComparer.Ordinal).Count() != option.Readings.Count || option.Budgets.Any(x => x == null || !QuotaBudget.Scope(x.Scope) || x.Scope.Provider != option.Provider || x.Scope.Account != option.Account) || option.Budgets.Select(x => x.Scope.Window).Distinct(StringComparer.Ordinal).Count() != option.Budgets.Count) { result.Reason = "scope-mismatch-or-duplicate-window"; return result; }
            var exhausted = new List<DateTime>();
            string hold = null;
            foreach (string window in option.RequiredWindows)
            {
                var reading = option.Readings.FirstOrDefault(x => x.Scope.Window == window);
                if (reading == null) { hold = hold ?? "required-window-missing"; continue; }
                var budget = option.Budgets.FirstOrDefault(x => x.Scope.Window == window) ?? new QuotaBudgetPlan { Scope = reading.Scope };
                var assessment = QuotaBudget.Assess(budget, reading, nowUtc, true);
                result.Windows.Add(assessment);
                if (assessment.Status == "exhausted") exhausted.Add(reading.Scope.ResetAtUtc);
                else if (assessment.Status != "ready") hold = hold ?? assessment.Status;
                else if (assessment.AvailableForCategory(request.CategoryId) <= 0) hold = hold ?? "quota-reserved-for-other-work";
            }
            if (hold != null) { result.Status = "hold"; result.Reason = hold; return result; }
            if (exhausted.Count > 0)
            {
                result.Status = "waiting-reset"; result.Reason = "recheck-after-reported-reset-not-guaranteed-availability";
                result.RecheckAtUtc = exhausted.Max(); return result;
            }
            result.Status = "ready"; result.Reason = "supported-capability-fresh-quota-and-category-headroom; task-cost-unknown";
            return result;
        }
    }
}
#endif
