using System;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;
namespace CreditMonitor
{
    public static class GeminiConfiguration
    {
        public const string Host = "com.firm.quota_monitor.gemini", ExtensionId = "hgaopoikidkmaokghdlaffihbgbklhci", ExtensionOrigin = "chrome-extension://hgaopoikidkmaokghdlaffihbgbklhci/";
        private static readonly string[] Keys = { @"Software\Google\Chrome\NativeMessagingHosts\" + Host, @"Software\Microsoft\Edge\NativeMessagingHosts\" + Host };
        private static string ManifestPath { get { return Path.Combine(Product.Storage, "gemini-native-host.json"); } }
        private static bool Same(string a, string b) { try { return String.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        public static bool Registered(string executable)
        {
            try { if (!File.Exists(ManifestPath)) return false; var value = new JavaScriptSerializer().Deserialize<NativeManifest>(File.ReadAllText(ManifestPath)); if (!Same(value.path, executable) || value.name != Host) return false; using (var key = Registry.CurrentUser.OpenSubKey(Keys[0])) return key != null && Same(Convert.ToString(key.GetValue("")), ManifestPath); } catch { return false; }
        }
        public static void Connect(string executable)
        {
            executable = Path.GetFullPath(executable); if (!File.Exists(executable)) throw new FileNotFoundException("ไม่พบตัวเชื่อม Gemini · แตก ZIP ทั้งโฟลเดอร์ก่อน");
            foreach (string path in Keys) using (var existing = Registry.CurrentUser.OpenSubKey(path)) { if (existing != null && !Same(Convert.ToString(existing.GetValue("")), ManifestPath)) throw new IOException("มีตัวเชื่อมชื่อเดียวกันจากที่อื่น กรุณาตรวจการติดตั้งเดิมก่อน"); }
            Directory.CreateDirectory(Product.Storage);
            File.WriteAllText(ManifestPath, new JavaScriptSerializer().Serialize(new NativeManifest { name = Host, description = "Gemini Usage quota only", path = executable, type = "stdio", allowed_origins = new[] { ExtensionOrigin } }));
            foreach (string path in Keys) using (var key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("", ManifestPath);
        }
        public static void Disconnect(string executable)
        {
            if (!File.Exists(ManifestPath)) return;
            var manifest = new JavaScriptSerializer().Deserialize<NativeManifest>(File.ReadAllText(ManifestPath)); if (!Same(manifest.path, executable)) return;
            foreach (string path in Keys) { bool owned; using (var key = Registry.CurrentUser.OpenSubKey(path)) owned = key != null && Same(Convert.ToString(key.GetValue("")), ManifestPath); if (owned) Registry.CurrentUser.DeleteSubKeyTree(path, false); }
            File.Delete(ManifestPath);
        }
        private sealed class NativeManifest { public string name, description, path, type; public string[] allowed_origins; }
    }
}
