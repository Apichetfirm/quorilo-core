using System;
using System.Collections.Generic;
using System.Globalization;

namespace CreditMonitor
{
    public static class NativeLocale
    {
        static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "มอนิเตอร์", new[] { "Monitors", "モニター" } },
            { "รูปแบบ", new[] { "Appearance", "外観" } },
            { "แจ้งเตือน", new[] { "Alerts", "通知" } },
            { "ผู้ช่วย", new[] { "Assistant", "アシスタント" } },
            { "มือถือ", new[] { "Mobile", "モバイル" } },
            { "Reset Radar", new[] { "Reset Radar", "Reset Radar" } },
            { "เกี่ยวกับ", new[] { "About", "アプリについて" } },
            { "ภาษา", new[] { "Language", "言語" } },
            { "ไทย", new[] { "Thai", "タイ語" } },
            { "日本語", new[] { "Japanese", "日本語" } },
            { "English", new[] { "English", "英語" } },
            { "ธีมและการแสดงผล", new[] { "Theme and display", "テーマと表示" } },
            { "เวลารีเซ็ต", new[] { "Reset time", "リセット時刻" } },
            { "ซ่อนริมจอ", new[] { "Edge tab", "画面端に格納" } },
            { "แจ้งเตือนเครดิตต่ำ", new[] { "Low-credit alerts", "残量低下の通知" } },
            { "รอบอ่าน Gemini Apps", new[] { "Gemini Apps refresh interval", "Gemini Apps の更新間隔" } },
            { "คุณค่าที่ได้จากการใช้", new[] { "Your time saved", "節約できた時間" } },
            { "ประมาณ ", new[] { "About ", "約 " } },
            { " นาทีจาก ", new[] { " minutes across ", " 分（更新 " } },
            { " รอบอัปเดต", new[] { " updates", " 回）" } },
            { "ผู้ผลิต · Firm Impulse", new[] { "Made by Firm Impulse", "開発元 · Firm Impulse" } },
            { "เสร็จแล้ว", new[] { "Done", "完了" } },
            { "ปิดตั้งค่า", new[] { "Close settings", "設定を閉じる" } },
            { "บันทึกอัตโนมัติ · ", new[] { "Saved automatically · ", "自動保存 · " } },
            { "Keep this program awake (Prevent sleep)", new[] { "Keep this program awake (Prevent sleep)", "スリープを防止" } },
            { "เปิดพร้อม Windows", new[] { "Start with Windows", "Windows 起動時に開始" } },
            { "ปักหมุด · ไม่ซ่อนอัตโนมัติ", new[] { "Pin · do not auto-hide", "固定 · 自動で隠さない" } },
            { "แสดง % 5h ของบริการที่เชื่อมต่อบนแท็บริมจอ", new[] { "Show connected 5h quota on the edge tab", "画面端タブに接続済みサービスの5時間残量を表示" } },
            { "เลื่อนเปิด / ซ่อนพร้อม Motion Blur", new[] { "Slide with motion blur", "モーションブラー付きで開閉" } },
            { "เปิดแจ้งเตือนแบบรวม", new[] { "Enable combined alerts", "まとめて通知を有効にする" } },
            { "บริการที่รองรับ", new[] { "Supported services", "対応サービス" } },
            { "ดูบนมือถือ", new[] { "View on mobile", "モバイルで見る" } },
            { "ข่าวรีเซ็ตเครดิต Codex", new[] { "Codex reset updates", "Codex のリセット情報" } },
            { "ข้อมูลจาก Quorilo Reset · แสดงเวลาอ่านและแหล่งอ้างอิงแยกจากเครดิตของคุณ", new[] { "From Quorilo Reset · source and update time are separate from your quota", "Quorilo Reset の情報です。参照元と更新時刻をクレジット残量とは分けて表示します。" } },
            { "กดรีเฟรชเพื่ออ่านข้อมูลล่าสุด", new[] { "Refresh to load the latest information", "更新して最新情報を読み込む" } },
            { "รีเฟรชข่าว", new[] { "Refresh updates", "情報を更新" } },
            { "เปิด Quorilo Reset", new[] { "Open Quorilo Reset", "Quorilo Reset を開く" } },
            { "กำลังอ่าน Reset Radar…", new[] { "Loading Reset Radar…", "Reset Radar を読み込み中…" } },
            { "ข้อมูลต้นทางล่าสุด", new[] { "Latest source data", "最新のソースデータ" } },
            { "ข้อมูลสำรองที่ตรวจแล้ว", new[] { "Verified backup data", "確認済みの代替データ" } },
            { "วินาที", new[] { "seconds", "秒" } },
            { "นาที", new[] { "minutes", "分" } },
            { "ชั่วโมง", new[] { "hours", "時間" } },
            { "วันนี้", new[] { "Today", "今日" } },
            { "พรุ่งนี้", new[] { "Tomorrow", "明日" } },
            { "รีเซ็ตใน ", new[] { "Resets in ", "リセットまで " } },
            { "เหลือ ", new[] { " remaining ", " 残り " } },
            { "รีเซ็ตแล้ว", new[] { "Reset", "リセット済み" } },
            { "ข้อมูลตัวอย่าง", new[] { "Sample data", "サンプルデータ" } },
            { "ส่วนตัว", new[] { "Personal", "個人用" } },
            { "พัก", new[] { "Paused", "一時停止" } },
            { "ทำงานต่อ", new[] { "Resume", "再開" } },
            { "เกี่ยวกับ Quorilo Monitor", new[] { "About Quorilo Monitor", "Quorilo Monitor について" } }
        };

        public static string Normalize(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "th";
            string language = value.Trim().Replace('_', '-').Split('-')[0].ToLowerInvariant();
            return language == "en" || language == "ja" || language == "th" ? language : "th";
        }

        public static string Detect()
        {
            try { return Normalize(CultureInfo.CurrentUICulture.Name); }
            catch { return "th"; }
        }

        public static string Translate(string text, string locale)
        {
            if (text == null) return "";
            string language = Normalize(locale);
            string[] values;
            if (language == "th" || !Strings.TryGetValue(text, out values)) return text;
            return language == "en" ? values[0] : values[1];
        }
    }
}
