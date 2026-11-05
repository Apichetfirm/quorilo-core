using CreditMonitor;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle(Product.Name)]
[assembly: AssemblyProduct(Product.Name)]
[assembly: AssemblyDescription("Independent desktop usage widget")]
[assembly: AssemblyVersion(Product.AssemblyVersion)]
[assembly: AssemblyFileVersion(Product.AssemblyVersion)]

namespace CreditMonitor
{
    public sealed class Preferences
    {
        public bool Compact = false;
        public bool Topmost = true;
        public bool Hidden = false;
        public double? Left;
        public double? Top;
        public double? Width; public double? Height;
        public string Theme = "Original";
        public string DockSide = "None";
        public string EdgeSide = "Nearest";
        public bool AnimateStatus = false;
        public bool OnboardingViewed;
        public string ClaudeSource = "Latest";
        public string GeminiSource = "Background";
        public int GeminiRefreshSeconds = 15;
        public bool MonitoringPaused;
        public bool AlertsEnabled;
        public bool AlertFiveHour = true; public bool AlertWeekly = true;
        public int AlertThreshold = 20; public int QuietStart = 22; public int QuietEnd = 8;
        public bool QuietHours = true;
        public DateTime? SnoozeUntil;
        public string TimeFormat = "24H";
        public bool EdgeShowPercent = true;
        public bool EdgeMotion = true;
        public int SchemaVersion = 2;
    }

    public sealed partial class Widget : IDisposable
    {
#if !TRIAL
        int observationScope;
#endif
        public const string Version = Product.Version;
        public readonly Window Window;
        public string State { get; private set; }
        public int DiscoveryAttempts { get; private set; }
        public bool Compact { get { return preferences.Compact; } }
        public DateTime? LastReceived { get { return lastRead; } }
        public bool Connecting { get { return connecting; } }
        private readonly string storage;
        private readonly string settingsPath;
        private Preferences preferences = new Preferences();
        private bool geminiSourceMigrated;
        private UsageData latestData;
        private readonly DispatcherTimer countdown = new DispatcherTimer();
        private readonly DispatcherTimer reconnect = new DispatcherTimer();
        private IUsageClient watcher;
        private int consecutiveFailures;
        private DateTime nextConnectAt = DateTime.MinValue;
        private Forms.NotifyIcon tray;
        private Forms.ContextMenuStrip trayMenu;
        private System.Drawing.Text.PrivateFontCollection trayFonts;
        private System.Drawing.Font trayFont;
        private NativeFont nativeTrayFont;
        private System.Drawing.Icon appIcon;
        private CreditMonitorWindowSignal wakeSignal;
        private CreditMonitorWindowSignal stopSignal;
        private bool connecting;
        private bool exiting;
        private bool disposed;
        private readonly bool verification;
        private readonly FontFamily font;
        private DateTime? lastRead;
        private static string AssetsRoot { get { return Path.GetDirectoryName(typeof(Widget).Assembly.Location); } }

        public Widget(string dataDirectory, bool forVerification)
        {
            StartupProfile.Mark("widget_ctor_start", Program.HubWorker ? "hub_worker" : "standalone");
            verification = forVerification;
            storage = dataDirectory;
            settingsPath = Path.Combine(storage, "settings.json");
            ReadSettings();
            Window = (Window)CompiledViews.Load("Widget.xaml");
            font = new FontFamily(new Uri(Path.Combine(AssetsRoot, "Fonts") + Path.DirectorySeparatorChar), "./#Noto Sans Thai");
            Window.FontFamily = font;
            Window.Title = Product.Name;
            Control<TextBlock>("HeaderTitle").Text = Product.Label;
            Control<TextBlock>("HeaderSubtitle").Text = Product.Channel + " · เหลือ";
            Window.Topmost = preferences.Topmost;
            countdown.Interval = TimeSpan.FromSeconds(10); countdown.Tick += (s, e) => UpdateCountdown();
            if (!Program.HubWorker) {
                WireActions();
                BuildMenus();
            }
            ApplyMetric("Current", null);
            ApplyMetric("Weekly", null);
            ApplyQuotaLabels();
            InitializeAppearance();
            InitializeExperience();
            InitializeCommerce();
            SetState("Waiting", "เปิดตั้งค่า → การเชื่อมต่อ เพื่อเริ่มใช้งาน");
            reconnect.Interval = TimeSpan.FromSeconds(2);
            reconnect.Tick += async (s, e) => { if (DateTime.UtcNow >= nextConnectAt) await ConnectAsync(); };
            Window.Closing += OnClosing;
            if (!verification) {
                string identity = Program.InstanceName;
                wakeSignal = new CreditMonitorWindowSignal(identity + "-Show", Window.Dispatcher, Show);
                stopSignal = new CreditMonitorWindowSignal(identity + "-Stop", Window.Dispatcher, Exit);
            }
            if (geminiSourceMigrated && !verification) SaveSettings();
            InitializeHubWorker();
            StartupProfile.Mark("widget_ctor_end");
        }

