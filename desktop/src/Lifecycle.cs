using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CreditMonitor
{
    public static class Lifecycle
    {
        public static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Product.StorageName);
        public static readonly string InstalledExe = Path.Combine(InstallRoot, Product.Executable);
        public static readonly string StartupLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Product.Name + ".lnk");
        public static readonly string MenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product.Name + ".lnk");
        private static readonly string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Product.StorageName;
        private static string LegacyStartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Claude Code Monitor.lnk"); } }
        private static string LegacyMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Claude Code Monitor.lnk"); } }
        private static string[] LegacyNames { get { return Product.IsGemini ? new[]{"Gemini Quota Monitor","Gemini Credit Monitor","Credit Monitor · Gemini"} : Product.IsClaude ? new[]{"Quota Monitor · Code Bridge","Claude Code Monitor","Claude Credit Monitor","Credit Monitor · Claude"} : Product.IsUnified ? (Product.Trial ? new[]{"Credit Monitor Trial","Credit Monitor by Firm Impulse"} : new[]{"Credit Monitor","Credit Monitor by Firm Impulse"}) : new[]{"ChatGPT Credit Monitor","Credit Monitor · Codex"}; } }
        private static System.Collections.Generic.IEnumerable<string> OldLinks(bool startup){foreach(string name in LegacyNames)yield return Path.Combine(Environment.GetFolderPath(startup?Environment.SpecialFolder.Startup:Environment.SpecialFolder.Programs),name+".lnk");}
        private static void DeleteOldLinks(bool startup){foreach(string path in OldLinks(startup))if(IsOwnedShortcut(path,InstalledExe))File.Delete(path);}
        public static bool StartupEnabled { get { if(File.Exists(StartupLink))return true;foreach(string path in OldLinks(true))if(IsOwnedShortcut(path,InstalledExe))return true;return false; } }
        public static bool IsOwnedShortcut(string path, string target)
        {
            if (!File.Exists(path)) return false;
            object shellObject = null, shortcutObject = null;
            try {
                shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic shell = shellObject;
                shortcutObject = shell.CreateShortcut(path);
                dynamic shortcut = shortcutObject;
                return String.Equals(Path.GetFullPath((string)shortcut.TargetPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
            finally {
                if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
                if (shellObject != null) Marshal.FinalReleaseComObject(shellObject);
            }
        }
        private static void DeleteOwnedLegacyLink(string path)
        {
            if (Product.IsClaude && IsOwnedShortcut(path, InstalledExe)) File.Delete(path);
        }
        public static void Shortcut(string path, string target, string arguments)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            object shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcutObject = null;
            try {
                dynamic shell = shellObject;
                shortcutObject = shell.CreateShortcut(path);
                dynamic shortcut = shortcutObject;
                shortcut.TargetPath = target;
                shortcut.Arguments = arguments;
                shortcut.WorkingDirectory = Path.GetDirectoryName(target);
                shortcut.IconLocation = target + ",0";
                shortcut.Description = Product.Name;
                shortcut.Save();
            }
            finally {
                if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject);
                Marshal.FinalReleaseComObject(shellObject);
            }
        }
        public static void SetStartup(bool enabled, string executable)
        {
            if (enabled) Shortcut(StartupLink, executable, "--startup");
            else if (File.Exists(StartupLink)) File.Delete(StartupLink);
            DeleteOldLinks(true);
        }
        public static bool IsMonitorCommand(string command, string scriptPath)
        {
            return Regex.IsMatch(command ?? "", @"(?:^|\s)-File\s+(?:""" + Regex.Escape(scriptPath) + @"""|" + Regex.Escape(scriptPath) + @")(?:\s|$)", RegexOptions.IgnoreCase);
        }
        public static void StopInstalledMonitor()
        {
            try { using (var stop = EventWaitHandle.OpenExisting(Program.InstanceName + "-Stop")) stop.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            var ids = new HashSet<int>();
            using (var search = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process WHERE Name='" + Product.Executable + "'")) {
                using (var results = search.Get()) {
                    foreach (ManagementObject item in results) {
                        using (item) {
                            int id = Convert.ToInt32(item["ProcessId"]);
                            if (id == Process.GetCurrentProcess().Id) continue;
                            string name = Convert.ToString(item["Name"]);
                            bool match = String.Equals(name, Product.Executable, StringComparison.OrdinalIgnoreCase)
                                ? String.Equals(Convert.ToString(item["ExecutablePath"]), InstalledExe, StringComparison.OrdinalIgnoreCase)
                                : IsMonitorCommand(Convert.ToString(item["CommandLine"]), Path.Combine(InstallRoot, "Monitor.ps1"));
                            if (match) ids.Add(id);
                        }
                    }
                }
            }
            foreach (int id in ids) {
                try { using (var process = Process.GetProcessById(id)) { if (!process.WaitForExit(5000)) throw new IOException("กรุณาปิด " + Product.Name + " ก่อนลองติดตั้งอีกครั้ง"); } }
                catch (ArgumentException) { }
            }
            if(Product.IsUnified)foreach(string storage in new[]{"ChatGPTCreditMonitor","ClaudeCodeMonitor","GeminiQuotaMonitor"}){
                string folder=storage=="ChatGPTCreditMonitor"?"ChatGPT":storage=="ClaudeCodeMonitor"?"Claude":"Gemini";
                string expected=Path.Combine(InstallRoot,"Readers",folder,storage+".exe");
                foreach(var process in Process.GetProcessesByName(storage))using(process)try{
                    if(!String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))continue;
                    try{using(var stop=EventWaitHandle.OpenExisting("Local\\"+storage+"-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value+"-Stop"))stop.Set();}catch(WaitHandleCannotBeOpenedException){}
                    if(!process.WaitForExit(6000))throw new IOException("ตัวอ่านยังทำงาน · ปิดโปรแกรมก่อนติดตั้งอีกครั้ง");
                }catch(InvalidOperationException){}
            }
            Mutex running;
            if (Mutex.TryOpenExisting(Program.InstanceName, out running)) {
                using (running) {
                    bool released;
                    try { released = running.WaitOne(1000); } catch (AbandonedMutexException) { released = true; }
                    if (!released) throw new IOException("กรุณาปิด " + Product.Name + " ก่อนลองติดตั้งอีกครั้ง");
                    running.ReleaseMutex();
                }
            }
        }
        public static void RegisterInstallation(bool startup,string restoredVersion=null)
        {
            var links = new Dictionary<string, byte[]>();
            foreach (string file in new[] { MenuLink, StartupLink }) links[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
            foreach(bool startupLink in new[]{false,true})foreach(string file in OldLinks(startupLink))links[file]=File.Exists(file)?File.ReadAllBytes(file):null;
            var values = new Dictionary<string, Tuple<object, RegistryValueKind>>();
            bool hadKey;
            using (var old = Registry.CurrentUser.OpenSubKey(UninstallKey)) {
                hadKey = old != null;
                if (old != null) foreach (string name in old.GetValueNames()) values[name] = Tuple.Create(old.GetValue(name), old.GetValueKind(name));
            }
            try { ApplyRegistration(startup,restoredVersion); }
            catch {
                foreach (var link in links) {
                    try { if (link.Value == null) { if (File.Exists(link.Key)) File.Delete(link.Key); } else File.WriteAllBytes(link.Key, link.Value); } catch { }
                }
                try {
                    if (!hadKey) Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                    else using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey)) {
                        foreach (string name in key.GetValueNames()) if (!values.ContainsKey(name)) key.DeleteValue(name);
                        foreach (var value in values) key.SetValue(value.Key, value.Value.Item1, value.Value.Item2);
                    }
                } catch { }
                throw;
            }
        }
        private static void ApplyRegistration(bool startup,string restoredVersion=null)
        {
            Shortcut(MenuLink, InstalledExe, "");
            DeleteOldLinks(false);
            SetStartup(startup, InstalledExe);
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey)) {
                key.SetValue("DisplayName", Product.Name);
                key.SetValue("Publisher", "Firm Impulse");
                key.SetValue("DisplayVersion", String.IsNullOrEmpty(restoredVersion)?Product.Version:restoredVersion);
                key.SetValue("InstallLocation", InstallRoot);
                key.SetValue("DisplayIcon", InstalledExe);
                key.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        public static void ValidateTree(string directory, string permittedDirectory)
        {
            string resolved = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            string expected = Path.GetFullPath(permittedDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!String.Equals(resolved, expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected installation directory.");
            if (!Directory.Exists(resolved)) return;
            var pending = new Stack<string>(); pending.Push(resolved);
            while (pending.Count > 0) {
                string current = pending.Pop();
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Installation directory contains a directory link; no files were removed.");
                foreach (string child in Directory.GetDirectories(current)) pending.Push(child);
            }
        }
        public static void DeleteOwnedDirectory(string directory, string permittedDirectory)
        {
            ValidateTree(directory, permittedDirectory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        public static int Uninstall(bool silent)
        {
            if (!silent && MessageBox.Show("ถอนการติดตั้ง " + Product.Name + "?\n\nเก็บธีมและค่าล่าสุดไว้", "ถอนการติดตั้ง", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 0;
            try {
                string helper = Path.Combine(Path.GetTempPath(), "CreditMonitor-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, helper);
                Process.Start(new ProcessStartInfo(helper, "--finish-uninstall") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                return 0;
            }
            catch (Exception error) { MessageBox.Show(error.Message, "ถอนการติดตั้งไม่สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        public static int FinishUninstall()
        {
            bool manualCleanup = false;
            try {
                StopInstalledMonitor();
                Thread.Sleep(500);
                if (Product.IsClaude) {
                    try { ClaudeConfiguration.Disconnect(ClaudeConfiguration.SettingsPath, Path.Combine(InstallRoot, "ClaudeQuotaBridge.exe")); }
                    catch { manualCleanup = true; }
                }
                if (Product.IsGemini) GeminiConfiguration.Disconnect(Path.Combine(InstallRoot, "GeminiQuotaBridge.exe"));
                if (Product.IsUnified) {
                    try{ClaudeConfiguration.Disconnect(ClaudeConfiguration.SettingsPath,Path.Combine(InstallRoot,"Readers","Claude","ClaudeQuotaBridge.exe"));}catch{manualCleanup=true;}
                }
                DeleteOwnedDirectory(InstallRoot, InstallRoot);
                if (File.Exists(StartupLink)) File.Delete(StartupLink);
                if (File.Exists(MenuLink)) File.Delete(MenuLink);
                DeleteOldLinks(true);
                DeleteOldLinks(false);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                MessageBox.Show("ถอนการติดตั้งแล้ว\nเก็บธีมและค่าล่าสุดไว้สำหรับติดตั้งใหม่" + (manualCleanup ? "\n\nอ่าน Claude settings ไม่ได้ กรุณาลบคำสั่ง ClaudeQuotaBridge ของโปรแกรมออกจาก statusLine ด้วยตนเอง" : ""), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception error) { MessageBox.Show("ถอนการติดตั้งไม่สำเร็จ\n" + error.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
