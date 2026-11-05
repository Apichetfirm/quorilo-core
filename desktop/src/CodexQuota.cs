using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public static class CodexQuota
    {
        private static Dictionary<string, object> Obj(Dictionary<string, object> p, string k) { return ClaudeQuota.Object(ClaudeQuota.Field(p, k)); }
        public static UsageData Parse(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("Invalid quota response.");
            var buckets = Obj(root, "rateLimitsByLimitId");
            var bucket = buckets != null ? Obj(buckets, "codex") : Obj(root, "rateLimits");
            object id = ClaudeQuota.Field(bucket, "limitId");
            if (id != null && Convert.ToString(id) != "codex") bucket = null;
            string sourceKey = Convert.ToString(ClaudeQuota.Field(root, "source_key"));
            if (!System.Text.RegularExpressions.Regex.IsMatch(sourceKey ?? "", "^[a-f0-9]{24}$")) sourceKey = "codex";
            var result = new UsageData { Source = "Codex · บัญชี ChatGPT", SourceKey = sourceKey, Capability = "NoQuota" };
            foreach (string name in new[] { "primary", "secondary" }) {
                var w = Obj(bucket, name);
                double? used = ClaudeQuota.Number(ClaudeQuota.Field(w, "usedPercent")), duration = ClaudeQuota.Number(ClaudeQuota.Field(w, "windowDurationMins"));
                if (!used.HasValue || used < 0 || used > 100 || !duration.HasValue) continue;
                var m = new Metric { Remaining = 100 - used.Value };
                double? reset = ClaudeQuota.Number(ClaudeQuota.Field(w, "resetsAt"));
                if (reset.HasValue && reset > 0 && reset < 253402300800 && reset == Math.Floor(reset.Value)) m.ResetAt = ClaudeQuota.Epoch.AddSeconds(reset.Value);
                if (m.ResetAt.HasValue && m.ResetAt <= DateTime.UtcNow) { result.ResetPending = true; continue; }
                if (duration == 300) result.Current = m; else if (duration == 10080) result.Weekly = m;
            }
            if (result.HasQuota) result.Capability = "Quota";
            return result;
        }
    }
}