        private static string ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (stream == null) throw new FileNotFoundException("Missing application resource: " + name);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }
        public T Control<T>(string name) where T : class { return Window.FindName(name) as T; }
        private void WireActions()
        {
            Control<Button>("RefreshButton").Click += async (s, e) => await RefreshAsync();
            Control<Button>("CompactButton").Click += (s, e) => SetMode(!Compact, true);
            Control<Button>("HideButton").Click += (s, e) => Hide();
            Window.MouseLeftButtonDown += (s, e) => {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null) {
                    if (source is Button || source is System.Windows.Controls.Primitives.Thumb) return;
                    if (!(source is Visual)) return;
                    source = VisualTreeHelper.GetParent(source);
                }
                if (e.ClickCount >= 2) SetMode(!Compact, true);
                else if (e.LeftButton == MouseButtonState.Pressed) {
                    BeginWindowMove();
                    try { Window.DragMove(); } catch (InvalidOperationException) { }
                    finally { EndWindowMove(); }
                }
                e.Handled = true;
            };
            Window.KeyDown += async (s, e) => {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) { OpenSettings(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M) { SetMode(!Compact, true); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.P) { TogglePinned(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { await RefreshAsync(); e.Handled = true; }
            };
        }
        private MenuItem Item(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (s, e) => action();
            return item;
        }
        private void BuildMenus()
        {
            var menu = new ContextMenu { FontFamily = font, FontSize = 12 };
            menu.Items.Add(Item("อ่านค่าตอนนี้  Ctrl+R", async () => await RefreshAsync()));
            var size = Item("ย่อเป็นแถบเล็ก  Ctrl+M", () => SetMode(!Compact, true));
            menu.Items.Add(size);
            menu.Items.Add(Item("ธีม / ตั้งค่า  Ctrl+,", OpenSettings));
            if (Product.IsClaude) { menu.Items.Add(Item("เชื่อม Claude Code", ConnectClaude)); menu.Items.Add(Item("หยุดส่งเครดิตจาก Claude Code", DisconnectClaude)); }
            menu.Items.Add(Item("พัก / ทำงานต่อ", ToggleMonitoring));
            var pin = Item("ปักหมุด: ไม่ซ่อนริมจอ  Ctrl+P", TogglePinned);
            menu.Items.Add(pin);
            var dockMenu = new MenuItem { Header = "ริมจอที่จะซ่อน" };
            dockMenu.Items.Add(Item("ด้านที่ใกล้ที่สุด", () => SetAutoHideSide("Nearest"))); dockMenu.Items.Add(Item("ซ้าย", () => SetAutoHideSide("Left"))); dockMenu.Items.Add(Item("ขวา", () => SetAutoHideSide("Right"))); menu.Items.Add(dockMenu);
            menu.Items.Add(Item("ซ่อนไปถาดระบบ  Esc", Hide));
            menu.Items.Add(new Separator());
            var topmost = Item("อยู่เหนือหน้าต่างอื่น", () => { Window.Topmost = !Window.Topmost; SaveSettings(); });
            var startup = Item("เปิดพร้อม Windows", ToggleStartup);
            menu.Items.Add(topmost);
            menu.Items.Add(startup);
            menu.Items.Add(Item("คู่มือ / เกี่ยวกับโปรแกรม", ShowHelp));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("ออกจากโปรแกรม", Exit));
            menu.Opened += (s, e) => {
                size.Header = Compact ? "ขยายวิดเจ็ต  Ctrl+M" : "ย่อเป็นแถบเล็ก  Ctrl+M";
                topmost.IsChecked = Window.Topmost;
                startup.IsChecked = Lifecycle.StartupEnabled;
                pin.IsChecked = Pinned;
                pin.Header = Pinned ? "ปักหมุด: ไม่ซ่อนริมจอ  Ctrl+P" : "เอาหมุดออก: ซ่อนริมจออัตโนมัติ  Ctrl+P";
            };
            Window.ContextMenu = menu;
            if (verification || Program.HubWorker) return;
            trayFonts = new System.Drawing.Text.PrivateFontCollection();
            byte[] trayFontBytes = File.ReadAllBytes(Path.Combine(AssetsRoot, "Fonts", "NotoSansThai-Regular.ttf"));
            nativeTrayFont = new NativeFont(trayFontBytes);
            trayFonts.AddMemoryFont(nativeTrayFont.Memory, trayFontBytes.Length);
            trayFont = new System.Drawing.Font(trayFonts.Families[0], 9f);
            trayMenu = new Forms.ContextMenuStrip { Font = trayFont };
            AddTray("แสดงวิดเจ็ต", Show);
            AddTray("ซ่อนวิดเจ็ต", Hide);
            AddTray("ย่อ / ขยาย", () => SetMode(!Compact, true));
            AddTray("ธีม / ตั้งค่า", OpenSettings);
            if (Product.IsClaude) AddTray("เชื่อม Claude Code", ConnectClaude);
            AddTray("พัก / ทำงานต่อ", ToggleMonitoring);
            AddTray("ปักหมุด / ซ่อนริมจออัตโนมัติ", TogglePinned);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            AddTray("อ่านค่าตอนนี้", async () => await RefreshAsync());
            AddTray("คู่มือ / เกี่ยวกับโปรแกรม", ShowHelp);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            AddTray("ออกจากโปรแกรม", Exit);
            appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            tray = new Forms.NotifyIcon { Icon = appIcon, Text = Product.Name, ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += (s, e) => Show();
        }
        private void AddTray(string text, Action action) { trayMenu.Items.Add(text).Click += (s, e) => action(); }
        private void ToggleStartup()
        {
            try { Lifecycle.SetStartup(!Lifecycle.StartupEnabled, Assembly.GetExecutingAssembly().Location); }
            catch (Exception error) { MessageBox.Show(Window, error.Message, "ตั้งค่า Startup", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        private void ShowHelp()
        {
            string guide = Path.Combine(AssetsRoot, "README.html");
            if (File.Exists(guide)) Process.Start(new ProcessStartInfo(guide) { UseShellExecute = true });
            else MessageBox.Show(Window, Product.Name + " " + Version + "\nแสดงเครดิต 5h/Weekly · เครื่องมืออิสระ", "เกี่ยวกับโปรแกรม");
        }
        private string BridgeExecutable { get { string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","ClaudeCodeMonitor","ClaudeQuotaBridge.exe");if(Program.HubWorker&&Product.IsClaude&&File.Exists(legacy)&&ClaudeConfiguration.UsesBridge(ClaudeConfiguration.SettingsPath,legacy))return legacy;return Path.Combine(AssetsRoot, "ClaudeQuotaBridge.exe"); } }
        private async void ConnectClaude()
        {
            try {
                if (!File.Exists(BridgeExecutable)) throw new IOException("ไม่พบตัวส่งเครดิต · ติดตั้งโปรแกรมอีกครั้ง");
                bool changed = await Task.Run(() => ClaudeConfiguration.Connect(ClaudeConfiguration.SettingsPath, BridgeExecutable));
                if (exiting) return;
                MessageBox.Show(Window, changed ? "ตั้งค่าตัวส่งเครดิตแล้ว\nเปิด Claude Code และใช้งานตามปกติ\nข้อมูลจะมาเมื่อ Claude Code ส่ง statusLine\nบัญชีและช่องทางใช้งานต้องส่ง rate_limits" : "ตั้งค่าไว้แล้ว · รอข้อมูลจาก Claude Code", "เชื่อม Claude Code");
            } catch (Exception error) { if (!exiting) MessageBox.Show(Window, error.Message, "เชื่อม Claude Code", MessageBoxButton.OK, MessageBoxImage.Information); }
            if (!exiting) CheckSetupAsync();
        }
        private async void DisconnectClaude()
        {
            try {
                bool removed = await Task.Run(() => ClaudeConfiguration.Disconnect(ClaudeConfiguration.SettingsPath, BridgeExecutable));
                if (exiting) return;
                MessageBox.Show(Window, removed ? "หยุดส่งเครดิตแล้ว · เก็บค่าครั้งล่าสุดไว้" : "ไม่มี statusLine ของตัวมอนิเตอร์ที่จะลบ", Product.Name);
            } catch (Exception error) { if (!exiting) MessageBox.Show(Window, error.Message, Product.Name); }
            if (!exiting) CheckSetupAsync();
        }
        public async Task ConnectAsync()
        {
            if (exiting || connecting || preferences.MonitoringPaused || (watcher != null && watcher.IsRunning)) return;
            if (!CanStartCommercialReader()) return;
            connecting = true; DiscoveryAttempts++;
            StartupProfile.Mark("connect_start", Product.Channel);
            Control<Button>("RefreshButton").IsEnabled = false;
            SetState("Connecting", "กำลังค้นหาตัวอ่านเครดิตในเครื่อง");
            try {
                if (watcher != null) { watcher.Dispose(); watcher = null; }
                string address = await Provider.DiscoverAsync();
                if ((Product.IsClaude || Product.IsGemini) && !Product.TestFeed) address = storage;
                if (exiting || preferences.MonitoringPaused || (Product.AccountLicensed&&!CommercialAllowed)) return;
#if !TRIAL
                observationScope++;
                latestData=null;lastRead=null;ApplyMetric("Current",null);ApplyMetric("Weekly",null);UpdateCountdown();
#endif
                watcher = Provider.Create(Product.IsGemini ? preferences.GeminiSource : preferences.ClaudeSource, preferences.GeminiRefreshSeconds);
                watcher.Start(address, Window.Dispatcher, Receive);
                SetState("Waiting", Product.IsGemini ? preferences.GeminiSource=="CLI" ? "รอ Gemini CLI · ลงชื่อเข้าใช้ด้วย Google แล้วรีเฟรช" : "รอเครดิต · กดเชื่อมบัญชี Gemini ในตั้งค่า" : Product.IsClaude ? "ตัวอ่านพร้อม · รอข้อมูลจาก Claude Code" : "ตัวอ่านพร้อม · รอข้อมูลจาก Codex");
                StartupProfile.Mark("connect_watcher_started", Product.Channel);
            } catch (FileNotFoundException) {
                ScheduleReconnect();
                if (!exiting) SetState("MissingCli", "ไม่พบ CLI · เปิดตั้งค่า → การเชื่อมต่อ");
                Log("connect", "CliMissing");
            } catch {
                if (!exiting) SetState("Error", "เริ่มตัวอ่านไม่ได้ · เปิดแท็บตรวจระบบ");
                ScheduleReconnect();
                Log("connect", "ReaderStartFailed");
            } finally { connecting = false; if (!exiting) Control<Button>("RefreshButton").IsEnabled = true; }
        }
        public async Task RefreshAsync()
        {
            if (exiting || preferences.MonitoringPaused) return;
            if(Product.AccountLicensed&&!CommercialAllowed){
                if(watcher!=null){watcher.Dispose();watcher=null;}
                await CheckLicenseAsync(false);
                if(!CommercialAllowed)return;
            }
            if (watcher != null && watcher.IsRunning) { watcher.RequestSnapshot(); return; }
            consecutiveFailures = 0;
            nextConnectAt = DateTime.MinValue;
            await ConnectAsync();
        }
        private void ScheduleReconnect()
        {
            consecutiveFailures = Math.Min(5, consecutiveFailures + 1);
            nextConnectAt = DateTime.UtcNow.AddSeconds(Math.Min(30, 2 * Math.Pow(2, consecutiveFailures - 1)));
        }
        private void Receive()
        {
            if (exiting || watcher == null) return;
            string text, latest = null;
            bool accountChanged = false;
            while (watcher.Updates.TryDequeue(out text)) {
                if (text.Contains("\"monitorAccountChanged\":true")) accountChanged = true;
                latest = text;
            }
            if (latest != null) {
                try {
                    if (accountChanged) {
#if !TRIAL
                        observationScope++;
#endif
                        latestData = null; lastRead = null; ApplyMetric("Current", null); ApplyMetric("Weekly", null); UpdateCountdown(); }
                    if(!latest.Contains("\"monitorAccountChanged\":true"))ApplyData(Provider.Parse(latest));
                }
                catch { SetState("Error", "ข้อมูลเครดิตไม่ถูกต้อง · เก็บค่าครั้งล่าสุดไว้"); Log("parse", "SnapshotInvalid"); }
            }
            if (!String.IsNullOrEmpty(watcher.LastError)) {
                if (!watcher.IsRunning) ScheduleReconnect();
                string error = watcher.LastError;
                SetState(error == "LoginRequired" || error=="WebLoginRequired" || error=="WebPasskeyStep" || error=="WebSignInUnsupported" ? "LoginRequired" : error == "UnsupportedAccount" ? "UnsupportedAccount" : Product.IsGemini&&(error=="WebReadUnavailable"||error=="WebUsageUnavailable"||error=="WebNavigationSlow") ? "Retrying" : watcher.IsRunning ? "Error" : "Offline",
                    error.StartsWith("Web",StringComparison.Ordinal) ? BackgroundReadingDetail(error) : error == "SnapshotMissing" ? "ไฟล์ข้อมูลหายไป · รอข้อมูลใหม่" : error == "SnapshotInvalid" ? "ไฟล์ข้อมูลเสียหรืออ่านไม่ได้ · เก็บค่าครั้งล่าสุดไว้" : error == "LoginRequired" ? Product.IsGemini ? "ลงชื่อเข้าใช้ Gemini CLI ด้วย Google แล้วรีเฟรช" : "ลงชื่อเข้าใช้ Codex ด้วยบัญชี ChatGPT แล้วลองอีกครั้ง" : error == "UnsupportedAccount" ? Product.IsGemini ? "ยังอ่านบัญชีนี้ไม่ได้ · รองรับ Google login ที่มีไฟล์ OAuth ของ Gemini CLI" : "บัญชีนี้ไม่ส่งเครดิต Codex แบบ ChatGPT" : error=="RateLimited" ? "Google จำกัดความถี่ · รอรีเฟรชรอบถัดไป" : "ยังอ่านเครดิตไม่ได้ · กำลังลองอีกครั้ง");
                Log("reader", error == "SnapshotMissing" || error == "SnapshotInvalid" || error == "LoginRequired" || error == "UnsupportedAccount" ? error : "ReaderDisconnected");
            }
            UpdateDiagnostics();
        }

        public void ApplyData(UsageData data)
        {
            if (!AcceptCommercialData(data)) return;
            bool firstQuota = latestData == null && data.HasQuota;
            consecutiveFailures = 0; nextConnectAt = DateTime.MinValue;
            latestData = data;
            ApplyQuotaLabels();
            ApplyMetric("Current", data.Current); ApplyMetric("Weekly", data.Weekly);
            lastRead = data.ReadAt; UpdateCountdown(); UpdateHealth(); WriteStatus();
            if (firstQuota) StartupProfile.Mark("first_quota_applied", Product.Channel);
            if (!Program.HubWorker) {
                SetTooltip(data.CurrentLabel + ": " + Describe(data.Current) + "\n" + data.WeeklyLabel + ": " + Describe(data.Weekly)
                    + "\nแหล่งข้อมูล: " + data.Source + "\nรับล่าสุด " + data.ReadAt.ToString("dd/MM/yyyy HH:mm:ss")
                    + (Product.IsGemini ? preferences.GeminiSource=="CLI" ? "\nเครดิต Gemini CLI ตามโมเดล · อ่าน Google โดยไม่ส่ง prompt\nแสดง bucket ที่เหลือน้อยสุดในแต่ละตระกูลโมเดล" : "\nGemini · อ่านเบื้องหลัง ไม่ต้องเปิด Chrome/Edge\nรีเฟรชทุก " + preferences.GeminiRefreshSeconds + " วินาที · บัญชีอยู่ในโปรไฟล์ของโปรแกรม" : Product.IsClaude ? "\nเวลาเป็นเวลารับ statusLine ในเครื่อง\nอ่านไฟล์ซ้ำไม่ดึงเครดิตใหม่จากเซิร์ฟเวอร์" : "\nเครดิต Codex ของบัญชี ChatGPT\nเครดิตการแชทบนเว็บไม่ได้รวมอยู่ในข้อมูลนี้"));
                UpdateResetTooltips(); UpdateEdgeQuota();
            }
            RecordHistory(data); CheckAlerts(data); UpdateDiagnostics();
        }
        private string PrimaryLabel { get {return latestData!=null ? latestData.CurrentLabel : Product.IsGemini && preferences.GeminiSource=="CLI" ? "Pro" : "5h";} }
        private string SecondaryLabel { get {return latestData!=null ? latestData.WeeklyLabel : Product.IsGemini && preferences.GeminiSource=="CLI" ? "Flash" : "Weekly";} }
        private void ApplyQuotaLabels()
        {
            if(!Product.IsGemini)return;
            if (!Program.HubWorker) {
                foreach(string prefix in new[]{"","Compact","Wide","Row","Tall"}) {Control<TextBlock>(prefix+"CurrentTitle").Text=PrimaryLabel;Control<TextBlock>(prefix+"WeeklyTitle").Text=SecondaryLabel;}
                Control<TextBlock>("HeaderSubtitle").Text=(Product.IsGemini ? preferences.GeminiSource=="CLI" ? "CLI" : "Apps" : Product.Channel)+" · เหลือ";
            }
            UpdateSettingsSummary();
        }
        private async Task SelectGeminiSource(string source)
        {
            if(source!="CLI" && source!="Background")return;
            if(preferences.GeminiSource==source && watcher!=null)return;
            preferences.GeminiSource=source;SaveSettings();
            if(watcher!=null){watcher.Dispose();watcher=null;}latestData=null;lastRead=null;
            ApplyMetric("Current",null);ApplyMetric("Weekly",null);ApplyQuotaLabels();UpdateCountdown();CheckSetupAsync();await RefreshAsync();
        }
        private void SetGeminiRefreshInterval(int seconds)
        {
            preferences.GeminiRefreshSeconds=seconds==30 || seconds==60 ? seconds : 15;
            SaveSettings();
#if GEMINI
            var background=watcher as GeminiBackgroundUsageClient;
            if(background!=null)background.SetRefreshInterval(preferences.GeminiRefreshSeconds);
#endif
            UpdateDiagnostics();
        }
        private async Task OpenGeminiBackgroundConnection()
        {
#if GEMINI
            await RefreshAsync();
            var background=watcher as GeminiBackgroundUsageClient;
            if(background!=null)await background.ShowConnectionAsync();
            else MessageBox.Show(settingsWindow,"เริ่มอ่านก่อนเชื่อมบัญชี · ตรวจปุ่ม Pause และสิทธิ์ใช้งาน","Gemini");
#else
            await Task.FromResult(0);
#endif
        }
        private static string BackgroundInspect()
        {
#if GEMINI
            return GeminiBackgroundUsageClient.Inspect();
#else
            return "";
#endif
        }
        private static string BackgroundReadingDetail(string error)
        {
            return error=="WebPasskeyStep" ? "Google รอขั้นตอน passkey · เปิดหน้าต่างเชื่อมแล้วเลือก Not now หากไม่ต้องการสร้าง" : error=="WebSignInUnsupported" ? "Google ปฏิเสธ login ในตัวอ่าน · โหมดเบื้องหลังยังใช้ไม่ได้" : error=="WebNavigationBlocked" ? "หน้าเชื่อมพาไปลิงก์ที่ยังไม่รองรับ · เปิดหน้าต่างเชื่อมแล้วลองใหม่" : error=="WebNavigationSlow" ? "Google โหลดเกิน 30 วินาที · เปิดหน้าต่างเชื่อมแล้วกดลองโหลดใหม่" : error=="WebUsageUnavailable" ? "ไม่พบเครดิตบนหน้า Usage · เปิดหน้าต่างเชื่อมเพื่อตรวจสอบ" : error=="WebRuntimeMissing" ? "ติดตั้ง Microsoft Edge WebView2 Runtime แล้วเปิดโปรแกรมใหม่" : error=="WebLoginRequired" ? "เปิดตั้งค่า → เชื่อมบัญชี Gemini · ลงชื่อเข้าใช้ในหน้าต่างโปรแกรม" : error=="WebUsageWaiting" ? "กำลังอ่านหน้า Usage · หากโหลดเกิน 30 วินาทีจะแจ้งปัญหา" : "ตัวอ่านเว็บยังอ่านไม่ได้ · ตรวจอินเทอร์เน็ตหรือเปิดหน้าต่างเชื่อมบัญชี";
        }
        private string Describe(Metric metric) { return metric == null ? "ยังอ่านค่าไม่ได้" : metric.Remaining.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "% remaining · " + GetResetTooltip(metric) + (String.IsNullOrEmpty(metric.Model) ? "" : " · " + metric.Model); }
        public static SolidColorBrush RemainingBrush(double? percent)
        {
            string color = !percent.HasValue ? "#94A3B8" : percent >= 60 ? "#4ADE80" : percent >= 25 ? "#FBBF24" : "#FB7185";
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush;
        }

        private void PaintMetric(string prefix, SolidColorBrush brush)
        {
            if (Program.HubWorker) return;
            if (ThemeCatalog.Find(preferences.Theme).Light) {
                string rgb = brush.Color.ToString();
                brush = ColorBrush(rgb == "#FF4ADE80" ? ThemeCatalog.Find(preferences.Theme).MetricColor(80) : rgb == "#FFFBBF24" ? ThemeCatalog.Find(preferences.Theme).MetricColor(40) : rgb == "#FFFB7185" ? ThemeCatalog.Find(preferences.Theme).MetricColor(10) : ThemeCatalog.Find(preferences.Theme).Muted);
            }
            Control<ProgressBar>(prefix + "Progress").Foreground = preferences.Theme == "Minecraft" ? (Brush)PixelFill(brush.Color, false) : brush;
            Control<TextBlock>(prefix + "Value").Foreground = brush;
            var background = brush.Clone(); background.Opacity = 0.12;
            var outline = brush.Clone(); outline.Opacity = 0.24;
            Control<Border>(prefix + "Badge").Background = background;
            Control<Border>(prefix + "Badge").BorderBrush = outline;
        }
        private void ApplyMetric(string prefix, Metric metric)
        {
            if (Program.HubWorker) return;
            Control<ProgressBar>(prefix + "Progress").Value = metric == null ? 0 : metric.Remaining;
            Control<TextBlock>(prefix + "Value").Text = metric == null ? "--" : metric.Display;
            Control<TextBlock>(prefix + "Reset").Text = GetResetText(metric);
            PaintMetric(prefix, RemainingBrush(metric == null ? (double?)null : metric.Remaining));
        }
        private void UpdateCountdown()
        {
            if (latestData != null && QuotaFormatting.Expire(latestData, DateTime.UtcNow)) {
                ApplyMetric("Current", latestData.Current); ApplyMetric("Weekly", latestData.Weekly);
                UpdateHealth(); WriteStatus();
                if (watcher != null && (!Product.AccountLicensed||CommercialAllowed)) watcher.RequestSnapshot();
            }
            if (Program.HubWorker) return;
            Metric current = latestData == null ? null : latestData.Current;
            Metric weekly = latestData == null ? null : latestData.Weekly;
            foreach (string prefix in new[] { "", "Compact", "Wide", "Tall", "Row" }) {
                Control<TextBlock>(prefix + "CurrentReset").Text = prefix == "Row" ? GetRowResetText(current) : GetResetText(current);
                Control<TextBlock>(prefix + "WeeklyReset").Text = prefix == "Row" ? GetRowResetText(weekly) : GetResetText(weekly);
            }
            UpdateResetTooltips(); UpdateEdgeQuota();
        }

        public void SetState(string state, string message)
        {
            State = state;
            string label = StateLabel(state);
            if (!Program.HubWorker) {
                foreach (string name in new[] { "ConnectionDot" }) {
                    var dot = Control<Ellipse>(name);
                    dot.Fill = StatusBrush(state);
                    dot.ToolTip = label;
                }
                if (state != "Live" && state != "Partial") {
                    PaintMetric("Current", RemainingBrush(null)); PaintMetric("Weekly", RemainingBrush(null));
                }
            }
            UpdateAppearanceStatus(message);
            if (!Program.HubWorker) UpdateEdgeQuota();
            if (tray != null) tray.Text = (Product.Label + " · " + label).Substring(0, Math.Min(63, (Product.Label + " · " + label).Length));
            WriteStatus();
        }
        private void SetTooltip(string text)
        {
            if (Program.HubWorker) return;
            foreach (string name in new[] { "RootCard","WideCurrentTitle","WideCurrentProgress","WideCurrentValue","WideCurrentBadge","WideCurrentReset","WideWeeklyTitle","WideWeeklyProgress","WideWeeklyValue","WideWeeklyBadge","WideWeeklyReset","RowCurrentProgress","RowWeeklyProgress","RowCurrentValue","RowWeeklyValue","HeaderTitle","CurrentTitle","CurrentProgress","CurrentValue","CurrentBadge","CurrentReset","WeeklyTitle","WeeklyProgress","WeeklyValue","WeeklyBadge","WeeklyReset","StatusLabel","CompactCurrentTitle","CompactCurrentProgress","CompactCurrentValue","CompactCurrentBadge","CompactCurrentReset","CompactWeeklyTitle","CompactWeeklyProgress","CompactWeeklyValue","CompactWeeklyBadge","CompactWeeklyReset" }) {
                if (Control<FrameworkElement>(name) != null) Control<FrameworkElement>(name).ToolTip = new ToolTip { FontFamily = font, Padding = new Thickness(10), Content = new TextBlock { Text = text, FontFamily = font, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
            }
        }
        public void SetMode(bool compact, bool save) { SetSize(compact ? 300 : 360, compact ? 126 : 210, save); }
        private void PositionWindow()
        {
            if (Program.HubWorker) return;
            Rect area = SystemParameters.WorkArea;
            double left = preferences.Left ?? (area.Right - Window.Width - 14);
            double top = preferences.Top ?? (area.Bottom - Window.Height - 14);
            // Use the saved monitor's work area when a presentation source is available.
            var presentation = PresentationSource.FromVisual(Window);
            if (presentation != null && presentation.CompositionTarget != null) {
                Matrix toDevice = presentation.CompositionTarget.TransformToDevice;
                Matrix fromDevice = presentation.CompositionTarget.TransformFromDevice;
                Point screenPoint = toDevice.Transform(new Point(left + Window.Width / 2, top + Window.Height / 2));
                var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));
                var bounds = screen.WorkingArea;
                Point origin = fromDevice.Transform(new Point(bounds.Left, bounds.Top));
                Point end = fromDevice.Transform(new Point(bounds.Right, bounds.Bottom));
                area = new Rect(origin, end);
            }
            Window.Width = Math.Max(Window.MinWidth, Math.Min(Window.Width, area.Width));
            Window.Height = Math.Max(Window.MinHeight, Math.Min(Window.Height, area.Height));
            Window.Left = Math.Max(area.Left, Math.Min(left, area.Right - Window.Width));
            Window.Top = Math.Max(area.Top, Math.Min(top, area.Bottom - Window.Height));
        }
        public void Hide() { CancelEdgeTransition(); preferences.Hidden = true; edgeDelay.Stop(); if (settingsWindow != null) settingsWindow.Close(); if (edgeWindow != null) edgeWindow.Hide(); edgeHidden = false; Window.Hide(); SaveSettings(); }
        public void Show() { if (exiting) return; if(Program.HubWorker){try{using(var signal=EventWaitHandle.OpenExisting("Local\\CreditMonitorFirmImpulse-"+WindowsIdentity.GetCurrent().User.Value+"-Show"))signal.Set();}catch{}return;} preferences.Hidden = false; RevealFromEdge(); Window.Activate(); SaveSettings(); ScheduleEdgeHide(); }
        public void Start(bool fromStartup)
        {
            if(Program.HubWorker){
                reconnect.Start();countdown.Start();health.Start();
                Window.Dispatcher.BeginInvoke(new Action(async()=>{
                    if (Product.Commercial) await CheckLicenseAsync(false);
                    await ConnectAsync();
                }));
                return;
            }
            if (!fromStartup) preferences.Hidden = false;
            Window.Show();
            PositionWindow();
            if (fromStartup && preferences.Hidden) Window.Hide();
            reconnect.Start(); countdown.Start(); health.Start();
            if (preferences.DockSide != "None" && !preferences.Hidden) ScheduleEdgeHide();
            Window.Dispatcher.BeginInvoke(new Action(async () => {
                if (Product.Commercial) await CheckLicenseAsync(false);
                await ConnectAsync();
                if (!fromStartup && !preferences.OnboardingViewed) OpenSettings();
            }));
        }
        public void Exit() { if (exiting) return; CancelEdgeTransition(); exiting = true; SaveSettings(); Window.Close(); }
        private void OnClosing(object sender, CancelEventArgs args)
        {
            if (!exiting) { args.Cancel = true; Hide(); return; }
            Dispose();
        }
        private void ReadSettings()
        {
            try {
                if (File.Exists(settingsPath)) {
                    string json = File.ReadAllText(settingsPath);
                    var serializer = new JavaScriptSerializer();
                    preferences = serializer.Deserialize<Preferences>(json) ?? new Preferences();
                    var fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
                    if (fields != null && !fields.ContainsKey("EdgeSide") && (preferences.DockSide == "Left" || preferences.DockSide == "Right")) preferences.EdgeSide = preferences.DockSide;
                }
            }
            catch { preferences = new Preferences(); }
            if(Product.IsGemini && preferences.GeminiRefreshSeconds!=15 && preferences.GeminiRefreshSeconds!=30 && preferences.GeminiRefreshSeconds!=60){preferences.GeminiRefreshSeconds=15;geminiSourceMigrated=true;}
            if (Product.IsGemini && preferences.GeminiSource != "CLI" && preferences.GeminiSource != "Background") {
                preferences.GeminiSource = "Background";
                geminiSourceMigrated = true;
            }
            if (preferences.Left.HasValue && (Double.IsNaN(preferences.Left.Value) || Double.IsInfinity(preferences.Left.Value))) preferences.Left = null;
            if (preferences.Top.HasValue && (Double.IsNaN(preferences.Top.Value) || Double.IsInfinity(preferences.Top.Value))) preferences.Top = null;
        }
        public void SaveSettings()
        {
            preferences.Topmost = Window.Topmost;
            preferences.Width = Window.Width; preferences.Height = Window.Height;
            preferences.Left = Window.Left;
            preferences.Top = Window.Top;
            try {
                Directory.CreateDirectory(storage);
                string temporary = settingsPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(preferences), new System.Text.UTF8Encoding(false));
                if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, null); else File.Move(temporary, settingsPath);
            } catch (Exception error) { Log("settings", error.Message); }
        }
        private void WriteStatus()
        {
            if (verification) return;
            try {
                Directory.CreateDirectory(storage);
                ClaudeBridge.AtomicWrite(Path.Combine(storage, "status.json"), new JavaScriptSerializer().Serialize(new {
                    schema=2, flavor=Product.IsGemini?"Gemini":Product.IsClaude?"Claude":"ChatGPT", commercial=Product.Commercial, hubWorker=Program.HubWorker, processId=Process.GetCurrentProcess().Id,
                    version = Version, state = State, source = latestData == null ? (Product.IsGemini ? (preferences.GeminiSource=="CLI" ? "Gemini CLI · Code Assist" : "Gemini Apps · หน้า Usage") : Product.Source) : latestData.Source, capability = latestData == null ? "Unknown" : latestData.Capability, lastReceived = lastRead.HasValue ? lastRead.Value.ToUniversalTime().ToString("o") : null,
#if !TRIAL
                    sourceKey=latestData==null?null:latestData.SourceKey,observationScope=observationScope,
#endif
                    detail=stateDetail, paused=preferences.MonitoringPaused, freshAvailable=HubFreshData(), primaryLabel=PrimaryLabel, secondaryLabel=SecondaryLabel,
                    primary=CommercialAllowed?SafeHubMetric(latestData==null?null:latestData.Current):null, secondary=CommercialAllowed?SafeHubMetric(latestData==null?null:latestData.Weekly):null, refreshSeconds=preferences.GeminiRefreshSeconds,
                    updatedAt = DateTime.UtcNow.ToString("o")
                }));
            } catch { }
        }
        private void Log(string kind, string message)
        {
            if (verification) return;
            try {
                Directory.CreateDirectory(storage);
                string file = Path.Combine(storage, "app.log");
                if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) { File.Copy(file, Path.Combine(storage, "app.previous.log"), true); File.WriteAllText(file, ""); }
                string clean = (message ?? "").Replace('\r',' ').Replace('\n',' ');
                File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " " + kind + " " + clean.Substring(0, Math.Min(clean.Length, 300)) + Environment.NewLine);
            } catch { }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DisposeHubWorker();
            exiting = true;
            if(Product.AccountLicensed)Microsoft.Win32.SystemEvents.PowerModeChanged-=TrialPowerChanged;reconnect.Stop(); countdown.Stop(); licenseTimer.Stop(); DisposeAppearance(); DisposeExperience();
            if (watcher != null) watcher.Dispose();
            if (wakeSignal != null) wakeSignal.Dispose();
            if (stopSignal != null) stopSignal.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayMenu != null) trayMenu.Dispose();
            if (trayFont != null) trayFont.Dispose();
            if (trayFonts != null) trayFonts.Dispose();
            if (nativeTrayFont != null) nativeTrayFont.Dispose();
            if (appIcon != null) appIcon.Dispose();
        }
    }

