using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public static class GeminiQuota
    {
        private static object Field(Dictionary<string, object> data, string key) { object value; return data != null && data.TryGetValue(key, out value) ? value : null; }
        private static DateTime Date(object value)
        {
            string text = value as string; DateTimeOffset parsed;
            if (text == null || text.Length > 40 || !System.Text.RegularExpressions.Regex.IsMatch(text, @"T.*(?:Z|[+-]\d\d:\d\d)$") || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) throw new FormatException("Invalid observation time.");
            return parsed.UtcDateTime;
        }
        private static Metric Read(object value, UsageData data, DateTime now)
        {
            var window = value as Dictionary<string, object>; if (window == null) return null;
            object raw = Field(window, "used_percentage");
            if (!(raw is int || raw is long || raw is double || raw is decimal)) return null;
            double used = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (Double.IsNaN(used) || Double.IsInfinity(used) || used < 0 || used > 100) return null;
            DateTime? reset = null;
            if (Field(window, "resets_at") != null) {
                try { reset = Date(Field(window, "resets_at")); } catch { return null; }
                if (reset > now.AddDays(8)) return null;
                if (reset <= now) { data.ResetPending = true; return null; }
            }
            return new Metric { Remaining = 100 - used, ResetAt = reset };
        }
        public static UsageData Parse(string json) { return Parse(json, DateTime.UtcNow); }
        public static UsageData Parse(string json, DateTime now)
        {
            if (json != null && json.Length <= 32768) { var snapshot = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>; if (Convert.ToString(Field(snapshot,"provider")) == "gemini-cli") return GeminiCliQuota.Parse(json,now); }
            if (json == null || json.Length > 8192) throw new FormatException("Quota snapshot too large.");
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || Convert.ToString(Field(root, "provider")) != "gemini-apps" || Convert.ToString(Field(root, "schema")) != "1") throw new FormatException("Wrong Gemini source.");
            DateTime captured = Date(Field(root, "captured_at"));
            if (captured > now.AddMinutes(2)) throw new FormatException("Future observation.");
            string source = Convert.ToString(Field(root, "source_key"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(source ?? "", "^[a-f0-9]{24}$")) throw new FormatException("Invalid source context.");
            var data = new UsageData { Source = "Gemini Apps · หน้า Usage", SourceKey = source, ReadAt = captured.ToLocalTime(), Capability = "NoQuota" };
            data.Current = Read(Field(root, "current"), data, now); data.Weekly = Read(Field(root, "weekly"), data, now);
            if (data.HasQuota) data.Capability = "Quota";
            return data;
        }
    }
}
