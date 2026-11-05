using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public static class GeminiBridge
    {
        public static string SnapshotPath(string directory) { return Path.Combine(directory, "gemini-usage.json"); }
        public static string Capture(string input, string directory)
        {
            var serializer = new JavaScriptSerializer();var root=serializer.DeserializeObject(input) as System.Collections.Generic.Dictionary<string,object>;object provider;
            if(root==null || !root.TryGetValue("provider",out provider) || Convert.ToString(provider)!="gemini-apps")throw new FormatException("Wrong bridge source.");
            UsageData data = GeminiQuota.Parse(input);
            Func<Metric, object> metric = m => m == null ? null : new { used_percentage = 100 - m.Remaining, resets_at = m.ResetAt.HasValue ? m.ResetAt.Value.ToString("o") : null };
            string safe = serializer.Serialize(new { schema = 1, provider = "gemini-apps", source_key = data.SourceKey, captured_at = data.ReadAt.ToUniversalTime().ToString("o"), current = metric(data.Current), weekly = metric(data.Weekly) });
            Directory.CreateDirectory(directory);
            using (var mutex = new Mutex(false, "Local\\GeminiQuotaSnapshot-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)) {
                bool owned = false;
                try {
                    try { owned = mutex.WaitOne(3000); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("Snapshot busy.");
                    string path = SnapshotPath(directory);
                    if (File.Exists(path)) {
                        try { var previous = GeminiQuota.Parse(File.ReadAllText(path)); if (previous.ReadAt > data.ReadAt) return "OlderObservation"; } catch { }
                    }
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, safe, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                } finally { if (owned) mutex.ReleaseMutex(); }
            }
            return data.HasQuota ? "Quota" : "NoQuota";
        }
        private static bool Exact(Stream stream, byte[] buffer, int size)
        {
            int read = 0, count; while (read < size && (count = stream.Read(buffer, read, size - read)) > 0) read += count;
            return read == size;
        }
        private static void Reply(Stream output, object message, object gate)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(message));
            byte[] header = BitConverter.GetBytes((uint)bytes.Length);
            lock (gate) { output.Write(header, 0, 4); output.Write(bytes, 0, bytes.Length); output.Flush(); }
        }
        public static int Run(string[] args)
        {
            if (args.Length < 1 || args[0] != GeminiConfiguration.ExtensionOrigin) return 2;
            using (var input = Console.OpenStandardInput()) using (var output = Console.OpenStandardOutput()) {
                object gate = new object(); string seen = null;
                using (var refresh = new Timer(state => {
                    try {
                        string path = Path.Combine(Product.Storage, "gemini-refresh.request");
                        if (!File.Exists(path) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds > 30) return;
                        string nonce = File.ReadAllText(path); if (nonce.Length != 32 || nonce == seen) return;
                        seen = nonce; Reply(output, new { type = "refresh-request" }, gate);
                    } catch { }
                }, null, 1000, 1000)) {
                while (true) {
                    byte[] header = new byte[4]; if (!Exact(input, header, 4)) return 0;
                    uint length = BitConverter.ToUInt32(header, 0); if (length == 0 || length > 8192) return 3;
                    byte[] bytes = new byte[(int)length]; if (!Exact(input, bytes, bytes.Length)) return 3;
                    string status;
                    try { status = Capture(new UTF8Encoding(false, true).GetString(bytes), Product.Storage); } catch { status = "InvalidSnapshot"; }
                    Reply(output, new { ok = status != "InvalidSnapshot", status = status }, gate);
                }
                }
            }
        }
    }
    public static class GeminiBridgeProgram { public static int Main(string[] args) { return GeminiBridge.Run(args); } }
}
