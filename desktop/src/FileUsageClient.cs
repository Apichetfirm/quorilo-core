using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Windows.Threading;
namespace CreditMonitor
{
    public sealed class FileUsageClient : IUsageClient
    {
        private readonly ConcurrentQueue<string> updates = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> Updates { get { return updates; } }
        public bool IsRunning { get { return Volatile.Read(ref running) == 1; } }
        public string LastError { get; private set; }
        private Timer timer;
        private int running, reading, notified;
        private string path, last;
        private Dispatcher dispatcher;
        private Action changed;
        private readonly string sourceKey;
        public FileUsageClient(string source = "Latest") { sourceKey = source; }
        public void Start(string address, Dispatcher ui, Action callback) { path = Product.TestFeed ? address : Product.IsGemini ? GeminiBridge.SnapshotPath(address) : sourceKey == "Latest" ? ClaudeBridge.SnapshotPath(address) : Path.Combine(address, "claude-sources", sourceKey + ".json"); dispatcher = ui; changed = callback; Interlocked.Exchange(ref running, 1); timer = new Timer(s => Read(false), null, 0, 2000); }
        public void RequestSnapshot() { ThreadPool.QueueUserWorkItem(s => { if (Product.IsGemini && !Product.TestFeed && path != null) { try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "gemini-refresh.request"), Guid.NewGuid().ToString("N")); } catch { } } Read(true); }); }
        private void Read(bool force)
        {
            if (!IsRunning || Interlocked.Exchange(ref reading, 1) != 0) return;
            try {
                if (!File.Exists(path)) { SetError(last == null ? null : "SnapshotMissing"); return; }
                if (new FileInfo(path).Length > 16384) throw new FormatException();
                string json;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var reader = new StreamReader(file)) {
                    var content = new System.Text.StringBuilder(); var buffer = new char[2048]; int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) { if (content.Length + count > 16384) throw new FormatException(); content.Append(buffer, 0, count); }
                    json = content.ToString();
                }
                if (!force && json == last) { SetError(null); return; }
                Provider.Parse(json); last = json; LastError = null;
                string ignored; while (Updates.TryDequeue(out ignored)) { }
                Updates.Enqueue(json); Notify();
            } catch { SetError("SnapshotInvalid"); }
            finally { Interlocked.Exchange(ref reading, 0); }
        }
        private void SetError(string error) { if (LastError == error) return; LastError = error; Notify(); }
        private void Notify()
        {
            if (!IsRunning || dispatcher == null || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref notified, 1) != 0) return;
            try { dispatcher.BeginInvoke(new Action(() => { Interlocked.Exchange(ref notified, 0); if (IsRunning && changed != null) changed(); })); } catch (InvalidOperationException) { Interlocked.Exchange(ref notified, 0); }
        }
        public void Dispose() { Interlocked.Exchange(ref running, 0); if (timer != null) timer.Dispose(); changed = null; }
    }
}
