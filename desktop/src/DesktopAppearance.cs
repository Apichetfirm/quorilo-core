using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CreditMonitor
{
    public sealed partial class Widget
    {
        public string Layout { get; private set; }
        public string Theme { get { return preferences.Theme; } }
        public string DockSide { get { return preferences.DockSide; } }
        public bool Pinned { get { return DockSide == "None"; } }
        public bool EdgeHidden { get { return edgeHidden; } }
        private readonly DispatcherTimer saveLayout = new DispatcherTimer();
        private readonly DispatcherTimer health = new DispatcherTimer();
        private readonly DispatcherTimer edgeDelay = new DispatcherTimer();
        private Window edgeWindow, settingsWindow;
        private Border edgeCard;
        private System.Windows.Shapes.Ellipse edgeDot;
        private TextBlock edgeTitle, edgePercent, edgeQuotaLabel;
        private readonly TranslateTransform edgeSlide = new TranslateTransform();
        private HorizontalMotionBlurEffect edgeBlur;
        private Transform edgePreviousTransform;
        private Effect edgePreviousEffect;
        private bool edgeMotionActive, keyboardInteraction;
        private int edgeMotionSerial;
        private readonly Dictionary<string, Button> themeButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, TextBlock> themeChecks = new Dictionary<string, TextBlock>();
        private TextBlock selectedThemeLabel;
        public static string[] ThemeNames { get { return ThemeCatalog.All.Select(p => p.Key).ToArray(); } }
        private bool appearanceReady, resizing, edgeHidden, transitioning;
        private string lastPulseState;
        private string stateDetail;

        private void InitializeAppearance()
        {
            if (!ThemeCatalog.Contains(preferences.Theme)) preferences.Theme = "Original";
            if (preferences.DockSide != "Left" && preferences.DockSide != "Right") preferences.DockSide = "None";
            if (preferences.EdgeSide != "Nearest" && preferences.EdgeSide != "Left" && preferences.EdgeSide != "Right") preferences.EdgeSide = "Nearest";
            if (preferences.TimeFormat != "12H") preferences.TimeFormat = "24H";
            health.Interval = TimeSpan.FromSeconds(2);
            health.Tick += (s, e) => UpdateHealth();
            if (Program.HubWorker) {
                appearanceReady = true;
                return;
            }
            ApplyTheme(preferences.Theme, false);
            Window.Width = ValidSize(preferences.Width, preferences.Compact ? 300 : 360, 220, 1400);
            Window.Height = ValidSize(preferences.Height, preferences.Compact ? 126 : 210, 66, 1000);
            saveLayout.Interval = TimeSpan.FromMilliseconds(350);
            saveLayout.Tick += (s, e) => { saveLayout.Stop(); SaveSettings(); };
            Window.SizeChanged += (s, e) => {
                bool finishCollapse = edgeMotionActive && edgeHidden;
                CancelEdgeTransition();
                if (finishCollapse) Window.Hide();
                UpdateCountdown();
                UpdateLayout();
                if (appearanceReady && !transitioning) { saveLayout.Stop(); saveLayout.Start(); }
            };
            Window.IsVisibleChanged += (s, e) => UpdatePulse();
            Window.LocationChanged += (s, e) => { if (appearanceReady && !transitioning && Window.IsVisible) { saveLayout.Stop(); saveLayout.Start(); } };
            foreach (string edge in new[] { "N", "S", "E", "W", "NE", "NW", "SE", "SW" }) {
                string direction = edge;
                var thumb = Control<Thumb>("Resize" + edge);
                thumb.DragStarted += (s, e) => BeginWindowMove();
                thumb.DragDelta += (s, e) => ResizeBy(direction, e.HorizontalChange, e.VerticalChange);
                thumb.DragCompleted += (s, e) => EndWindowMove();
                thumb.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = false; };
            }
            Control<Button>("SettingsButton").Click += (s, e) => OpenSettings();
            Control<Button>("StripSettingsButton").Click += (s, e) => OpenSettings();
            Control<Button>("StripRefreshButton").Click += async (s, e) => await RefreshAsync();
            Control<Button>("StripHideButton").Click += (s, e) => Hide();
            Control<Button>("PinButton").Click += (s, e) => TogglePinned();
            Control<Button>("MiniPinButton").Click += (s, e) => TogglePinned();
            Window.MouseEnter += (s, e) => { edgeDelay.Stop(); if (edgeHidden && edgeMotionActive) RevealFromEdge(); };
            Window.MouseLeave += (s, e) => ScheduleEdgeHide();
            Window.PreviewKeyDown += (s, e) => { keyboardInteraction = true; edgeDelay.Stop(); };
            Window.PreviewMouseDown += (s, e) => keyboardInteraction = false;
            Window.LostKeyboardFocus += (s, e) => { if (!Window.IsKeyboardFocusWithin) { keyboardInteraction = false; ScheduleEdgeHide(); } };
            Window.ContextMenu.Closed += (s, e) => ScheduleEdgeHide();
            edgeDelay.Interval = TimeSpan.FromMilliseconds(1000);
            edgeDelay.Tick += (s, e) => { edgeDelay.Stop(); if (!Window.IsMouseOver && !resizing && !Window.ContextMenu.IsOpen && settingsWindow == null) CollapseToEdge(); };
            UpdateLayout(); PositionWindow(); appearanceReady = true;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnDesktopPreferenceChanged;
            UpdatePinAppearance();
        }
        private static double ValidSize(double? value, double fallback, double minimum, double maximum)
        { return value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value) ? Math.Max(minimum, Math.Min(maximum, value.Value)) : fallback; }

        public void UpdateLayout()
        {
            if (Program.HubWorker) return;
            double width = Window.Width, height = Window.Height;
            UpdateCountdown();
            // A wide, shallow window needs inline metrics rather than taller compact cards.
            Layout = width >= 680 && height < 230 && width / height >= 4.5 ? "Wide" : height >= 198 && (width < 340 || height > width * 1.12) ? "Tall" : width >= 340 && height >= 198 ? "Full" : width >= 300 && height >= 122 ? "Compact" : "Horizontal";
            preferences.Compact = Layout != "Full";
            Control<Grid>("FullView").Visibility = Layout == "Full" ? Visibility.Visible : Visibility.Collapsed;
            Control<Grid>("CompactView").Visibility = Layout == "Compact" ? Visibility.Visible : Visibility.Collapsed;
            Control<Grid>("TallView").Visibility = Layout == "Tall" ? Visibility.Visible : Visibility.Collapsed;
            Control<Grid>("HorizontalView").Visibility = Layout == "Horizontal" ? Visibility.Visible : Visibility.Collapsed;
            Control<Grid>("WideView").Visibility = Layout == "Wide" ? Visibility.Visible : Visibility.Collapsed;
            bool horizontal = Layout == "Horizontal";
            Window.Resources["CardRadius"] = new CornerRadius(Math.Min(ThemeCatalog.Find(Theme).Radius, height < 198 ? 12 : ThemeCatalog.Find(Theme).Radius));
            bool showRowReset = horizontal && height >= 108;
            bool wide = Layout == "Wide", showHeader = !horizontal && (!wide || height >= 136);
            Control<Grid>("HeaderRow").Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
            double pad = horizontal ? 7 : wide ? 10 : Layout == "Compact" ? 7 : Clamp(height / 16, 10, 16);
            double verticalPad = horizontal ? 5 : wide && !showHeader ? 4 : pad;
            if (showRowReset) verticalPad = 4;
            bool shortCompact = Layout == "Compact" && height < 138;
            if (shortCompact) verticalPad = 4;
            // Keep the minimum strip's content budget when a theme uses a thicker border.
            var cardBorder = Control<Border>("RootCard").BorderThickness;
            if (horizontal || shortCompact || (wide && !showHeader)) verticalPad = Math.Max(2, verticalPad - Math.Max(0, cardBorder.Top + cardBorder.Bottom - 2) / 2);
            Control<Border>("RootCard").Padding = new Thickness(pad, verticalPad, pad, verticalPad);
            double headerHeight = Clamp(Math.Min(width / 12, height * 0.15), shortCompact ? 22 : 28, 44);
            Control<Grid>("HeaderRow").Height = headerHeight;
            Control<TextBlock>("HeaderTitle").FontSize = Clamp(Math.Min(width / 23, headerHeight * 0.59), 15.5, 26);
            Control<TextBlock>("HeaderSubtitle").FontSize = Clamp(Math.Min(width / 45, headerHeight * 0.4), 11, 16);
            Control<TextBlock>("HeaderSubtitle").Visibility = width >= 480 ? Visibility.Visible : Visibility.Collapsed;
            foreach (string name in new[] { "RefreshButton", "SettingsButton", "PinButton", "CompactButton", "HideButton" }) {
                var button = Control<Button>(name);
                button.Width = button.Height = Math.Min(headerHeight, Clamp(width / 16, 24, 34));
                button.FontSize = Clamp(button.Height * 0.66, 16, 23);
            }
            Control<Button>("MiniPinButton").Visibility = !showHeader ? Visibility.Visible : Visibility.Collapsed;
            Control<StackPanel>("StripActions").Visibility = wide && !showHeader ? Visibility.Visible : Visibility.Collapsed;
            var pinView = Control<Button>("PinButton").Content as Viewbox;
            pinView.Width = pinView.Height = Clamp(width / 20, 16, 22);
            Control<Button>("CompactButton").Content = Layout == "Full" ? "↙" : "↗";
            double statusSize = horizontal ? Clamp(Math.Min(width / 29, height / 7), 9.5, 13) : Clamp(Math.Min(width / 31, height / 10), 11, 16);
            if (shortCompact) statusSize = 10;
            double statusHeight = Math.Ceiling(statusSize * 1.6);
            Control<Grid>("StatusRow").Height = statusHeight;
            Control<TextBlock>("StatusLabel").FontSize = statusSize;
            double dotSize = Clamp(statusSize * 0.66, 7, 10);
            Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Width = Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Height = dotSize;
            Control<Grid>("StatusRow").ColumnDefinitions[0].Width = new GridLength(dotSize + 8);
            double borderHeight = Control<Border>("RootCard").BorderThickness.Top + Control<Border>("RootCard").BorderThickness.Bottom;
            double bodyHeight = Math.Max(1, height - 2 * verticalPad - borderHeight - (showHeader ? headerHeight : 0) - statusHeight - 3);
            var reset = Control<TextBlock>("CurrentReset");
            double fullResetSize = Layout == "Full" ? Clamp((width - 2 * pad) / 27, 11, 15) : 9;
            reset.FontSize = fullResetSize;
            reset.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double resetHeight = Math.Ceiling(reset.DesiredSize.Height) - reset.Margin.Top - reset.Margin.Bottom;
            double scale = Clamp(Math.Min((width - 2 * pad) / 330, (bodyHeight / 2 - resetHeight - 12) / 34), 0.75, 2.4);
            double compactScale = Clamp(Math.Min((width - 2 * pad - 12) / 290, (bodyHeight - resetHeight - 5) / 38), 0.7, 2.3);
            double rowSize = Clamp(Math.Min(width / 18, (bodyHeight / 2 - 2) / 1.8), 9.5, 28);
            double rowResetHeight = 0;
            if (showRowReset) {
                var rowReset = Control<TextBlock>("RowCurrentReset");
                rowReset.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                rowResetHeight = Math.Ceiling(rowReset.DesiredSize.Height);
                // Measure even while collapsed: its text still contributes to the next visible layout.
                if (rowResetHeight == 0) rowResetHeight = resetHeight * (height < 122 ? 0.5 : 1);
                rowSize = Clamp(Math.Min(width / 18, (bodyHeight / 2 - rowResetHeight - 3) / 1.75), 9.5, 18);
            }
            double wideSize = Clamp((bodyHeight - resetHeight - 3) / 1.8, 11, 26);
            double metricHeight = bodyHeight / 2 - 10;
            bool portraitHero = metricHeight >= 160;
            double tallSize = portraitHero ? Clamp(Math.Min((width - 2 * pad - 20) / 3.3, (metricHeight - 104) / 1.55), 24, 96) : Clamp(Math.Min((width - 2 * pad) / 11, (metricHeight - resetHeight - 10) / 1.55), 14, 27);
            foreach (string prefix in new[] { "Current", "Weekly" }) {
                Control<TextBlock>(prefix + "Value").FontSize = 20 * scale;
                Control<TextBlock>(prefix + "Title").FontSize = 14 * scale;
                Control<TextBlock>(prefix + "Reset").FontSize = fullResetSize;
                SizeBadge(prefix + "Badge", 76 * scale, 34 * scale);
                SetColumns("Full" + prefix + "Grid", 106 * scale, Math.Max(116, 92 * scale));
                Control<ProgressBar>(prefix + "Progress").Height = 8 * scale;
                double compactBadgeHeight = Clamp(bodyHeight / 2 - resetHeight - 2, 20, 30);
                Control<TextBlock>("Compact" + prefix + "Title").FontSize = Clamp(compactBadgeHeight * 0.57, 12, 15);
                // Right-align labels in the width of Weekly. Both rows then have
                // the same visible label/bar gap and the same bar/badge gap.
                var compactTitle = Control<TextBlock>("Compact" + prefix + "Title");
                var labelSize = new FormattedText("Weekly", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(compactTitle.FontFamily, compactTitle.FontStyle, compactTitle.FontWeight, compactTitle.FontStretch), compactTitle.FontSize, compactTitle.Foreground, VisualTreeHelper.GetDpi(Window).PixelsPerDip);
                Control<Grid>("Compact" + prefix + "Grid").ColumnDefinitions[0].Width = new GridLength(Math.Ceiling(labelSize.WidthIncludingTrailingWhitespace));
                compactTitle.HorizontalAlignment = HorizontalAlignment.Right;
                Control<TextBlock>("Compact" + prefix + "Value").FontSize = Clamp(compactBadgeHeight * 0.68, 14, 20);
                SizeBadge("Compact" + prefix + "Badge", Math.Max(54, compactBadgeHeight * 2.7), compactBadgeHeight);
                Control<ProgressBar>("Compact" + prefix + "Progress").Height = 6 * compactScale;
                Control<TextBlock>("Compact" + prefix + "Reset").Margin = new Thickness(0, 1, 0, 0);
                Control<ProgressBar>("Compact" + prefix + "Progress").Margin = new Thickness(8, 0, 8, 0);
                Control<TextBlock>("Row" + prefix + "Title").FontSize = rowSize;
                Control<TextBlock>("Row" + prefix + "Value").FontSize = rowSize + 1;
                Control<TextBlock>("Row" + prefix + "Reset").Visibility = showRowReset ? Visibility.Visible : Visibility.Collapsed;
                Control<TextBlock>("Row" + prefix + "Value").TextAlignment = showRowReset ? TextAlignment.Center : TextAlignment.Right;
                Control<Grid>("Row" + prefix + "Grid").VerticalAlignment = showRowReset ? VerticalAlignment.Center : VerticalAlignment.Stretch;
                SetColumns("Row" + prefix + "Grid", 5 * rowSize, 4 * rowSize);
                Control<ProgressBar>("Row" + prefix + "Progress").Height = Clamp(rowSize / 2, 6, 12);
                Control<TextBlock>("Wide" + prefix + "Title").FontSize = Clamp(wideSize * 0.8, 12, 19);
                Control<TextBlock>("Wide" + prefix + "Value").FontSize = wideSize;
                Control<TextBlock>("Wide" + prefix + "Reset").FontSize = height >= 136 ? 11 : 9.5;
                SizeBadge("Wide" + prefix + "Badge", Math.Max(46, wideSize * 3.6), wideSize * 1.8);
                SetColumns("Wide" + prefix + "Grid", Clamp(wideSize * 3.6, 54, 76), Math.Max(104, wideSize * 3.8));
                Control<ProgressBar>("Wide" + prefix + "Progress").Height = Clamp(wideSize / 2.5, 6, 10);
                var tallCard = Control<Border>("Tall" + prefix + "Card");
                tallCard.Padding = new Thickness(portraitHero ? 10 : 0);
                tallCard.BorderThickness = new Thickness(portraitHero ? 1 : 0);
                Control<TextBlock>("Tall" + prefix + "Title").FontSize = portraitHero ? Clamp(tallSize * 0.38, 16, 23) : Clamp(tallSize * 0.65, 14, 18);
                Control<TextBlock>("Tall" + prefix + "Value").FontSize = tallSize;
                var tallBadge = Control<Border>("Tall" + prefix + "Badge");
                Grid.SetRow(tallBadge, portraitHero ? 1 : 0);
                tallBadge.HorizontalAlignment = portraitHero ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                tallBadge.Margin = new Thickness(0, portraitHero ? 8 : 0, 0, 0);
                SizeBadge("Tall" + prefix + "Badge", tallSize * 3.1, tallSize * 1.55);
                Control<TextBlock>("Tall" + prefix + "Reset").Margin = new Thickness(0, portraitHero ? 3 : 0, 0, 0);
                var tallReset = Control<TextBlock>("Tall" + prefix + "Reset");
                double tallResetWidth = width - 2 * pad - (portraitHero ? 20 : 0) - 4;
                double preferredResetSize = Clamp(tallResetWidth / 22, 10, 15);
                var resetLine = new FormattedText(tallReset.Text ?? "", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(tallReset.FontFamily, tallReset.FontStyle, tallReset.FontWeight, tallReset.FontStretch), preferredResetSize, tallReset.Foreground, VisualTreeHelper.GetDpi(Window).PixelsPerDip);
                tallReset.FontSize = Math.Max(9, Math.Min(preferredResetSize, preferredResetSize * tallResetWidth / Math.Max(1, resetLine.WidthIncludingTrailingWhitespace)));
                Control<ProgressBar>("Tall" + prefix + "Progress").Height = portraitHero ? Clamp(tallSize / 6, 8, 12) : 6;
                Control<ProgressBar>("Tall" + prefix + "Progress").Margin = new Thickness(0, portraitHero ? 8 : 3, 0, 0);
            }
            Window.UpdateLayout();
        }
        private static double Clamp(double value, double minimum, double maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        private void SizeBadge(string name, double width, double height)
        {
            var badge = Control<Border>(name); badge.Width = width; badge.Height = height;
            badge.CornerRadius = new CornerRadius(Math.Min(height / 2, ThemeCatalog.Find(Theme).BadgeRadiusLimit));
        }
        private void SetColumns(string name, double first, double last)
        {
            var grid = Control<Grid>(name);
            grid.ColumnDefinitions[0].Width = new GridLength(first);
            grid.ColumnDefinitions[2].Width = new GridLength(last);
        }
        public void ResizeBy(string edge, double deltaX, double deltaY)
        {
            Rect area = CurrentWorkArea();
            double left = Window.Left, top = Window.Top, width = Window.Width, height = Window.Height;
            if (edge.Contains("E")) width = Math.Max(220, Math.Min(area.Right - left, width + deltaX));
            if (edge.Contains("S")) height = Math.Max(66, Math.Min(area.Bottom - top, height + deltaY));
            if (edge.Contains("W")) { double next = Math.Max(220, Math.Min(left + width - area.Left, width - deltaX)); left += width - next; width = next; }
            if (edge.Contains("N")) { double next = Math.Max(66, Math.Min(top + height - area.Top, height - deltaY)); top += height - next; height = next; }
            Window.Left = left; Window.Top = top; Window.Width = width; Window.Height = height;
            UpdateLayout();
        }
        public void SetSize(double width, double height, bool save)
        {
            if (edgeHidden) RevealFromEdge();
            Window.Width = Math.Max(220, width); Window.Height = Math.Max(66, height);
            UpdateLayout(); PositionWindow();
            if (save) SaveSettings();
        }
        private static SolidColorBrush ColorBrush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
        private static DrawingBrush PixelFill(Color color, bool surface)
        {
            double size = surface ? 24 : 8;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, size, size))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(surface ? (byte)10 : (byte)60, 0, 0, 0)), null, new RectangleGeometry(new Rect(size - 2, 0, 2, size))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(surface ? (byte)6 : (byte)28, 255, 255, 255)), null, new RectangleGeometry(new Rect(0, 0, size - 2, surface ? 3 : 1))));
            if (surface) group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)), null, new RectangleGeometry(new Rect(6, 10, 6, 5))));
            var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, size, size), Stretch = Stretch.None };
            brush.Freeze(); return brush;
        }
        public void ApplyTheme(string theme, bool save)
        {
            if (!ThemeCatalog.Contains(theme)) return;
            preferences.Theme = theme;
            if (Program.HubWorker) { if (save) SaveSettings(); return; }
            var preset = ThemeCatalog.Find(theme);
            Window.Resources["SurfaceBrush"] = preset.Pixel ? (Brush)PixelFill(Color.FromRgb(38, 46, 36), true) : preset.SurfaceBrush();
            Window.Resources["EdgeBrush"] = ColorBrush(preset.Edge);
            Window.Resources["TextBrush"] = ColorBrush(preset.Text);
            Window.Resources["MutedBrush"] = ColorBrush(preset.Muted);
            Window.Resources["TrackBrush"] = ColorBrush(preset.Track);
            Window.Resources["MetricSurfaceBrush"] = ColorBrush(preset.MetricSurface);
            Window.Resources["MetricEdgeBrush"] = ColorBrush(preset.MetricEdge);
            Window.Resources["HoverBrush"] = ColorBrush(preset.Hover);
            Window.Resources["PressedBrush"] = ColorBrush(preset.Pressed);
            Window.Resources["AccentBrush"] = ColorBrush(preset.Accent);
            Window.Resources["CardRadius"] = new CornerRadius(preset.Radius);
            Window.Resources["BadgeRadius"] = new CornerRadius(Math.Min(15, preset.BadgeRadiusLimit));
            Window.Resources["ButtonRadius"] = new CornerRadius(preset.ButtonRadius);
            Window.Resources["BarRadius"] = new CornerRadius(preset.BarRadius);
            Control<Border>("RootCard").BorderThickness = new Thickness(preset.Pixel ? 3 : 1);
            foreach (string prefix in new[] { "Current", "Weekly" }) {
                foreach (string layout in new[] { "Full", "Tall" }) {
                    var card = Control<Border>(layout + prefix + "Card");
                    if (layout == "Full") {
                        card.Background = preset.Elevated ? ColorBrush(preset.MetricSurface) : Brushes.Transparent;
                        card.Margin = new Thickness(0, preset.Elevated ? 2 : 0, 0, preset.Elevated ? 2 : 0);
                        Control<Grid>("Full" + prefix + "Grid").Margin = new Thickness(0, preset.Elevated ? 2 : 4, 0, preset.Elevated ? 2 : 4);
                    }
                    card.Effect = preset.Elevated ? new DropShadowEffect { Color = Color.FromRgb(67, 44, 95), Opacity = 0.12, BlurRadius = 6, ShadowDepth = 2, Direction = 270 } : null;
                }
            }
            if (latestData != null) {
                ApplyMetric("Current", latestData.Current); ApplyMetric("Weekly", latestData.Weekly);
                if (State != "Live" && State != "Partial") { PaintMetric("Current", RemainingBrush(null)); PaintMetric("Weekly", RemainingBrush(null)); }
            }
            if (Window.ContextMenu != null) {
                Window.ContextMenu.Background = ColorBrush(preset.Light ? preset.Surface : "#182232");
                Window.ContextMenu.Foreground = (Brush)Window.Resources["TextBrush"];
            }
            if (edgeCard != null) { edgeCard.Background = (Brush)Window.Resources["SurfaceBrush"]; edgeCard.BorderBrush = (Brush)Window.Resources["EdgeBrush"]; edgeTitle.Foreground = (Brush)Window.Resources["TextBrush"]; edgeQuotaLabel.Foreground = (Brush)Window.Resources["MutedBrush"]; UpdateEdgeQuota(); }
            if (appearanceReady) { UpdateLayout(); UpdatePinAppearance(); UpdateThemeSelection(); }
            Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Fill = StatusBrush(State);
            if (edgeDot != null) edgeDot.Fill = Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Fill;
            if (save) SaveSettings();
        }
        private SolidColorBrush StatusBrush(string state)
        {
            bool light = ThemeCatalog.Find(Theme).Light;
            return ColorBrush(state == "Live" ? (light ? "#15803D" : "#4ADE80") : state == "Partial" || state == "Connecting" || state == "Stale" ? (light ? "#A16207" : "#FBBF24") : (light ? "#64748B" : "#94A3B8"));
        }
        private static string StateLabel(string state)
        {
            switch (state) {
                case "Live": return "รับข้อมูลแล้ว"; case "Partial": return "ข้อมูลบางส่วน";
                case "Connecting": return "กำลังเชื่อมต่อ"; case "Stale": return "ข้อมูลเก่า";
                case "Paused": return "พักการอ่านข้อมูล"; case "Error": return "อ่านข้อมูลไม่ได้";
                case "MissingCli": return "ไม่พบ CLI"; case "LoginRequired": return "ต้องลงชื่อเข้าใช้";
                case "UnsupportedAccount": return "บัญชีไม่รองรับเครดิตนี้";
                case "ResetPending": return "รอค่ารอบใหม่"; case "Offline": return "การเชื่อมต่อหยุดลง";
                case "LicenseRequired": return "ตรวจสิทธิ์ใช้งาน";
                case "NoQuota": return "ยังไม่ได้รับเครดิต"; default: return "รอข้อมูล";
            }
        }
        private string StatusText()
        {
            if (State == "Live" || State == "Partial") {
                int age = lastRead.HasValue ? (int)Math.Max(0, (DateTime.Now - lastRead.Value).TotalSeconds) : 0;
                return (Product.IsGemini ? preferences.GeminiSource=="CLI" ? "Gemini CLI" : "Gemini Apps" : Product.IsClaude ? "Claude Code" : "Codex") + (State == "Partial" ? " · บางส่วน" : "") + " · รับล่าสุด " + (age >= 60 ? age / 60 + "m" : age + "s");
            }
            return StateLabel(State) + " · ตั้งค่าเพื่อดูรายละเอียด";
        }
        private void UpdateHealth()
        {
            if (!CommercialAllowed) { latestData = null; lastRead = null; ApplyMetric("Current", null); ApplyMetric("Weekly", null); SetState("LicenseRequired", "เปิดตั้งค่า → สิทธิ์ใช้งาน เพื่อดูสถานะ"); return; }
            if (preferences.MonitoringPaused) { SetState("Paused", "พักการอ่านเครดิตไว้ · กดทำงานต่อในตั้งค่า"); }
            else if (watcher != null && !String.IsNullOrEmpty(watcher.LastError)) { /* Preserve an actionable reader failure. */ }
            else if (!verification && watcher == null && (State == "MissingCli" || State == "Error" || State == "Offline")) { }
            else if (latestData != null) {
                bool any = latestData.HasQuota, full = latestData.Current != null && latestData.Weekly != null;
                bool old = lastRead.HasValue && (DateTime.Now - lastRead.Value).TotalSeconds > 90;
                string next = old ? "Stale" : latestData.ResetPending ? "ResetPending" : full ? "Live" : any ? "Partial" : "NoQuota";
                if (State != next) SetState(next, old ? "ข้อมูลเกิน 90 วินาที · แสดงค่าครั้งล่าสุด" : latestData.ResetPending ? "ถึงเวลารีเซ็ตแล้ว · รอข้อมูลยืนยันค่ารอบใหม่" : any ? "รับข้อมูลเครดิตแล้ว" : Product.IsGemini && preferences.GeminiSource=="CLI" ? "Google ยังไม่ส่งเครดิต Pro / Flash ที่รองรับ" : Product.IsClaude ? "ได้รับ statusLine แล้ว แต่ไม่มีเครดิต 5h/Weekly · ตรวจบัญชีและใช้ Claude Code ให้ตอบครั้งแรก" : "ได้รับข้อมูลแล้ว แต่ไม่มีหน้าต่างเครดิต 5h/Weekly ที่รองรับ");
                if (next != "Live" && next != "Partial") { PaintMetric("Current", RemainingBrush(null)); PaintMetric("Weekly", RemainingBrush(null)); }
            }
            if (!Program.HubWorker) Control<TextBlock>("StatusLabel").Text = StatusText();
            UpdateDiagnostics();
        }

        private void UpdatePulse()
        {
            if (Program.HubWorker) return;
            foreach (var dot in new[] { Control<System.Windows.Shapes.Ellipse>("ConnectionDot"), edgeDot }) {
                if (dot == null) continue;
                bool visible = dot == edgeDot ? edgeWindow != null && edgeWindow.IsVisible : Window.IsVisible;
                dot.BeginAnimation(UIElement.OpacityProperty, null);
                dot.Opacity = 1;
                if (State == "Live" && preferences.AnimateStatus && SystemParameters.ClientAreaAnimation && visible) {
                    dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.28, 1, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                }
            }
        }
        private void UpdateAppearanceStatus(string message)
        {
            stateDetail = message;
            if (Program.HubWorker) return;
            Control<TextBlock>("StatusLabel").Text = StatusText();
            var dot = Control<System.Windows.Shapes.Ellipse>("ConnectionDot");
            dot.ToolTip = State == "Live" ? "รับเครดิตผ่าน " + Product.Source + "\nเวลาแสดงคือเวลารับข้อมูลในเครื่อง" : message;
            if (edgeDot != null) { edgeDot.Fill = dot.Fill; edgeWindow.ToolTip = StatusText() + "\n" + message + "\nชี้เมาส์เพื่อแสดงวิดเจ็ต"; }
            if (lastPulseState != State) { lastPulseState = State; UpdatePulse(); }
        }
        private Rect CurrentWorkArea()
        {
            var presentation = PresentationSource.FromVisual(Window);
            Matrix to = presentation != null && presentation.CompositionTarget != null ? presentation.CompositionTarget.TransformToDevice : Matrix.Identity;
            Matrix from = presentation != null && presentation.CompositionTarget != null ? presentation.CompositionTarget.TransformFromDevice : Matrix.Identity;
            Point point = to.Transform(new Point(Window.Left + Window.Width / 2, Window.Top + Window.Height / 2));
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
            var bounds = screen.WorkingArea;
            return new Rect(from.Transform(new Point(bounds.Left, bounds.Top)), from.Transform(new Point(bounds.Right, bounds.Bottom)));
        }
        private void ClampCurrentWindowToWorkArea()
        {
            Rect area = CurrentWorkArea();
            Window.Width = Math.Max(Window.MinWidth, Math.Min(Window.Width, area.Width));
            Window.Height = Math.Max(Window.MinHeight, Math.Min(Window.Height, area.Height));
            Window.Left = Math.Max(area.Left, Math.Min(Window.Left, area.Right - Window.Width));
            Window.Top = Math.Max(area.Top, Math.Min(Window.Top, area.Bottom - Window.Height));
        }
        private void OnDisplaySettingsChanged(object sender, EventArgs args) { QueueDisplayRecovery(); }
        private void OnDesktopPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (args.Category == UserPreferenceCategory.Desktop || args.Category == UserPreferenceCategory.General || args.Category == UserPreferenceCategory.Window) QueueDisplayRecovery();
        }
        private void QueueDisplayRecovery()
        {
            if (exiting || disposed || Window.Dispatcher.HasShutdownStarted) return;
            // SystemEvents may arrive on a worker thread. All WPF changes stay on its dispatcher.
            try { Window.Dispatcher.BeginInvoke(new Action(RecoverDisplayPlacement)); }
            catch (InvalidOperationException) { }
        }
        public void RecoverDisplayPlacement()
        {
            if (exiting || disposed) return;
            bool wasHidden = edgeHidden;
            CancelEdgeTransition();
            if (wasHidden) Window.Hide();
            bool previousTransition = transitioning;
            transitioning = true;
            try {
                ClampCurrentWindowToWorkArea();
                if (edgeHidden && edgeWindow != null) PositionEdgeWindow(CurrentWorkArea());
            } finally { transitioning = previousTransition; }
            // Keep both the tray-hidden state and edge-hidden state; display changes never show a window.
            SaveSettings();
        }
        private void PositionEdgeWindow(Rect area)
        {
            edgeWindow.Left = DockSide == "Left" ? area.Left : area.Right - edgeWindow.Width;
            edgeWindow.Top = Math.Max(area.Top, Math.Min(area.Bottom - edgeWindow.Height, Window.Top + (Window.Height - edgeWindow.Height) / 2));
            edgeWindow.Topmost = Window.Topmost;
            edgeCard.CornerRadius = ThemeCatalog.Find(Theme).Pixel ? new CornerRadius(0) : DockSide == "Left" ? new CornerRadius(0, 12, 12, 0) : new CornerRadius(12, 0, 0, 12);
        }
        public void BeginWindowMove()
        {
            if (edgeHidden) RevealFromEdge();
            CancelEdgeTransition();
            if (edgeWindow != null) edgeWindow.Hide();
            edgeHidden = false; resizing = true; edgeDelay.Stop();
        }
        public void EndWindowMove()
        {
            resizing = false;
            if (!Pinned && preferences.EdgeSide == "Nearest") SetDock(ResolveAutoHideSide(), false);
            SaveSettings(); ScheduleEdgeHide();
        }
        private string ResolveAutoHideSide()
        {
            if (preferences.EdgeSide == "Left" || preferences.EdgeSide == "Right") return preferences.EdgeSide;
            Rect area = CurrentWorkArea();
            return Window.Left + Window.Width / 2 < area.Left + area.Width / 2 ? "Left" : "Right";
        }
        public void SetAutoHideSide(string side)
        {
            preferences.EdgeSide = side == "Left" || side == "Right" ? side : "Nearest";
            if (!Pinned) SetDock(ResolveAutoHideSide(), false);
            SaveSettings();
        }
        public void TogglePinned()
        {
            if (exiting) return;
            if (preferences.Hidden) Show();
            SetDock(Pinned ? ResolveAutoHideSide() : "None", true);
        }
        private void UpdatePinAppearance()
        {
            if (Program.HubWorker) return;
            bool light = ThemeCatalog.Find(Theme).Light;
            Brush ink = Pinned ? ColorBrush(light ? "#15803D" : "#4ADE80") : (Brush)Window.Resources["MutedBrush"];
            foreach (string prefix in new[] { "", "Mini" }) {
                var glyph = Control<System.Windows.Shapes.Path>(prefix + "PinGlyph");
                glyph.Stroke = ink; glyph.Fill = Pinned ? ColorBrush(light ? "#3015803D" : "#304ADE80") : Brushes.Transparent;
                Control<System.Windows.Shapes.Path>(prefix + "PinSlash").Visibility = Pinned ? Visibility.Collapsed : Visibility.Visible;
                Control<Button>(prefix + "PinButton").ToolTip = Pinned ? "ปักหมุดอยู่ · ไม่ซ่อนริมจอ\nกดเพื่อซ่อนริมจออัตโนมัติ · Ctrl+P" : "ไม่ปักหมุด · ซ่อนริมจอ" + (DockSide == "Left" ? "ซ้าย" : "ขวา") + "อัตโนมัติ\nกดเพื่อปักหมุดให้อยู่บนจอ · Ctrl+P";
                Control<Button>(prefix + "PinButton").Background = Pinned ? ColorBrush(light ? "#1815803D" : "#184ADE80") : Brushes.Transparent;
            }
        }
        public void SetDock(string side, bool save)
        {
            if (side != "Left" && side != "Right") side = "None";
            edgeDelay.Stop();
            if (edgeHidden) RevealFromEdge();
            CancelEdgeTransition();
            preferences.DockSide = side;
            if (side == "None") { edgeDelay.Stop(); if (edgeWindow != null) edgeWindow.Hide(); edgeHidden = false; }
            else { EnsureEdgeWindow(); ScheduleEdgeHide(); }
            UpdatePinAppearance();
            if (save) SaveSettings();
        }
        private void EnsureEdgeWindow()
        {
            if (edgeWindow != null) return;
            edgeDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Fill, Margin = new Thickness(0, 8, 0, 5), HorizontalAlignment = HorizontalAlignment.Center };
            edgeTitle = new TextBlock { Text = Product.Label, FontFamily = font, FontSize = 12, Foreground = (Brush)Window.Resources["TextBrush"], HorizontalAlignment = HorizontalAlignment.Center };
            edgeQuotaLabel = new TextBlock { Text = "5h", FontFamily = font, FontSize = 10, Foreground = (Brush)Window.Resources["MutedBrush"], HorizontalAlignment = HorizontalAlignment.Center };
            edgePercent = new TextBlock { Text = "—", FontFamily = font, FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7) };
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(edgeDot); panel.Children.Add(edgeTitle); panel.Children.Add(edgePercent);
            edgeCard = new Border { Background = (Brush)Window.Resources["SurfaceBrush"], BorderBrush = (Brush)Window.Resources["EdgeBrush"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = panel, Cursor = Cursors.Hand };
            edgeWindow = new Window { Width = 28, Height = 108, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = Window.Topmost, ShowActivated = false, Content = edgeCard, Title = Product.Name + " · edge tab", FontFamily = font, Opacity = verification ? 0 : 1 };
            edgeWindow.MouseEnter += (s, e) => RevealFromEdge();
            edgeWindow.MouseLeftButtonDown += (s, e) => RevealFromEdge();
            edgeWindow.IsVisibleChanged += (s, e) => UpdatePulse();
            System.Windows.Automation.AutomationProperties.SetName(edgeCard, Product.Label + " · แท็บเครดิตริมจอ");
            UpdateEdgeQuota();
        }
        public void UpdateEdgeQuota()
        {
            if (edgeWindow == null) return;
            if(edgeQuotaLabel!=null)edgeQuotaLabel.Text=PrimaryLabel;
            Metric metric = latestData == null ? null : latestData.Current;
            bool fresh = metric != null && !preferences.MonitoringPaused && (State == "Live" || State == "Partial");
            edgePercent.Text = metric == null ? "—" : metric.Display;
            edgePercent.Foreground = fresh ? Control<TextBlock>("CurrentValue").Foreground : ColorBrush(ThemeCatalog.Find(Theme).Muted);
            edgePercent.Visibility = preferences.EdgeShowPercent ? Visibility.Visible : Visibility.Collapsed;
            edgeQuotaLabel.Visibility = Visibility.Collapsed;
            edgeWindow.Width = 28;
            edgeTitle.FontSize = 12;
            edgeTitle.LayoutTransform = new RotateTransform(90);
            edgeTitle.Margin = new Thickness(0, 0, 0, 6);
            edgeDot.Fill = Control<System.Windows.Shapes.Ellipse>("ConnectionDot").Fill;
            string value = metric == null ? "ยังไม่มีข้อมูล" : metric.Display + " remaining" + (fresh ? "" : " · ค่าครั้งล่าสุด");
            edgeWindow.ToolTip = PrimaryLabel + ": " + value + "\n" + GetResetTooltip(metric) + "\n" + StatusText() + "\nชี้เมาส์เพื่อแสดงวิดเจ็ต";
            System.Windows.Automation.AutomationProperties.SetHelpText(edgeCard, PrimaryLabel + ": " + value + ". " + StatusText() + ". คลิกเพื่อแสดงวิดเจ็ต");
            if (edgeWindow.IsVisible) PositionEdgeWindow(CurrentWorkArea());
        }
        public void SetTimeFormat(string format)
        {
            preferences.TimeFormat = format == "12H" ? "12H" : "24H";
            UpdateCountdown(); UpdateLayout(); UpdateEdgeQuota(); SaveSettings();
        }
        public void SetEdgeShowPercent(bool show)
        {
            preferences.EdgeShowPercent = show; UpdateEdgeQuota(); SaveSettings();
        }
        public void SetEdgeMotion(bool enabled)
        {
            preferences.EdgeMotion = enabled;
            if (!enabled && edgeMotionActive) {
                bool concealed = edgeHidden;
                CancelEdgeTransition();
                if (concealed) Window.Hide();
                else { if (edgeWindow != null) edgeWindow.Hide(); ScheduleEdgeHide(); }
            }
            SaveSettings();
        }
        public bool EdgeTransitionActive { get { return edgeMotionActive; } }
        public void CancelEdgeTransition()
        {
            ++edgeMotionSerial;
            if (!edgeMotionActive) return;
            edgeSlide.BeginAnimation(TranslateTransform.XProperty, null); edgeSlide.X = 0;
            if (edgeBlur != null) edgeBlur.BeginAnimation(HorizontalMotionBlurEffect.RadiusProperty, null);
            var card = Control<Border>("RootCard");
            card.RenderTransform = edgePreviousTransform;
            card.Effect = edgePreviousEffect;
            edgeBlur = null; edgeMotionActive = false; transitioning = false;
        }
        private void AnimateEdge(double from, double to, bool reveal, Action completed)
        {
            if (!preferences.EdgeMotion || !SystemParameters.ClientAreaAnimation || verification) { completed(); return; }
            CancelEdgeTransition(); saveLayout.Stop();
            var card = Control<Border>("RootCard");
            edgePreviousTransform = card.RenderTransform; edgePreviousEffect = card.Effect;
            card.RenderTransform = edgeSlide; edgeSlide.X = from;
            // Shader Model 2 works with WPF's hardware or software renderer.
            // If loading an effect fails, keep the slide usable without blur.
            try { edgeBlur = new HorizontalMotionBlurEffect(); card.Effect = edgeBlur; }
            catch (Exception) { edgeBlur = null; card.Effect = edgePreviousEffect; }
            edgeMotionActive = true; transitioning = true;
            int serial = edgeMotionSerial;
            TimeSpan duration = TimeSpan.FromMilliseconds(reveal ? 280 : 220);
            var slide = new DoubleAnimation(from, to, duration) { EasingFunction = new CubicEase { EasingMode = reveal ? EasingMode.EaseOut : EasingMode.EaseIn }, FillBehavior = FillBehavior.HoldEnd };
            // Blur belongs only to the moving surface and returns to sharp before the slide ends.
            var blur = new DoubleAnimationUsingKeyFrames { Duration = new Duration(duration) };
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(12 / Math.Max(1, card.ActualWidth), KeyTime.FromPercent(reveal ? 0.22 : 0.65)));
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.9)));
            slide.Completed += (s, e) => {
                if (serial != edgeMotionSerial || exiting || disposed) return;
                CancelEdgeTransition(); completed(); UpdatePulse();
            };
            if (edgeBlur != null) edgeBlur.BeginAnimation(HorizontalMotionBlurEffect.RadiusProperty, blur);
            edgeSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        private void ScheduleEdgeHide()
        {
            if (DockSide == "None" || preferences.Hidden || exiting || settingsWindow != null || edgeHidden || edgeMotionActive || (keyboardInteraction && Window.IsKeyboardFocusWithin)) return;
            edgeDelay.Stop(); edgeDelay.Start();
        }
        public void CollapseToEdge()
        {
            if (DockSide == "None" || preferences.Hidden || exiting || resizing || edgeHidden || settingsWindow != null || Window.ContextMenu.IsOpen || (keyboardInteraction && Window.IsKeyboardFocusWithin)) return;
            CancelEdgeTransition(); EnsureEdgeWindow(); Rect area = CurrentWorkArea();
            UpdateEdgeQuota(); PositionEdgeWindow(area);
            edgeHidden = true; edgeWindow.Show();
            if (!edgeHidden) return;
            AnimateEdge(0, (DockSide == "Left" ? -1 : 1) * (Window.Width + 8), false, () => { if (edgeHidden && !preferences.Hidden) Window.Hide(); });
            UpdatePulse();
        }
        public void RevealFromEdge()
        {
            edgeDelay.Stop();
            if (exiting || disposed) return;
            bool fromEdge = edgeHidden;
            bool reversing = edgeMotionActive && fromEdge;
            double motionFrom = reversing ? edgeSlide.X : (DockSide == "Left" ? -1 : 1) * (Window.Width + 8);
            CancelEdgeTransition();
            transitioning = true;
            try {
                if (edgeWindow != null && fromEdge) {
                    Rect area = CurrentWorkArea();
                    Window.Left = DockSide == "Left" ? area.Left + 3 : area.Right - Window.Width - 3;
                    Window.Top = Math.Max(area.Top, Math.Min(area.Bottom - Window.Height, edgeWindow.Top + (edgeWindow.Height - Window.Height) / 2));
                    edgeWindow.Hide();
                }
                ClampCurrentWindowToWorkArea();
                edgeHidden = false; Window.Show();
            } finally { transitioning = false; }
            if (fromEdge) AnimateEdge(motionFrom, 0, true, () => { if (edgeWindow != null) edgeWindow.Hide(); ScheduleEdgeHide(); });
            else if (edgeWindow != null) edgeWindow.Hide();
            UpdatePulse();
            // The pointer may still be over the 3 px gap at the edge rather than over this window.
            ScheduleEdgeHide();
        }
        private Button CreateThemeTile(ThemePreset preset)
        {
            var content = new StackPanel();
            var title = new Grid();
            title.ColumnDefinitions.Add(new ColumnDefinition());
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(new TextBlock { Text = preset.Label, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var check = new TextBlock { Text = "✓", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = ColorBrush(preset.Accent), Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Hidden };
            Grid.SetColumn(check, 1); title.Children.Add(check); content.Children.Add(title);
            content.Children.Add(new TextBlock { Text = preset.Description, Foreground = ColorBrush(preset.Muted), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 12) });
            for (int row = 0; row < 2; row++) {
                var sample = new Grid { Height = 20 };
                sample.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                sample.ColumnDefinitions.Add(new ColumnDefinition());
                sample.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                sample.Children.Add(new TextBlock { Text = row == 0 ? "5h" : "Weekly", FontSize = 11, Foreground = ColorBrush(preset.Muted), VerticalAlignment = VerticalAlignment.Center });
                var track = new Border { Background = ColorBrush(preset.Track), CornerRadius = new CornerRadius(preset.BarRadius), Height = 4, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                var fill = new Grid();
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(row == 0 ? 3 : 4, GridUnitType.Star) });
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fill.Children.Add(new Border { Background = ColorBrush(preset.Light ? "#15803D" : "#4ADE80"), CornerRadius = new CornerRadius(preset.BarRadius) });
                track.Child = fill; Grid.SetColumn(track, 1); sample.Children.Add(track);
                var badge = new Border { Height = 13, Width = 26, CornerRadius = new CornerRadius(Math.Min(6, preset.BadgeRadiusLimit)), Background = ColorBrush(preset.Light ? "#2415803D" : "#304ADE80"), BorderBrush = ColorBrush(preset.Light ? "#5015803D" : "#504ADE80"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(badge, 2); sample.Children.Add(badge); content.Children.Add(sample);
            }
            var button = new Button { Content = content, Height = 136, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(12), Background = preset.Pixel ? (Brush)PixelFill(Color.FromRgb(38, 46, 36), true) : preset.SurfaceBrush(), Foreground = ColorBrush(preset.Text), BorderBrush = ColorBrush(preset.Edge), BorderThickness = new Thickness(1), FontFamily = font, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = preset.Description + "\nตัวอย่างรูปแบบ · คลิกเพื่อนำไปใช้" };
            var chrome = new FrameworkElementFactory(typeof(Border)); chrome.Name = "ThemeTileFrame";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(Math.Min(12, preset.Radius)));
            foreach (var pair in new[] { new KeyValuePair<DependencyProperty, string>(Border.BackgroundProperty, "Background"), new KeyValuePair<DependencyProperty, string>(Border.BorderBrushProperty, "BorderBrush"), new KeyValuePair<DependencyProperty, string>(Border.BorderThicknessProperty, "BorderThickness"), new KeyValuePair<DependencyProperty, string>(Border.PaddingProperty, "Padding") })
                chrome.SetBinding(pair.Key, new System.Windows.Data.Binding(pair.Value) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            chrome.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var focus = new Trigger { Property = Button.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush("#93C5FD"), "ThemeTileFrame"));
            template.Triggers.Add(focus);
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9, "ThemeTileFrame")); template.Triggers.Add(hover);
            button.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(button, "ธีม " + preset.Label);
            button.Click += (s, e) => ApplyTheme(preset.Key, true);
            themeButtons[preset.Key] = button; themeChecks[preset.Key] = check;
            return button;
        }
        private void UpdateThemeSelection()
        {
            if (selectedThemeLabel != null) selectedThemeLabel.Text = "ธีมปัจจุบัน: " + ThemeCatalog.Find(Theme).Label;
            if (settingsWindow != null) { settingsWindow.Title = "ตั้งค่า · " + Product.Label; ApplySettingsPalette(); }
            foreach (var entry in themeButtons) {
                bool selected = entry.Key == Theme;
                var preset = ThemeCatalog.Find(entry.Key);
                entry.Value.BorderBrush = ColorBrush(selected ? preset.Accent : preset.Edge);
                entry.Value.BorderThickness = new Thickness(selected ? 2 : 1);
                themeChecks[entry.Key].Visibility = selected ? Visibility.Visible : Visibility.Hidden;
                System.Windows.Automation.AutomationProperties.SetHelpText(entry.Value, selected ? "เลือกอยู่" : "คลิกเพื่อเลือกธีม");
            }
        }
        private static void AddSettingButton(Panel panel, string text, Action click)
        {
            var button = new Button { Content = text, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
            button.Click += (s, e) => click(); panel.Children.Add(button);
        }
        private void DisposeAppearance()
        {
            if (!Program.HubWorker) {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.UserPreferenceChanged -= OnDesktopPreferenceChanged;
            }
            saveLayout.Stop(); health.Stop(); edgeDelay.Stop();
            CancelEdgeTransition();
            if (edgeDot != null) edgeDot.BeginAnimation(UIElement.OpacityProperty, null);
            if (edgeWindow != null) edgeWindow.Close();
            if (settingsWindow != null) settingsWindow.Close();
            if (!Program.HubWorker) Control<System.Windows.Shapes.Ellipse>("ConnectionDot").BeginAnimation(UIElement.OpacityProperty, null);
        }
    }
}
