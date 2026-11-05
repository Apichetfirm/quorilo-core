using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace CreditMonitor
{
    public sealed partial class Widget
    {
        private TextBlock settingsStateText, settingsStateDetail, settingsReceived, settingsFiveHour, settingsWeekly, settingsSnoozeText, settingsSetupText;
        private Button settingsPauseButton, settingsPinButton;
        private CheckBox settingsPrimaryAlert,settingsSecondaryAlert;
        private Grid settingsQuotaGrid;
        private WrapPanel settingsReadingActions;
        private TabControl settingsTabs;
        private UniformGrid settingsThemeGrid;

        private void ApplySettingsPalette()
        {
            if (settingsWindow == null) return;
            bool light = ThemeCatalog.Find(Theme).Light;
            string[] keys = { "Background", "Card", "Control", "Nav", "Selected", "Border", "Text", "Muted", "Accent", "OnAccent", "Good", "Warning", "Bad" };
            string[] colors = light
                ? new[] { "#F1F5FA", "#FFFFFF", "#F0F4FA", "#E4EBF5", "#FFFFFF", "#CFD9E7", "#17263B", "#4D607A", "#245CC5", "#FFFFFF", "#137A49", "#9B6500", "#B52547" }
                : new[] { "#101925", "#182536", "#22344A", "#152233", "#2A4261", "#34475F", "#F1F5FC", "#B5C3D8", "#8ABEFF", "#11233B", "#6AE5A0", "#FFD17A", "#FF93AB" };
            for (int i = 0; i < keys.Length; i++) settingsWindow.Resources["Settings" + keys[i] + "Brush"] = ColorBrush(colors[i]);
            if (SystemParameters.HighContrast) {
                foreach (string key in new[] { "Background", "Card", "Nav" }) settingsWindow.Resources["Settings" + key + "Brush"] = SystemColors.WindowBrush;
                foreach (string key in new[] { "Text", "Muted", "Good", "Warning", "Bad" }) settingsWindow.Resources["Settings" + key + "Brush"] = SystemColors.WindowTextBrush;
                settingsWindow.Resources["SettingsBorderBrush"] = SystemColors.WindowTextBrush;
                settingsWindow.Resources["SettingsControlBrush"] = SystemColors.ControlBrush;
                settingsWindow.Resources["SettingsSelectedBrush"] = SystemColors.ControlBrush;
                settingsWindow.Resources["SettingsAccentBrush"] = SystemColors.HighlightBrush;
                settingsWindow.Resources["SettingsOnAccentBrush"] = SystemColors.HighlightTextBrush;
            }
            UpdateSettingsSummary();
        }
        private StackPanel Page(TabControl tabs, string label)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            tabs.Items.Add(new TabItem { Header = label, Content = scroll });
            return panel;
        }
        private TextBlock Text(Panel panel, string value, bool heading = false)
        {
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = heading ? 18 : 14,
                LineHeight = heading ? 29 : 24, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, heading ? 12 : 10) };
            text.SetResourceReference(TextBlock.ForegroundProperty, heading ? "SettingsTextBrush" : "SettingsMutedBrush");
            panel.Children.Add(text); return text;
        }
        private void PageTitle(Panel panel, string title, string description)
        {
            var heading = Text(panel, title, true); heading.FontSize = 23; heading.LineHeight = 38; heading.Margin = new Thickness(0, 0, 0, 3);
            var subtitle = Text(panel, description); subtitle.Margin = new Thickness(0, 0, 0, 20);
        }
        private StackPanel Section(Panel panel, string title, string description = null)
        {
            var body = new StackPanel();
            var card = new Border { Child = body, Padding = new Thickness(20, 16, 20, 16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16) };
            card.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
            panel.Children.Add(card); Text(body, title, true);
            if (!String.IsNullOrEmpty(description)) Text(body, description);
            return body;
        }
        private WrapPanel Actions(Panel panel)
        { var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) }; panel.Children.Add(row); return row; }
        private Button ActionButton(Panel panel, string label, Action action)
        {
            var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 3, 10, 7), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, label); button.Click += (s, e) => action(); panel.Children.Add(button); return button;
        }
        private StackPanel More(Panel panel, string label)
        {
            var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var expander = new Expander { Header = label, Content = body, FontSize = 14, Margin = new Thickness(0, 4, 0, 14) };
            expander.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SettingsMutedBrush");
            panel.Children.Add(expander); return body;
        }
        private Button ReadingIcon(Panel panel, string glyph, string label, Action action)
        {
            var button = ActionButton(panel, label, action);
            button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18 };
            button.Width = 44; button.Padding = new Thickness(10); button.ToolTip = label;
            return button;
        }
        private void Primary(Button button)
        { button.SetResourceReference(FrameworkElement.StyleProperty, "SettingsPrimaryButton"); }
        private CheckBox Check(Panel panel, string label, bool initial, Action<bool> change)
        {
            var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, LineHeight = 27 }, IsChecked = initial, Margin = new Thickness(0, 7, 0, 11) };
            AutomationProperties.SetName(check, label);
            check.Checked += (s, e) => { change(true); SaveSettings(); }; check.Unchecked += (s, e) => { change(false); SaveSettings(); }; panel.Children.Add(check); return check;
        }
        private TextBlock QuotaSummary(Grid grid, int column, string label)
        {
            var body = new StackPanel();
            var tile = new Border { Child = body, Padding = new Thickness(14, 8, 14, 10), CornerRadius = new CornerRadius(8), Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 14) };
            tile.SetResourceReference(Border.BackgroundProperty, "SettingsControlBrush"); Grid.SetColumn(tile, column); grid.Children.Add(tile);
            var title = Text(body, label); title.FontSize = 14; title.LineHeight = 23; title.Margin = new Thickness(0);
            var value = Text(body, "--", true); value.FontSize = 26; value.LineHeight = 38; value.Margin = new Thickness(0); return value;
        }
        private void UpdateSettingsSummary()
        {
            if (settingsWindow == null || settingsStateText == null) return;
            settingsStateText.Text = StateLabel(State);
            settingsStateText.SetResourceReference(TextBlock.ForegroundProperty, State == "Live" ? "SettingsGoodBrush" : State == "Partial" || State == "Stale" || State == "Connecting" ? "SettingsWarningBrush" : "SettingsMutedBrush");
            settingsStateDetail.Text = stateDetail ?? "";
            if(settingsPrimaryAlert!=null)settingsPrimaryAlert.Content="แจ้งเตือนเครดิต "+PrimaryLabel;if(settingsSecondaryAlert!=null)settingsSecondaryAlert.Content="แจ้งเตือนเครดิต "+SecondaryLabel;
            foreach(var entry in new[]{new KeyValuePair<TextBlock,string>(settingsFiveHour,PrimaryLabel),new KeyValuePair<TextBlock,string>(settingsWeekly,SecondaryLabel)}) {var panel=entry.Key==null ? null : entry.Key.Parent as StackPanel;if(panel!=null)((TextBlock)panel.Children[0]).Text=entry.Value+" · คงเหลือ";}
            settingsStateDetail.Visibility = State == "Live" && (stateDetail == "รับข้อมูลเครดิตแล้ว" || String.IsNullOrEmpty(stateDetail)) ? Visibility.Collapsed : Visibility.Visible;
            settingsReceived.Text = "รับเครดิตล่าสุด: " + (lastRead.HasValue ? lastRead.Value.ToString("dd/MM/yyyy · HH:mm:ss") : "ยังไม่ได้รับข้อมูล");
            bool current = State == "Live" || State == "Partial";
            foreach (var entry in new[] { new KeyValuePair<TextBlock, Metric>(settingsFiveHour, latestData == null ? null : latestData.Current), new KeyValuePair<TextBlock, Metric>(settingsWeekly, latestData == null ? null : latestData.Weekly) }) {
                entry.Key.Text = entry.Value == null ? "--" : entry.Value.Display;
                entry.Key.SetResourceReference(TextBlock.ForegroundProperty, !current || entry.Value == null ? "SettingsMutedBrush" : entry.Value.Remaining >= 60 ? "SettingsGoodBrush" : entry.Value.Remaining >= 25 ? "SettingsWarningBrush" : "SettingsBadBrush");
                entry.Key.ToolTip = GetResetTooltip(entry.Value);
            }
            bool gated = State == "LicenseRequired";
            settingsStateText.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
            settingsStateDetail.Text = gated ? "ยังไม่ได้เปิดสิทธิ์ใช้งาน · ดูหน้าสิทธิ์ใช้งาน" : stateDetail ?? "";
            settingsStateDetail.Visibility = gated || (State != "Live" && !String.IsNullOrEmpty(stateDetail)) ? Visibility.Visible : Visibility.Collapsed;
            if (settingsQuotaGrid != null) settingsQuotaGrid.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
            if (settingsReadingActions != null) settingsReadingActions.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
            settingsReceived.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
            if (connectionText != null) connectionText.Visibility = gated ? Visibility.Collapsed : Visibility.Visible;
            if (settingsPauseButton != null) {
                ((TextBlock)settingsPauseButton.Content).Text = preferences.MonitoringPaused ? "\uE768" : "\uE769";
                settingsPauseButton.ToolTip = preferences.MonitoringPaused ? "Resume · ทำงานต่อ" : "Pause · พักการอ่าน";
                AutomationProperties.SetName(settingsPauseButton, (string)settingsPauseButton.ToolTip);
            }
            if (settingsPinButton != null) SetSettingsButtonLabel(settingsPinButton, Pinned ? "ถอดหมุด · ซ่อนริมจออัตโนมัติ" : "ปักหมุด · ให้วิดเจ็ตอยู่บนจอ");
            if (settingsSnoozeText != null) settingsSnoozeText.Text = preferences.SnoozeUntil.HasValue && preferences.SnoozeUntil > DateTime.Now
                ? "พักแจ้งเตือนชั่วคราวถึง " + preferences.SnoozeUntil.Value.ToString(preferences.TimeFormat == "12H" ? "h:mm tt" : "HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "ไม่ได้พักแจ้งเตือนชั่วคราว";
        }
        private static void SetSettingsButtonLabel(Button button, string label)
        { ((TextBlock)button.Content).Text = label; AutomationProperties.SetName(button, label); }

        public void OpenSettings()
        {
            if (edgeHidden) RevealFromEdge(); CancelEdgeTransition();
            if (edgeWindow != null) edgeWindow.Hide();
            if (settingsWindow != null) { settingsWindow.WindowState = WindowState.Normal; settingsWindow.Show(); settingsWindow.Activate(); return; }
            var previousFocus = Keyboard.FocusedElement;
            edgeDelay.Stop(); preferences.OnboardingViewed = true; SaveSettings();
            Rect area = CurrentWorkArea();
            settingsWindow = new Window { Title = "ตั้งค่า · " + Product.Label, Owner = Window, Width = Math.Min(860, area.Width), Height = Math.Min(820, area.Height),
                MinWidth = Math.Min(620, area.Width), MinHeight = Math.Min(480, area.Height), WindowStartupLocation = WindowStartupLocation.Manual,
                Left = area.Left + Math.Max(0, (area.Width - 860) / 2), Top = area.Top + Math.Max(0, (area.Height - 820) / 2),
                ShowInTaskbar = false, Topmost = Window.Topmost, FontFamily = font, FontSize = 14, Opacity = verification ? 0 : 1 };
            settingsWindow.Resources.MergedDictionaries.Add((ResourceDictionary)CompiledViews.Load("SettingsStyles.xaml"));
            settingsWindow.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SettingsBackgroundBrush");
            settingsWindow.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SettingsTextBrush");
            ApplySettingsPalette();
            var root = new DockPanel { LastChildFill = true };
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var title = Text(header, "ตั้งค่า " + Product.Label, true); title.FontSize = 25; title.LineHeight = 42; title.Margin = new Thickness(0, 0, 0, 2);
            var intro = Text(header, "จัดการการเชื่อมต่อ รูปแบบ และการแจ้งเตือนในที่เดียว"); intro.Margin = new Thickness(0);
            var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var footnotes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) }; footer.Children.Add(footnotes);
            var saved = Text(footnotes, "การเปลี่ยนค่ามีผลทันที · บันทึกอัตโนมัติ"); saved.FontSize = 14; saved.LineHeight = 23; saved.Margin = new Thickness(0);
            var version = Text(footnotes, Product.Name + " · " + Version.Replace("-preview", "") + " Preview"); version.FontSize = 12.5; version.LineHeight = 22; version.Margin = new Thickness(0);
            var close = new Button { Content = "เสร็จแล้ว", IsCancel = true, MinWidth = 108, VerticalAlignment = VerticalAlignment.Center }; Primary(close); Grid.SetColumn(close, 1); footer.Children.Add(close); close.Click += (s, e) => settingsWindow.Close();
            var tabs = new TabControl { Name = "SettingsTabs" }; settingsTabs = tabs; root.Children.Add(tabs);
            settingsWindow.StateChanged += (s, e) => { if (settingsWindow != null && settingsWindow.WindowState == WindowState.Minimized) settingsWindow.Close(); };

            var connection = Page(tabs, "การเชื่อมต่อ"); PageTitle(connection, "การเชื่อมต่อ", "ดูสถานะเครดิตและควบคุมการอ่านข้อมูล");
            var live = Section(connection, "สถานะการอ่านเครดิต");
            settingsStateText = Text(live, "", true); settingsStateText.FontSize = 18; settingsStateText.Margin = new Thickness(0, 0, 0, 3);
            settingsStateDetail = Text(live, "");
            var quota = new Grid(); settingsQuotaGrid = quota; quota.ColumnDefinitions.Add(new ColumnDefinition()); quota.ColumnDefinitions.Add(new ColumnDefinition()); live.Children.Add(quota);
            settingsFiveHour = QuotaSummary(quota, 0, "5h · คงเหลือ"); settingsWeekly = QuotaSummary(quota, 1, "Weekly · คงเหลือ");
            settingsReceived = Text(live, ""); settingsReceived.FontSize = 14; settingsReceived.LineHeight = 24;
            connectionText = Text(live, ""); connectionText.FontSize = 14; connectionText.LineHeight = 24;
            var controls = Actions(live); settingsReadingActions = controls;
            var refresh = ReadingIcon(controls, "\uE72C", "Refresh · รีเฟรชเครดิต", async () => await RefreshAsync()); Primary(refresh);
            settingsPauseButton = ReadingIcon(controls, "\uE769", "Pause · พักการอ่าน", ToggleMonitoring);
            if (Product.IsGemini) {
                Text(live,"เชื่อม Google ครั้งแรก แล้วติดตาม 5h / Weekly เบื้องหลังได้โดยไม่ต้องเปิด Chrome หรือ Edge");
                ActionButton(Actions(live),"เชื่อมบัญชี Gemini",async ()=>{await SelectGeminiSource("Background");await OpenGeminiBackgroundConnection();});
                var refreshRow=new WrapPanel {Margin=new Thickness(0,4,0,0)};live.Children.Add(refreshRow);
                var refreshLabel=Text(refreshRow,"รีเฟรช 5h / Weekly");refreshLabel.FontSize=14;refreshLabel.VerticalAlignment=VerticalAlignment.Center;refreshLabel.Margin=new Thickness(0,0,14,8);
                var intervals=new[]{15,30,60};
                var refreshChoice=new ComboBox {ItemsSource=new[]{"ทุก 15 วินาที","ทุก 30 วินาที","ทุก 60 วินาที"},SelectedIndex=Array.IndexOf(intervals,preferences.GeminiRefreshSeconds),MinWidth=150,Margin=new Thickness(0,0,0,8)};
                AutomationProperties.SetName(refreshChoice,"รอบรีเฟรชเครดิต Gemini 5h และ Weekly");refreshRow.Children.Add(refreshChoice);
                refreshChoice.SelectionChanged+=(s,e)=>{if(refreshChoice.SelectedIndex>=0)SetGeminiRefreshInterval(intervals[refreshChoice.SelectedIndex]);};
                var cliSetup=More(connection,"ตัวเลือกเพิ่มเติม · Gemini CLI");
                Text(cliSetup,"สำหรับผู้ใช้ Gemini CLI: อ่านเครดิต Pro / Flash แยกจาก 5h / Weekly ของ Gemini");
                Text(cliSetup,"ติดตั้ง Gemini CLI และลงชื่อเข้าใช้ด้วย Google ก่อน แล้วกดปุ่มด้านล่าง หากต้องการกลับมาอ่าน 5h / Weekly ให้กด เชื่อมบัญชี Gemini ด้านบน");
                ActionButton(Actions(cliSetup),"ใช้ Gemini CLI",async ()=>await SelectGeminiSource("CLI"));
                var help=More(connection,"ช่วยเชื่อมบัญชี");
                Text(help,"ลงชื่อเข้าใช้ด้วยตัวเองในหน้าต่างโปรแกรม เมื่อรับเครดิตแล้วหน้าต่างจะซ่อนและอ่านต่อทุก 15 / 30 / 60 วินาทีตามที่เลือก ย่อหรือซ่อนริมจอยังอ่านต่อ ปุ่ม Pause พักการอ่านได้ เมื่อเชื่อมต่อมีปัญหาจะเว้นช่วงลองใหม่สูงสุด 5 นาทีและกลับมารอบเดิมเมื่อรับเครดิตได้");
                Text(help,"ต้องมี Microsoft Edge WebView2 Runtime โปรไฟล์ Google เก็บในเครื่อง แยกจาก Chrome / Edge");
                ActionButton(Actions(help),"ติดตั้ง WebView2 Runtime",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/en-us/microsoft-edge/webview2/"){UseShellExecute=true}));
            }
            if (Product.IsClaude) {
                var setup = More(connection, "เชื่อม Claude Code");
                Text(setup, "ใช้ Claude Code กับบัญชีที่ส่งเครดิตได้ · Free Desktop ยังไม่รองรับช่องทางนี้");
                var bridgeActions = Actions(setup); Primary(ActionButton(bridgeActions, "เชื่อม Claude Code", ConnectClaude)); ActionButton(bridgeActions, "หยุดตัวส่ง", DisconnectClaude);
                var advanced = More(setup, "ตัวเลือก session และ statusLine เดิม");
                ActionButton(advanced, "คัดลอกคำสั่งสำหรับ statusLine เดิม", () => { Clipboard.SetText(ClaudeConfiguration.Command(BridgeExecutable)); MessageBox.Show(settingsWindow, "คัดลอกแล้ว · เพิ่มคำสั่งนี้ในสคริปต์เดิม โดยส่ง JSON ต้นฉบับทาง stdin ให้ทั้งสองคำสั่ง", "statusLine เดิม"); });
                var sources = new ComboBox { Margin = new Thickness(0, 0, 0, 4) }; AutomationProperties.SetName(sources, "เลือกแหล่งข้อมูล Claude"); advanced.Children.Add(sources); LoadSources(sources);
            }

            var appearance = Page(tabs, "รูปแบบ"); PageTitle(appearance, "รูปแบบวิดเจ็ต", "ปรับธีม ขนาด และพฤติกรรมให้เหมาะกับหน้าจอ");
            var themes = Section(appearance, "ธีม"); selectedThemeLabel = Text(themes, ""); settingsThemeGrid = new UniformGrid { Columns = 3 }; themes.Children.Add(settingsThemeGrid);
            themeButtons.Clear(); themeChecks.Clear(); foreach (var theme in ThemeCatalog.All) settingsThemeGrid.Children.Add(CreateThemeTile(theme)); UpdateThemeSelection();
            var display = Section(appearance, "ขนาดและการแสดงผล", "ลากขอบวิดเจ็ตเพื่อปรับขนาดได้ หรือเลือกขนาดเริ่มต้นด้านล่าง");
            var sizes = Actions(display); ActionButton(sizes, "ย่อเป็นแถบเล็ก", () => SetMode(true, true)); ActionButton(sizes, "ขนาดปกติ", () => SetMode(false, true));
            ActionButton(sizes, "โหมดทำงาน · แถบเล็กริมจอ", () => { SetMode(true, true); preferences.Topmost = true; Window.Topmost = true; if (Pinned) TogglePinned(); SaveSettings(); });
            ActionButton(sizes, "ซ่อนก่อนแชร์หน้าจอ", Hide);
            Check(display, "อยู่เหนือหน้าต่างอื่น", Window.Topmost, value => { Window.Topmost = value; if (settingsWindow != null) settingsWindow.Topmost = value; });
            Check(display, "เปิดพร้อม Windows", Lifecycle.StartupEnabled, value => { try { Lifecycle.SetStartup(value, typeof(Widget).Assembly.Location); } catch { MessageBox.Show(settingsWindow, "ตั้งค่าเปิดพร้อม Windows ไม่สำเร็จ"); } });
            Check(display, "ให้จุดสถานะหรี่–สว่าง", preferences.AnimateStatus, value => { preferences.AnimateStatus = value; UpdatePulse(); });
            AddResetAndEdgeSettings(appearance);

            var alerts = Page(tabs, "แจ้งเตือน"); PageTitle(alerts, "การแจ้งเตือน", "เตือนเมื่อเครดิตใกล้หมด และพักแจ้งเตือนได้เมื่อไม่ต้องการรบกวน");
            var alertOptions = Section(alerts, "เครดิตที่ต้องการติดตาม"); Check(alertOptions, "เปิดการแจ้งเตือนผ่านถาดระบบ", preferences.AlertsEnabled, value => preferences.AlertsEnabled = value);
            settingsPrimaryAlert=Check(alertOptions, "แจ้งเตือนเครดิต " + PrimaryLabel, preferences.AlertFiveHour, value => preferences.AlertFiveHour = value); settingsSecondaryAlert=Check(alertOptions, "แจ้งเตือนเครดิต " + SecondaryLabel, preferences.AlertWeekly, value => preferences.AlertWeekly = value);
            var level = Section(alerts, "เกณฑ์แจ้งเตือน", "แจ้งเมื่อเปอร์เซ็นต์คงเหลือถึงหรือต่ำกว่าเกณฑ์นี้ เลือกได้ 1–50%");
            var percent = Text(level, preferences.AlertThreshold + "%", true); percent.FontSize = 28; percent.SetResourceReference(TextBlock.ForegroundProperty, "SettingsAccentBrush");
            var threshold = new Slider { Minimum = 1, Maximum = 50, Value = preferences.AlertThreshold, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 0, 0, 4) };
            AutomationProperties.SetName(threshold, "เกณฑ์เครดิตคงเหลือ"); level.Children.Add(threshold); threshold.ValueChanged += (s, e) => { preferences.AlertThreshold = (int)threshold.Value; percent.Text = preferences.AlertThreshold + "%"; SaveSettings(); };
            var quiet = Section(alerts, "เวลาพัก"); Check(quiet, "งดแจ้งเตือนในเวลาพัก", preferences.QuietHours, value => preferences.QuietHours = value);
            var hours = new WrapPanel(); quiet.Children.Add(hours);
            foreach (bool start in new[] { true, false }) {
                var group = new StackPanel { Margin = new Thickness(0, 0, 16, 12) }; Text(group, start ? "เริ่มพัก" : "สิ้นสุดพัก");
                var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00") + ":00").ToArray(), SelectedIndex = start ? preferences.QuietStart : preferences.QuietEnd, Width = 160 };
                AutomationProperties.SetName(combo, start ? "เริ่มเวลาพัก" : "สิ้นสุดเวลาพัก"); combo.SelectionChanged += (s, e) => { if (start) preferences.QuietStart = combo.SelectedIndex; else preferences.QuietEnd = combo.SelectedIndex; SaveSettings(); }; group.Children.Add(combo); hours.Children.Add(group);
            }
            Text(quiet, "ใช้เวลาของเครื่อง หากเริ่มพักและสิ้นสุดพักเท่ากันจะพักทั้งวัน");
            settingsSnoozeText = Text(quiet, "");
            var snooze = Actions(quiet); ActionButton(snooze, "พักแจ้งเตือน 1 ชั่วโมง", () => { preferences.SnoozeUntil = DateTime.Now.AddHours(1); SaveSettings(); UpdateDiagnostics(); }); ActionButton(snooze, "ยกเลิกพักแจ้งเตือน", () => { preferences.SnoozeUntil = null; SaveSettings(); UpdateDiagnostics(); });
            Text(alerts, "แจ้งครั้งเดียวต่อรอบเครดิตและเกณฑ์ ไม่แจ้งจากข้อมูลเก่าหรือข้อมูลทดสอบ การแสดงแจ้งเตือนขึ้นอยู่กับการตั้งค่า Windows");

            var diagnostics = Page(tabs, "ตรวจระบบ"); PageTitle(diagnostics, "ตรวจระบบ", "ดูข้อมูลการทำงานหรือส่งออกเพื่อช่วยตรวจปัญหา");
            var details = Section(diagnostics, "สถานะและเครดิตล่าสุด"); diagnosticText = Text(details, "");
            var advancedTools = More(diagnostics, "เครื่องมือช่วยแก้ปัญหา");
            settingsSetupText = Text(advancedTools, ""); settingsSetupText.FontSize = 14;
            var exportsRow = Actions(advancedTools); ActionButton(exportsRow, "ตรวจใหม่", CheckSetupAsync); ActionButton(exportsRow, "ส่งออกข้อมูลตรวจระบบ", ExportDiagnostics); ActionButton(exportsRow, "ส่งออกประวัติเครดิต", ExportHistory);

            var frame = new Border { Child = root, Padding = new Thickness(24, 20, 24, 16) };
            frame.SetResourceReference(Border.BackgroundProperty, "SettingsBackgroundBrush"); settingsWindow.Content = frame;
            settingsWindow.SizeChanged += (s, e) => {
                if (settingsWindow == null) return;
                if (settingsThemeGrid != null) settingsThemeGrid.Columns = settingsWindow.Width < 760 ? 2 : 3;
                bool small = settingsWindow.Height < 650;
                intro.Visibility = small ? Visibility.Collapsed : Visibility.Visible;
                version.Visibility = settingsWindow.Height < 560 ? Visibility.Collapsed : Visibility.Visible;
                header.Margin = new Thickness(0, 0, 0, small ? 12 : 20);
                frame.Padding = new Thickness(settingsWindow.Width < 760 ? 18 : 24, small ? 14 : 20, settingsWindow.Width < 760 ? 18 : 24, 16);
            };
            settingsWindow.Closed += (s, e) => { settingsWindow = null; connectionText = diagnosticText = selectedThemeLabel = settingsStateText = settingsStateDetail = settingsReceived = settingsFiveHour = settingsWeekly = settingsSnoozeText = settingsSetupText = null; settingsPauseButton = settingsPinButton = null; settingsThemeGrid = null; settingsTabs = null; settingsQuotaGrid = null; settingsReadingActions = null; licenseSummary = licenseDetail = updateSummary = null; licenseDevices = null; themeButtons.Clear(); themeChecks.Clear(); Window.Activate(); var focused = previousFocus as UIElement; if (focused != null && focused.IsVisible) focused.Focus(); else Control<Button>(Layout == "Horizontal" || Layout == "Wide" ? "StripSettingsButton" : "SettingsButton").Focus(); ScheduleEdgeHide(); };
            if (Product.Commercial) AddCommerceSettings(tabs);
            settingsWindow.Show(); tabs.SelectedIndex = 0; ((TabItem)tabs.Items[0]).Focus(); UpdateDiagnostics(); CheckSetupAsync();
        }
        private string SettingsMetricDetail(Metric metric)
        {
            if (metric == null) return "ยังไม่มีข้อมูล";
            return metric.Display + " คงเหลือ · " + QuotaFormatting.ResetLabel(metric) + (metric.ResetAt.HasValue
                ? " | " + metric.ResetAt.Value.ToLocalTime().ToString(preferences.TimeFormat == "12H" ? "dd/MM h:mm tt" : "dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "");
        }
    }
}
