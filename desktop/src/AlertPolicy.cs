using System;
using System.Collections.Generic;
using System.Globalization;
namespace CreditMonitor
{
    public static class AlertPolicy
    {
        public static bool Quiet(int hour, int start, int end)
        { return start == end || (start < end ? hour >= start && hour < end : hour >= start || hour < end); }
        public static string Key(UsageData data, Metric metric, string window, int threshold)
        { return data.SourceKey + ":" + window + ":" + metric.ResetAt.Value.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + ":" + threshold; }
        public static List<string> Eligible(UsageData data, Preferences p, DateTime localNow, bool connected, bool test)
        {
            var result = new List<string>();
            if (data == null || !p.AlertsEnabled || p.MonitoringPaused || !connected || test || data.ResetPending
                || (localNow - data.ReadAt).TotalSeconds > 90 || data.ReadAt > localNow.AddSeconds(5)
                || (p.SnoozeUntil.HasValue && p.SnoozeUntil > localNow)
                || (p.QuietHours && Quiet(localNow.Hour, p.QuietStart, p.QuietEnd))) return result;
            var windows = new[] { data.Current, data.Weekly };
            for (int i = 0; i < 2; i++) {
                var m = windows[i];
                if (m != null && (i == 0 ? p.AlertFiveHour : p.AlertWeekly) && m.ResetAt.HasValue
                    && m.ResetAt.Value.ToUniversalTime() > localNow.ToUniversalTime() && m.Remaining <= p.AlertThreshold)
                    result.Add(Key(data, m, i == 0 ? data.CurrentLabel : data.WeeklyLabel, p.AlertThreshold));
            }
            return result;
        }
    }
}
