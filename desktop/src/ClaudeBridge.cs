using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

namespace CreditMonitor
{
    public static class ClaudeQuota
    {
        public const int StaleSeconds = 90;
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object>; }
        public static object Field(Dictionary<string, object> obj, string key)
        { object value; return obj != null && obj.TryGetValue(key, out value) ? value : null; }
        public static double? Number(object value)
        {
            if (!(value is int || value is long || value is decimal || value is double || value is float)) return null;
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return Double.IsNaN(number) || Double.IsInfinity(number) ? (double?)null : number;
        }
        public static Dictionary<string, object> Window(object value)
        {
            var obj = Object(value);
            double? used = Number(Field(obj, "used_percentage"));
            if (!used.HasValue || used < 0 || used > 100) return null;
            var clean = new Dictionary<string, object> { { "used_percentage", used.Value } };
            double? reset = Number(Field(obj, "resets_at"));
            if (reset.HasValue && reset > 0 && reset < 253402300800 && reset == Math.Floor(reset.Value)) clean["resets_at"] = reset.Value;
            return clean;
        }
        private static Metric MetricFrom(object value, DateTime utcNow)
        {
            var clean = Window(value);
            if (clean == null) return null;
            var metric = new Metric { Remaining = 100 - (double)clean["used_percentage"] };
            object reset;
            if (clean.TryGetValue("resets_at", out reset)) {
                metric.ResetAt = Epoch.AddSeconds((double)reset);
                if (metric.ResetAt <= utcNow) return null; // Never invent a refill after reset.
            }
            return metric;
        }
        public static UsageData Parse(string json) { return ParseAt(json, DateTime.UtcNow); }
        public static UsageData ParseAt(string json, DateTime utcNow)
        {
            if (json == null || json.Length > 16384) throw new FormatException("Invalid snapshot size.");
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || Number(Field(root, "schema")) != 1) throw new FormatException("Invalid snapshot schema.");
            DateTimeOffset received;
            var captured = Field(root, "captured_at") as string;
            if (captured == null || !DateTimeOffset.TryParseExact(captured, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out received)
                || received.UtcDateTime > utcNow.AddSeconds(5)) throw new FormatException("Invalid snapshot time.");
            var limits = Object(Field(root, "rate_limits"));
            var result = new UsageData { ReadAt = received.LocalDateTime, Source = "Claude Code statusLine",
                SourceKey = Field(root, "source_key") as string ?? "unknown", Capability = Field(root, "capability") as string ?? "Unknown",
                Current = MetricFrom(Field(limits, "five_hour"), utcNow), Weekly = MetricFrom(Field(limits, "seven_day"), utcNow) };
            if (result.SourceKey != "unknown" && !System.Text.RegularExpressions.Regex.IsMatch(result.SourceKey, "^[a-f0-9]{24}$")) result.SourceKey = "unknown";
            if (result.Capability != "Quota" && result.Capability != "MissingRateLimits" && result.Capability != "NoQuota") result.Capability = "Unknown";
            foreach (string name in new[] { "five_hour", "seven_day" }) {
                var w = Window(Field(limits, name));
                var reset = Number(Field(w, "resets_at"));
                if (reset.HasValue && Epoch.AddSeconds(reset.Value) <= utcNow) result.ResetPending = true;
            }
            return result;
        }
        public static string ResetLabel(Metric metric)
        {
            return QuotaFormatting.ResetLabel(metric);
        }
    }
    public static class ClaudeBridge
    {
        public static string Storage { get { return Product.Storage; } }
        public static string SnapshotPath(string storage) { return Path.Combine(storage, "claude-usage.json"); }
        public static string Sanitize(string json, DateTime utcNow)
        {
            if (json == null || json.Length > 1024 * 1024) throw new FormatException("Invalid status line input.");
            var obj = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (obj == null) throw new FormatException("Expected a status line object.");
            var limits = ClaudeQuota.Object(ClaudeQuota.Field(obj, "rate_limits"));
            var clean = new Dictionary<string, object>();
            foreach (var quotaKey in new[] { "five_hour", "seven_day" }) {
                var window = ClaudeQuota.Window(ClaudeQuota.Field(limits, quotaKey));
                if (window != null) clean[quotaKey] = window;
            }
            string session = ClaudeQuota.Field(obj, "session_id") as string;
            string key = "unknown";
            if (!String.IsNullOrEmpty(session)) using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(session))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
            var cost = ClaudeQuota.Object(ClaudeQuota.Field(obj, "cost"));
            double? apiDuration = ClaudeQuota.Number(ClaudeQuota.Field(cost, "total_api_duration_ms"));
            string marker;
            using (var hash = SHA256.Create()) marker = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key + "|" + (apiDuration.HasValue ? apiDuration.Value.ToString(CultureInfo.InvariantCulture) : "") + "|" + new JavaScriptSerializer().Serialize(clean)))).Replace("-", "");
            return new JavaScriptSerializer().Serialize(new { schema = 1, captured_at = utcNow.ToString("o"), observation_marker = marker, source_key = key, capability = limits == null ? "MissingRateLimits" : clean.Count == 0 ? "NoQuota" : "Quota", rate_limits = clean });
        }
        public static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static string Capture(string json, string storage)
        {
            string sanitized;
            using (var mutex = new Mutex(false, "Local\\ClaudeCodeMonitor-Snapshot-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)) {
                bool acquired;
                try { acquired = mutex.WaitOne(2000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Snapshot busy.");
                try {
                    DateTime utcNow = DateTime.UtcNow;
                    sanitized = Sanitize(json, utcNow);
                    var source = ClaudeQuota.Parse(sanitized).SourceKey;
                    string sourcesDir = Path.Combine(storage, "claude-sources");
                    string sourcePath = Path.Combine(sourcesDir, source + ".json");
                    // Identical cached data from a statusline timer keeps its original observation time.
                    try {
                        if (File.Exists(sourcePath) && new FileInfo(sourcePath).Length <= 16384) {
                            var before = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(sourcePath)) as Dictionary<string, object>;
                            var now = new JavaScriptSerializer().DeserializeObject(sanitized) as Dictionary<string, object>;
                            if (ClaudeQuota.Field(before, "observation_marker") != null && Object.Equals(ClaudeQuota.Field(before, "observation_marker"), ClaudeQuota.Field(now, "observation_marker"))) {
                                now["captured_at"] = ClaudeQuota.Field(before, "captured_at");
                                sanitized = new JavaScriptSerializer().Serialize(now);
                            }
                        }
                    } catch { }
                    AtomicWrite(sourcePath, sanitized);
                    bool newest = true;
                    try { string latest = SnapshotPath(storage); if (File.Exists(latest) && new FileInfo(latest).Length <= 16384) newest = ClaudeQuota.Parse(File.ReadAllText(latest)).ReadAt <= ClaudeQuota.Parse(sanitized).ReadAt; } catch { }
                    if (newest) AtomicWrite(SnapshotPath(storage), sanitized);
                    // Bound stored sessions and remove abandoned files after 7 days using a single DirectoryInfo pass.
                    var dirInfo = new DirectoryInfo(sourcesDir);
                    if (dirInfo.Exists) {
                        var files = dirInfo.GetFiles("*.json");
                        var kept = new List<FileInfo>(files.Length);
                        DateTime cutoff = utcNow.AddDays(-7);
                        for (int i = 0; i < files.Length; i++) {
                            if (files[i].LastWriteTimeUtc < cutoff) { try { files[i].Delete(); } catch { } }
                            else kept.Add(files[i]);
                        }
                        if (kept.Count > 64) {
                            kept.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                            for (int i = 64; i < kept.Count; i++) { try { kept[i].Delete(); } catch { } }
                        }
                    }
                } finally { mutex.ReleaseMutex(); }
            }
            var data = ClaudeQuota.Parse(sanitized);
            return "Claude | 5h " + (data.Current == null ? "--" : data.Current.Display + " left")
                + " | Weekly " + (data.Weekly == null ? "--" : data.Weekly.Display + " left");
        }
        public static int Run()
        {
            try {
#if COMMERCIAL
                var trialLicense=new LicenseClient(CommerceConfiguration.Load(),LicenseClient.Storage,LicenseClient.DeviceIdentity());
                trialLicense.RefreshAsync(false,true).GetAwaiter().GetResult();
                if(!trialLicense.Allowed){Console.WriteLine("Claude | Trial locked");return 1;}
#endif
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = new UTF8Encoding(false);
                var input = new StringBuilder();
                var buffer = new char[4096]; int read;
                while ((read = Console.In.Read(buffer, 0, buffer.Length)) > 0) {
                    if (input.Length + read > 1024 * 1024) throw new FormatException();
                    input.Append(buffer, 0, read);
                }
#if COMMERCIAL
                if(!trialLicense.Allowed){Console.WriteLine("Claude | Trial locked");return 1;}
#endif
                Console.WriteLine(Capture(input.ToString(), Storage));
                return 0;
            } catch { Console.WriteLine("Claude | quota unavailable"); return 1; }
        }
    }
}
