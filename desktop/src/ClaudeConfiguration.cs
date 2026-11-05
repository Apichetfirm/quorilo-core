using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public static class ClaudeConfiguration
    {
        public static string SettingsPath { get { string custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"); return Path.Combine(String.IsNullOrWhiteSpace(custom) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude") : Path.GetFullPath(custom), "settings.json"); } }
        public static string Command(string executable)
        {
            string path = Path.GetFullPath(executable).Replace('\\', '/');
            if (path.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new IOException("Unsupported newline in installation path.");
            string slotSetup = Product.AccountSlot.Length==0?"":"$env:QUORILO_ACCOUNT_SLOT='"+Product.AccountSlot+"'; ";
            string adapter = slotSetup+"[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::In.ReadToEnd() | & '" + path.Replace("'", "''") + "'; exit $LASTEXITCODE";
            return "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(adapter));
        }
        private static byte[] ReadBytes(string path)
        {
            if (!File.Exists(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (stream.Length > 1024 * 1024) throw new IOException("Claude settings are too large.");
                using (var memory = new MemoryStream()) { var buffer = new byte[4096]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { if (memory.Length + read > 1024 * 1024) throw new IOException("Claude settings are too large."); memory.Write(buffer, 0, read); } return memory.ToArray(); }
            }
        }
        private static Dictionary<string, object> Parse(byte[] bytes)
        {
            if (bytes == null) return new Dictionary<string, object>();
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var data = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            if (data == null) throw new IOException("Claude settings must be a JSON object."); return data;
        }
        private static string Fingerprint(byte[] bytes)
        { if (bytes == null) return "missing"; using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        public static string Revision(string path) { return Fingerprint(ReadBytes(path)); }
        public static void Commit(string path, Dictionary<string, object> data, string expectedRevision)
        {
            // Guard against edits since reading; the mutex coordinates this app's writers.
            // Other applications may replace a file without participating in that mutex.
            byte[] original = ReadBytes(path);
            if (Fingerprint(original) != expectedRevision) throw new IOException("Claude settings เปลี่ยนระหว่างตั้งค่า · ยังไม่ได้บันทึก โปรดลองอีกครั้ง");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
                if (Revision(path) != expectedRevision) throw new IOException("Claude settings เปลี่ยนระหว่างตั้งค่า · ยังไม่ได้บันทึก โปรดลองอีกครั้ง");
                if (original != null) { File.WriteAllBytes(path + ".claude-monitor-backup-" + Guid.NewGuid().ToString("N"), original); File.Replace(temporary, path, null); }
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static bool Owned(Dictionary<string, object> data, string executable)
        {
            object raw, command; var status = data.TryGetValue("statusLine", out raw) ? raw as Dictionary<string, object> : null;
            return status != null && status.TryGetValue("command", out command) && String.Equals(command as string, Command(executable), StringComparison.Ordinal);
        }
        public static string Inspect(string path, string executable)
        {
            var data = Parse(ReadBytes(path));
            return Owned(data, executable) ? "ตั้งค่าตัวส่งแล้ว · ยังต้องรอข้อมูลเครดิต" : data.ContainsKey("statusLine") ? "มี statusLine เดิม · ใช้การเชื่อมในสคริปต์เดิม" : "ยังไม่ได้ตั้งค่าตัวส่ง";
        }
        public static bool UsesBridge(string path,string executable){try{return Owned(Parse(ReadBytes(path)),executable);}catch{return false;}}
        private static bool Change(string path, string executable, bool connect)
        {
            string lockName; using (var hash = SHA256.Create()) lockName = "Local\\ClaudeMonitorConfig-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))).Replace("-", "");
            using (var mutex = new Mutex(false, lockName)) {
                bool acquired; try { acquired = mutex.WaitOne(2000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Claude settings กำลังถูกแก้ไข · ลองอีกครั้ง");
                try {
                    byte[] bytes = ReadBytes(path); var data = Parse(bytes);
                    if (connect) {
                        if (Owned(data, executable)) return false;
                        if (data.ContainsKey("statusLine")) throw new IOException("มี statusLine เดิมอยู่แล้ว · ยังไม่ได้แก้ไข\nคัดลอกคำสั่งจากหน้าการเชื่อมต่อไปเพิ่มในสคริปต์เดิม");
                        data["statusLine"] = new Dictionary<string, object> { { "type", "command" }, { "command", Command(executable) } };
                    } else { if (!Owned(data, executable)) return false; data.Remove("statusLine"); }
                    Commit(path, data, Fingerprint(bytes)); return true;
                } finally { mutex.ReleaseMutex(); }
            }
        }
        public static bool Connect(string path, string executable) { return Change(path, executable, true); }
        public static bool Disconnect(string path, string executable) { return Change(path, executable, false); }
    }
}