    public static class Program
    {
        public static bool HubWorker;
        public static int HubParent;
        public static readonly string InstanceName = "Local\\" + Product.StorageName + "-" + WindowsIdentity.GetCurrent().User.Value;
        [STAThread]
        public static int Main(string[] args)
        {
            StartupProfile.Mark("worker_entry", String.Join(" ", args));
            HubWorker=args.Contains("--hub-worker");
            int parentIndex=Array.IndexOf(args,"--hub-parent");if(parentIndex>=0&&parentIndex+1<args.Length)Int32.TryParse(args[parentIndex+1],out HubParent);
            if (args.Contains("--claude-statusline")) return ClaudeBridge.Run();
            if (args.Contains("--uninstall")) return Lifecycle.Uninstall(false);
            if (args.Contains("--finish-uninstall")) return Lifecycle.FinishUninstall();
            using (var mutex = new Mutex(false, InstanceName)) {
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) {
                    if(!HubWorker)try { using (var signal = EventWaitHandle.OpenExisting(InstanceName + "-Show")) signal.Set(); } catch { }
                    return 0;
                }
                try {
                    var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    string storage = Product.Storage;
                    using (var widget = new Widget(storage, false)) {
                        application.MainWindow = widget.Window;
                        widget.Start(args.Contains("--startup"));
                        application.Run();
                    }
                    return 0;
                }
                catch (Exception error) {
                    MessageBox.Show("เปิดโปรแกรมไม่สำเร็จ\n" + error.Message + "\nลองติดตั้งโปรแกรมอีกครั้ง", Product.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
