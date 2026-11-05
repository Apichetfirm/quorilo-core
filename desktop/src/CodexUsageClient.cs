using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace CreditMonitor
{
    // Uses the documented app-server protocol. Never reads auth files or launches a browser.
    public sealed class CodexUsageClient : IUsageClient
    {
        private static string cachedExecutable;
        private static DateTime cachedExecutableAt = DateTime.MinValue;
        private readonly ConcurrentQueue<string> updates = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Updates { get { return updates; } }
        public bool IsRunning { get { return Volatile.Read(ref running) != 0; } }
        public string LastError { get; private set; }
        private int running, disposed, nextId, reading, ready, notificationQueued;
        private string accountKey="unknown";
        private Process process;
        private Timer timer;
        private Dispatcher dispatcher;
        private Action changed;
        private readonly object writeLock = new object();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();

        public static Task<string> DiscoverAsync()
        {
            return Task.Run(() => {
                if (cachedExecutable != null && DateTime.UtcNow - cachedExecutableAt < TimeSpan.FromSeconds(30) && File.Exists(cachedExecutable)) {
                    return cachedExecutable;
                }
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string root = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
                if (Directory.Exists(root)) {
                    string found = Directory.GetDirectories(root).Select(x => Path.Combine(x, "codex.exe"))
                        .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (found != null) {
                        cachedExecutable = found;
                        cachedExecutableAt = DateTime.UtcNow;
                        return found;
                    }
                }
                string[] priorityDirs = new[] {
                    Path.Combine(localAppData, "Programs", "codex"),
                    Path.Combine(appData, "npm")
                };
                string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
                // Scan fast local directories first; defer UNC network shares (\\) to avoid SMB timeouts.
                foreach (bool allowUnc in new[] { false, true }) {
                    foreach (string rawDir in allowUnc ? pathDirs : priorityDirs.Concat(pathDirs)) {
                        try {
                            string dir = (rawDir ?? "").Trim().Trim('"');
                            if (String.IsNullOrEmpty(dir)) continue;
                            bool isUnc = dir.StartsWith(@"\\", StringComparison.Ordinal);
                            if (isUnc != allowUnc) continue;
                            string candidate = Path.Combine(dir, "codex.exe");
                            if (File.Exists(candidate)) {
                                cachedExecutable = candidate;
                                cachedExecutableAt = DateTime.UtcNow;
                                return candidate;
                            }
                        } catch { }
                    }
                }
                throw new FileNotFoundException("ไม่พบ Codex CLI · ติดตั้ง Codex และลงชื่อเข้าใช้ด้วยบัญชี ChatGPT ก่อน");
            });
        }

        public void Start(string executable, Dispatcher ui, Action callback)
        {
            dispatcher = ui; changed = callback;
            accountKey="unknown";Interlocked.Exchange(ref ready,0);
            string oldUpdate;while(Updates.TryDequeue(out oldUpdate)){}
            Interlocked.Exchange(ref running, 1);
            Task.Run(async () => {
                try {
                    StartupProfile.Mark("codex_process_start", executable);
                    var child = new Process { StartInfo = new ProcessStartInfo(executable, "app-server --stdio") {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
                        WorkingDirectory = Path.GetDirectoryName(executable)
                    }};
                    lock (writeLock) {
                        if (disposed != 0) { child.Dispose(); return; }
                        process = child; child.Start();
                    }
                    // Drain diagnostics without storing account details, tokens, or server output.
                    child.ErrorDataReceived += (s, e) => { };
                    child.BeginErrorReadLine();
                    var reader = ReadLoop(child);
                    await Call("initialize", new { clientInfo = new { name = "chatgpt_credit_monitor", title = "Quorilo Monitor", version = Product.Version } }).ConfigureAwait(false);
                    Send(new { method = "initialized" });
                    // Pipeline account/read and account/rateLimits/read concurrently after initialize,
                    // while enforcing account validation before publishing any quota data.
                    var accountTask = Call("account/read", new { refreshToken = false });
                    var snapshotTask = Call("account/rateLimits/read", null);
                    var accountReply = await accountTask.ConfigureAwait(false);
                    var account = ClaudeQuota.Object(ClaudeQuota.Field(accountReply, "account"));
                    if (account == null) throw new InvalidOperationException("LoginRequired");
                    if (Convert.ToString(ClaudeQuota.Field(account, "type")) == "apiKey") throw new InvalidOperationException("UnsupportedAccount");
                    accountKey = CodexAccountIdentity.Hash(account);
                    var initialSnapshot = await snapshotTask.ConfigureAwait(false);
                    Publish(CodexAccountIdentity.Attach(new JavaScriptSerializer().Serialize(initialSnapshot),accountKey));
                    StartupProfile.Mark("codex_first_snapshot_published");
                    Interlocked.Exchange(ref ready, 1);
                    lock (writeLock) {
                        if (disposed == 0 && IsRunning) timer = new Timer(s => RequestSnapshot(), null, 30000, 30000);
                    }
                    await reader.ConfigureAwait(false);
                    if (disposed == 0) Fail("การเชื่อมต่อ Codex สิ้นสุดลง · กำลังลองเชื่อมต่อใหม่");
                }
                catch (Exception error) { if (disposed == 0) Fail(error.Message == "LoginRequired" || error.Message == "UnsupportedAccount" ? error.Message : "ReaderDisconnected"); }
                finally { lock (writeLock) { if (process != null) { process.Dispose(); process = null; } } }
            });
        }
        private async Task ReadLoop(Process child)
        {
            string line;
            while ((line = await child.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null) {
                if (line.Length > 1024 * 1024) continue;
                Dictionary<string, object> message;
                try { message = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                if (message == null) continue;
                object value;
                if (message.TryGetValue("id", out value)) {
                    int id; TaskCompletionSource<Dictionary<string, object>> completion;
                    if (Int32.TryParse(Convert.ToString(value), out id) && pending.TryRemove(id, out completion)) completion.TrySetResult(message);
                }
                else if (message.TryGetValue("method", out value) && Convert.ToString(value) == "account/updated") {
                    if (Volatile.Read(ref ready) == 0) continue;
                    Publish("{\"monitorAccountChanged\":true}");
                    Fail("AccountChanged");
                    break;
                }
                else if (message.TryGetValue("method", out value) && Convert.ToString(value) == "account/rateLimits/updated") {
                    if(Volatile.Read(ref ready)==0)continue;
                    if (message.TryGetValue("params", out value)) {
                        var parameters = value as Dictionary<string, object>;
                        object bucketValue, limitId;
                        if (parameters != null && parameters.TryGetValue("rateLimits", out bucketValue)) {
                            var bucket = bucketValue as Dictionary<string, object>;
                            if (bucket != null && bucket.TryGetValue("limitId", out limitId) && limitId != null && Convert.ToString(limitId) != "codex") continue;
                        }
                        Publish(CodexAccountIdentity.Attach(new JavaScriptSerializer().Serialize(value),accountKey));
                    }
                }
            }
            foreach (var entry in pending) entry.Value.TrySetException(new IOException("Codex disconnected"));
        }
        private void Send(object value)
        {
            string line = new JavaScriptSerializer().Serialize(value);
            lock (writeLock) {
                if (disposed != 0 || process == null || process.HasExited) throw new IOException("Codex unavailable");
                process.StandardInput.WriteLine(line); process.StandardInput.Flush();
            }
        }
        private async Task<Dictionary<string, object>> Call(string method, object parameters)
        {
            int id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            using (var timeoutCts = new CancellationTokenSource()) {
                try {
                    Send(new { id = id, method = method, @params = parameters });
                    var timeoutTask = Task.Delay(8000, timeoutCts.Token);
                    if (await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false) != completion.Task) throw new TimeoutException();
                    timeoutCts.Cancel();
                    var reply = await completion.Task.ConfigureAwait(false);
                    if (reply.ContainsKey("error") || !reply.ContainsKey("result")) throw new IOException("Quota request failed");
                    return reply["result"] as Dictionary<string, object>;
                }
                finally { TaskCompletionSource<Dictionary<string, object>> removed; pending.TryRemove(id, out removed); }
            }
        }
        private async Task ReadSnapshot()
        {
            if (Interlocked.Exchange(ref reading, 1) != 0) return;
            try { var result = await Call("account/rateLimits/read", null).ConfigureAwait(false); Publish(CodexAccountIdentity.Attach(new JavaScriptSerializer().Serialize(result),accountKey)); }
            finally { Interlocked.Exchange(ref reading, 0); }
        }
        public void RequestSnapshot()
        {
            if (!IsRunning || disposed != 0 || Volatile.Read(ref ready) == 0) return;
            Task.Run(async () => { try { await ReadSnapshot().ConfigureAwait(false); } catch { if (disposed == 0) Fail("อ่านเครดิตไม่ได้ · กำลังลองเชื่อมต่อใหม่"); } });
        }
        private void Publish(string json)
        {
            if (disposed != 0 || !IsRunning) return;
            Updates.Enqueue(json); string old; while (Updates.Count > 8) Updates.TryDequeue(out old);
            Notify();
        }
        private void Notify()
        {
            if (disposed != 0 || dispatcher == null || dispatcher.HasShutdownStarted) return;
            if (Interlocked.Exchange(ref notificationQueued, 1) != 0) return;
            try { dispatcher.BeginInvoke(new Action(() => { Interlocked.Exchange(ref notificationQueued, 0); if (disposed == 0) changed(); })); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref notificationQueued, 0); }
        }
        private void Fail(string reason)
        {
            if (Interlocked.Exchange(ref running, 0) == 0) return;
            Interlocked.Exchange(ref ready, 0);
            LastError = reason; StopChild(); Notify();
        }
        private void StopChild()
        {
            lock (writeLock) {
                if (timer != null) { timer.Dispose(); timer = null; }
                if (process != null) {
                    try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(300)) process.Kill(); } } catch { }
                }
            }
            foreach (var entry in pending) entry.Value.TrySetCanceled();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Interlocked.Exchange(ref ready, 0);
            Interlocked.Exchange(ref running, 0); StopChild();
        }
    }

}
