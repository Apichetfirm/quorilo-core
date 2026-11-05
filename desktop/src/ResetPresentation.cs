using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace CreditMonitor
{
    public sealed partial class Widget
    {
        public string TimeFormat { get { return preferences.TimeFormat; } }
        public string GetResetText(Metric metric)
        {
            string countdown = QuotaFormatting.ResetLabel(metric);
            if (metric == null || !metric.ResetAt.HasValue || Window.Height < 108) return countdown;
            DateTime local = metric.ResetAt.Value.ToLocalTime();
            string day = local.Date == DateTime.Now.Date ? "Today" : local.Date == DateTime.Now.Date.AddDays(1) ? "Tomorrow" : local.ToString("dd MMM", CultureInfo.InvariantCulture);
            return countdown + " | " + day + " " + local.ToString(preferences.TimeFormat == "12H" ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
        }
        public string GetResetTooltip(Metric metric)
        {
            if (metric == null || !metric.ResetAt.HasValue) return QuotaFormatting.ResetLabel(metric) + " · ไม่ทราบเวลารีเซ็ต";
            DateTime local = metric.ResetAt.Value.ToLocalTime();
            string zone = TimeZoneInfo.Local.IsDaylightSavingTime(local) ? TimeZoneInfo.Local.DaylightName : TimeZoneInfo.Local.StandardName;
            return QuotaFormatting.ResetLabel(metric) + "\nReset at " + local.ToString(preferences.TimeFormat == "12H" ? "ddd dd MMM yyyy · h:mm tt" : "ddd dd MMM yyyy · HH:mm", CultureInfo.InvariantCulture) + "\n" + zone;
        }
        private string GetRowResetText(Metric metric)
        {
            return GetResetText(metric);
        }
        private void UpdateResetTooltips()
        {
            if (Program.HubWorker) return;
            foreach (string prefix in new[] { "", "Compact", "Wide", "Tall", "Row" }) foreach (string period in new[] { "Current", "Weekly" }) {
                var text = Control<TextBlock>(prefix + period + "Reset");
                if (text != null) text.ToolTip = GetResetTooltip(latestData == null ? null : period == "Current" ? latestData.Current : latestData.Weekly);
            }
        }
        private void AddResetAndEdgeSettings(Panel panel)
        {
            var reset = Section(panel, "วันเวลารีเซ็ต", "แสดงเวลาที่เหลือพร้อมวันเวลารีเซ็ต โดยใช้เขตเวลาของ Windows");
            var clock = new ComboBox { ItemsSource = new[] { "24 ชั่วโมง · 18:30", "12 ชั่วโมง · 6:30 PM" }, SelectedIndex = preferences.TimeFormat == "12H" ? 1 : 0, Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(clock, "รูปแบบเวลารีเซ็ต");
            clock.SelectionChanged += (s, e) => SetTimeFormat(clock.SelectedIndex == 1 ? "12H" : "24H"); reset.Children.Add(clock);
            var edge = Section(panel, "ซ่อนริมจอ", "ถอดหมุดเพื่อซ่อนอัตโนมัติ ชี้เมาส์ที่แท็บริมจอเพื่อเปิดวิดเจ็ต");
            settingsPinButton = ActionButton(edge, "", () => { TogglePinned(); UpdateSettingsSummary(); });
            Text(edge, "ขอบจอที่ต้องการซ่อน");
            var side = new ComboBox { ItemsSource = new[] { "ขอบที่ใกล้วิดเจ็ตที่สุด", "ซ้าย", "ขวา" }, SelectedIndex = preferences.EdgeSide == "Left" ? 1 : preferences.EdgeSide == "Right" ? 2 : 0, Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(side, "เลือกขอบจอที่ซ่อน");
            side.SelectionChanged += (s, e) => SetAutoHideSide(side.SelectedIndex == 1 ? "Left" : side.SelectedIndex == 2 ? "Right" : "Nearest"); edge.Children.Add(side);
            Check(edge, "แสดงเปอร์เซ็นต์ 5h ใต้ชื่อบนแท็บริมจอ", preferences.EdgeShowPercent, SetEdgeShowPercent);
            Check(edge, "เลื่อนเปิด / ซ่อนพร้อม Motion Blur", preferences.EdgeMotion, SetEdgeMotion);
            Text(edge, "เบลอเฉพาะแนวนอนขณะเลื่อน แล้วกลับคมชัดเมื่อหยุด ปิดได้ และทำตามการตั้งค่า animation ของ Windows");
        }
    }
}
