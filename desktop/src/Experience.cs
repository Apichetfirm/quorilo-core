using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace CreditMonitor
{
    public sealed partial class Widget
    {
        private TextBlock diagnosticText, connectionText;
        private string setupCheck = "กำลังตรวจองค์ประกอบในเครื่อง";
        private bool? cliDetected;
        private readonly HashSet<string> notified = new HashSet<string>();
        private readonly object historyLock = new object();
        private Task historyTask = Task.FromResult(0);
        private string historySignature;
        private void InitializeExperience()
        {
            preferences.AlertThreshold = Math.Max(1, Math.Min(50, preferences.AlertThreshold));
            preferences.QuietStart = Math.Max(0, Math.Min(23, preferences.QuietStart));
            preferences.QuietEnd = Math.Max(0, Math.Min(23, preferences.QuietEnd));
            if (preferences.ClaudeSource != "Latest" && preferences.ClaudeSource != "unknown"
                && !System.Text.RegularExpressions.Regex.IsMatch(preferences.ClaudeSource ?? "", "^[a-f0-9]{24}$")) preferences.ClaudeSource = "Latest";
            foreach (string name in new[] { "RefreshButton", "StripRefreshButton", "SettingsButton", "StripSettingsButton", "PinButton", "MiniPinButton", "HideButton", "StripHideButton", "CompactButton" }) {
                var element = Control<FrameworkElement>(name);
                if (element != null) AutomationProperties.SetName(element, name.Contains("Refresh") ? "อ่านเครดิตตอนนี้" : name.Contains("Settings") ? "เปิดตั้งค่า" : name.Contains("Pin") ? "ปักหมุดหรือซ่อนริมจอ" : name.Contains("Compact") ? "ย่อหรือขยาย" : "ซ่อนไปถาดระบบ");
            }
            if (!verification) {
                try { foreach (string key in new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(Path.Combine(storage, "alert-keys.json"))) ?? new string[0]) notified.Add(key); } catch { }
                SystemEvents.PowerModeChanged += OnPowerMode;
            }
        }
        private void OnPowerMode(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume || exiting || Window.Dispatcher.HasShutdownStarted) return;
            Window.Dispatcher.BeginInvoke(new Action(async () => { SetState("Stale", "เครื่องตื่นจาก Sleep · กำลังตรวจข้อมูลใหม่"); nextConnectAt = DateTime.MinValue; UpdateCountdown(); PositionWindow(); await RefreshAsync(); }));
        }
        public void ToggleMonitoring()
        {
            preferences.MonitoringPaused = !preferences.MonitoringPaused;
            if (preferences.MonitoringPaused) { if (watcher != null) { watcher.Dispose(); watcher = null; } SetState("Paused", "พักการอ่านเครดิตไว้"); }
            else { SetState("Waiting", "กำลังทำงานต่อ"); Window.Dispatcher.BeginInvoke(new Action(async () => await ConnectAsync())); }
            SaveSettings(); UpdateDiagnostics();
        }
        public void SelectSource(string key)
        {
            if (key != "Latest" && key != "unknown" && !System.Text.RegularExpressions.Regex.IsMatch(key ?? "", "^[a-f0-9]{24}$")) throw new ArgumentException("Invalid source key");
            preferences.ClaudeSource = key;
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            latestData = null; lastRead = null; ApplyMetric("Current", null); ApplyMetric("Weekly", null);
            SetState(preferences.MonitoringPaused ? "Paused" : "Waiting", "เปลี่ยนแหล่งข้อมูล · รอค่าจากแหล่งที่เลือก");
            SaveSettings(); Window.Dispatcher.BeginInvoke(new Action(async () => await ConnectAsync()));
        }
        private async void LoadSources(ComboBox combo)
        {
            string[] paths;
            try { paths = await Task.Run(() => { string dir = Path.Combine(storage, "claude-sources"); return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(64).ToArray() : new string[0]; }); }
            catch { setupCheck = "อ่านรายชื่อ session ไม่ได้ · ตรวจสิทธิ์โฟลเดอร์ข้อมูล"; UpdateDiagnostics(); return; }
            if (settingsWindow == null) return;
            var keys = new List<string> { "Latest" }; combo.Items.Add("ล่าสุดจากทุก session");
            foreach (string path in paths) {
                string key = Path.GetFileNameWithoutExtension(path);
                if (key != "unknown" && !System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-f0-9]{24}$")) continue;
                keys.Add(key); combo.Items.Add("session " + key.Substring(0, Math.Min(8, key.Length)) + " · " + File.GetLastWriteTime(path).ToString("dd/MM HH:mm"));
            }
            if (!keys.Contains(preferences.ClaudeSource)) { keys.Add(preferences.ClaudeSource); combo.Items.Add("session " + preferences.ClaudeSource.Substring(0, Math.Min(8, preferences.ClaudeSource.Length)) + " · ยังไม่มีไฟล์"); }
            combo.SelectedIndex = keys.IndexOf(preferences.ClaudeSource);
            combo.SelectionChanged += (s, e) => { if (combo.SelectedIndex >= 0) SelectSource(keys[combo.SelectedIndex]); };
        }
        private async void CheckSetupAsync()
        {
            try {
                setupCheck = await Task.Run(async () => {
                    if (Product.IsGemini) { if(preferences.GeminiSource=="CLI"){cliDetected=File.Exists(GeminiCliUsageClient.CredentialsPath);return GeminiCliUsageClient.Inspect();} cliDetected=null;return BackgroundInspect(); }
                    if (Product.IsClaude) { cliDetected = Product.FindClaude() != null; return "Claude CLI: " + (cliDetected.Value ? "พบในเครื่อง" : "ไม่พบ") + "\nตัวส่งเครดิต: " + (File.Exists(BridgeExecutable) ? "พบ" : "ไม่พบ") + "\nstatusLine: " + ClaudeConfiguration.Inspect(ClaudeConfiguration.SettingsPath, BridgeExecutable); }
                    try { await CodexUsageClient.DiscoverAsync(); cliDetected = true; return "Codex CLI: พบในเครื่อง\nการลงชื่อเข้าใช้: ตรวจผ่าน app-server เมื่ออ่านข้อมูล"; } catch { cliDetected = false; return "Codex CLI: ไม่พบในเครื่อง"; }
                });
            } catch { setupCheck = "อ่านการตั้งค่าไม่ได้ · ตรวจสิทธิ์ไฟล์และรูปแบบ JSON ของ Claude settings"; }
            if (!exiting) UpdateDiagnostics();
        }
        private void UpdateDiagnostics()
        {
            string info = "สถานะ: " + StateLabel(State)
                + "\nแหล่งข้อมูล: " + (latestData == null ? Product.Source : latestData.Source)
                + (Product.IsClaude && latestData != null ? "\nSession: " + latestData.SourceKey.Substring(0, Math.Min(8, latestData.SourceKey.Length)) + " · " + (preferences.ClaudeSource == "Latest" ? "เลือกค่าล่าสุดจากทุก session" : "เลือก session นี้") : "")
                + "\nรับล่าสุด: " + (lastRead.HasValue ? lastRead.Value.ToString("dd/MM/yyyy HH:mm:ss") : "ยังไม่ได้รับข้อมูล")
                + "\n"+PrimaryLabel+": " + SettingsMetricDetail(latestData == null ? null : latestData.Current) + "\n"+SecondaryLabel+": " + SettingsMetricDetail(latestData == null ? null : latestData.Weekly)
;
            if (connectionText != null) connectionText.Text = (Product.IsGemini ? preferences.GeminiSource=="CLI" ? "Gemini CLI · Pro / Flash" : "Gemini · 5h / Weekly · อ่านเบื้องหลัง" : Product.Source) + (cliDetected.HasValue ? (cliDetected.Value ? " · พร้อมในเครื่อง" : " · ยังไม่พบในเครื่อง") : "");
            if (diagnosticText != null) diagnosticText.Text = info;
            if (settingsSetupText != null) settingsSetupText.Text = setupCheck;
            UpdateSettingsSummary();
        }
        private object SafeMetric(Metric m) { return m == null ? null : new { remaining = m.Remaining, resetsAt = m.ResetAt.HasValue ? m.ResetAt.Value.ToUniversalTime().ToString("o") : null, model=m.Model }; }
        private object SafeData(UsageData data) { return new { receivedAt = data.ReadAt.ToUniversalTime().ToString("o"), source = data.SourceKey, capability = data.Capability, primaryLabel=data.CurrentLabel, secondaryLabel=data.WeeklyLabel, fiveHour = SafeMetric(data.Current), weekly = SafeMetric(data.Weekly) }; }
        private void CheckAlerts(UsageData data)
        {
            if (verification || tray == null) return;
            var keys = AlertPolicy.Eligible(data, preferences, DateTime.Now, watcher != null && watcher.IsRunning && String.IsNullOrEmpty(watcher.LastError), Product.TestFeed);
            var fresh = keys.Where(k => !notified.Contains(k)).ToArray(); if (fresh.Length == 0) return;
            foreach (string key in fresh) notified.Add(key);
            if (notified.Count > 128) { var retain = notified.Skip(notified.Count - 128).ToArray(); notified.Clear(); foreach (string key in retain) notified.Add(key); }
            try { Directory.CreateDirectory(storage); ClaudeBridge.AtomicWrite(Path.Combine(storage, "alert-keys.json"), new JavaScriptSerializer().Serialize(notified.ToArray())); } catch { Log("alerts", "DedupSaveFailed"); }
            tray.ShowBalloonTip(8000, Product.Name, String.Join(" · ", fresh.Select(key => key.Contains(":"+data.CurrentLabel+":") ? data.CurrentLabel+" เหลือ " + data.Current.Display : data.WeeklyLabel+" เหลือ " + data.Weekly.Display)), System.Windows.Forms.ToolTipIcon.Warning);
        }
        private void RecordHistory(UsageData data)
        {
            if (verification || Product.TestFeed || !data.HasQuota || data.ResetPending || (DateTime.Now - data.ReadAt).TotalSeconds > 90) return;
            var serializer = new JavaScriptSerializer(); string entry = serializer.Serialize(SafeData(data));
            string signature = serializer.Serialize(new { source = data.SourceKey, fiveHour = SafeMetric(data.Current), weekly = SafeMetric(data.Weekly) });
            if (signature == historySignature) return; historySignature = signature;
            lock (historyLock) historyTask = historyTask.ContinueWith(previous => {
                try {
                    string path = Path.Combine(storage, "quota-history.json");
                    var entries = File.Exists(path) && new FileInfo(path).Length < 1024 * 1024 ? serializer.Deserialize<List<object>>(File.ReadAllText(path)) ?? new List<object>() : new List<object>();
                    entries.Add(serializer.DeserializeObject(entry)); if (entries.Count > 512) entries.RemoveRange(0, entries.Count - 512);
                    ClaudeBridge.AtomicWrite(path, serializer.Serialize(entries));
                } catch { Log("history", "HistoryWriteFailed"); }
            }, TaskScheduler.Default);
        }
        private void ExportDiagnostics()
        {
            var dialog = new SaveFileDialog { FileName = Product.StorageName + "-diagnostics.json", Filter = "JSON (*.json)|*.json", AddExtension = true };
            if (dialog.ShowDialog(settingsWindow) != true) return;
            try { File.WriteAllText(dialog.FileName, DiagnosticsJson(), new System.Text.UTF8Encoding(false)); }
            catch { MessageBox.Show(settingsWindow, "บันทึกข้อมูลตรวจระบบไม่สำเร็จ"); }
        }
        public string DiagnosticsJson()
        {
            return new JavaScriptSerializer().Serialize(new { schema = 1, product = Product.Name, version = Version, exportedAt = DateTime.UtcNow.ToString("o"), state = State, testFeed = Product.TestFeed, cliDetected = cliDetected, readerRunning = watcher != null && watcher.IsRunning, sourceSelection = Product.IsGemini ? preferences.GeminiSource : preferences.ClaudeSource, lastQuota = latestData == null ? null : SafeData(latestData), notifications = new { enabled = preferences.AlertsEnabled, threshold = preferences.AlertThreshold, quietStart = preferences.QuietStart, quietEnd = preferences.QuietEnd } });
        }
        private async void ExportHistory()
        {
            var dialog = new SaveFileDialog { FileName = Product.StorageName + "-history.json", Filter = "JSON (*.json)|*.json", AddExtension = true };
            if (dialog.ShowDialog(settingsWindow) != true) return;
            try { await historyTask; string path = Path.Combine(storage, "quota-history.json"); await Task.Run(() => File.WriteAllText(dialog.FileName, File.Exists(path) ? File.ReadAllText(path) : "[]", new System.Text.UTF8Encoding(false))); }
            catch { if (settingsWindow != null) MessageBox.Show(settingsWindow, "ส่งออกประวัติไม่สำเร็จ"); }
        }
        private void DisposeExperience()
        {
            if (!verification) SystemEvents.PowerModeChanged -= OnPowerMode;
            try { historyTask.Wait(500); } catch { }
        }
    }
}
