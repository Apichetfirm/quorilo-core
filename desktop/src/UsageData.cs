using System;
using System.Globalization;
namespace CreditMonitor
{
    public sealed class Metric
    {
        public double Remaining;
        public DateTime? ResetAt;
        public string Model;
        public string Display { get { return Remaining > 0 && Remaining < 1 ? "<1%" : (Remaining < 10 ? Remaining.ToString("0.#", CultureInfo.InvariantCulture) : Math.Floor(Remaining).ToString(CultureInfo.InvariantCulture)) + "%"; } }
    }
    public sealed class UsageData
    {
        public Metric Current, Weekly;
        public DateTime ReadAt = DateTime.Now;
        public string Source = Product.Source, SourceKey = "unknown", Capability = "Unknown";
        public string CurrentLabel = "5h", WeeklyLabel = "Weekly";
        public bool HasQuota { get { return Current != null || Weekly != null; } }
        public bool ResetPending;
    }
    public static class QuotaFormatting
    {
        public static double? ElapsedRatio(Metric metric, TimeSpan windowDuration, DateTime utcNow)
        {
            if (metric == null || !metric.ResetAt.HasValue || windowDuration.TotalSeconds <= 0) return null;
            var remaining = metric.ResetAt.Value.ToUniversalTime() - utcNow.ToUniversalTime();
            if (remaining.TotalSeconds <= 0 || remaining > windowDuration) return null;
            return 1.0 - (remaining.TotalSeconds / windowDuration.TotalSeconds);
        }

        public static string ResetLabel(Metric metric)
        {
            if (metric == null || !metric.ResetAt.HasValue) return "resets in —";
            var r = metric.ResetAt.Value.ToUniversalTime() - DateTime.UtcNow;
            if (r.TotalSeconds <= 0) return "รอค่ารอบใหม่";
            if (r.TotalDays >= 1) return "resets in " + (int)r.TotalDays + "d " + r.Hours + "h";
            if (r.TotalHours >= 1) return "resets in " + (int)r.TotalHours + "h " + r.Minutes + "m";
            return "resets in " + Math.Max(1, (int)Math.Ceiling(r.TotalMinutes)) + "m";
        }
        public static bool Expire(UsageData data, DateTime now)
        {
            if (data == null) return false;
            bool expired = false;
            if (data.Current != null && data.Current.ResetAt.HasValue && data.Current.ResetAt.Value <= now) { data.Current = null; expired = true; }
            if (data.Weekly != null && data.Weekly.ResetAt.HasValue && data.Weekly.ResetAt.Value <= now) { data.Weekly = null; expired = true; }
            if (expired) data.ResetPending = true;
            return expired;
        }
    }
}
