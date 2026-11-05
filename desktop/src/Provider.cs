using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows.Threading;
namespace CreditMonitor
{
    public interface IUsageClient : IDisposable
    {
        ConcurrentQueue<string> Updates { get; }
        bool IsRunning { get; }
        string LastError { get; }
        void Start(string address, Dispatcher dispatcher, Action callback);
        void RequestSnapshot();
    }
    public static class Provider
    {
        public static IUsageClient Create(string source = "Latest", int geminiRefreshSeconds = 15) {
#if GEMINI
            if(Product.AccountSlot.Length>0)source="Background";
            if(source!="CLI" && !Product.TestFeed)return new GeminiBackgroundUsageClient(geminiRefreshSeconds);
#endif
            if(Product.IsGemini && source=="CLI" && !Product.TestFeed)return new GeminiCliUsageClient(); return Product.IsClaude || Product.IsGemini || Product.TestFeed ? (IUsageClient)new FileUsageClient(source) : new CodexUsageClient(); }
        public static Task<string> DiscoverAsync() { if (Product.TestFeed) return Task.FromResult(Path.GetFullPath(Environment.GetEnvironmentVariable("CREDIT_MONITOR_FEED"))); return Product.IsClaude || Product.IsGemini ? Task.FromResult(Product.Storage) : CodexUsageClient.DiscoverAsync(); }
        public static UsageData Parse(string json) { var data = Product.IsGemini ? GeminiQuota.Parse(json) : Product.IsClaude ? ClaudeQuota.Parse(json) : CodexQuota.Parse(json); if (Product.TestFeed) data.Source = "ข้อมูลทดสอบในเครื่อง"; return data; }
    }
}
