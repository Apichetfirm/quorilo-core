using System;





using System.IO;





using System.Linq;





using System.Threading.Tasks;





using System.Collections.Concurrent;





using System.Windows.Threading;





namespace CreditMonitor





{





    public static class StartupProfile
    {
        static readonly long OriginTicks=System.Diagnostics.Stopwatch.GetTimestamp();
        static readonly bool Enabled=String.Equals(Environment.GetEnvironmentVariable("QUORILO_STARTUP_PROFILE"),"1",StringComparison.Ordinal);
        static readonly object Sync=new object();
        public static double ElapsedMilliseconds { get { return (System.Diagnostics.Stopwatch.GetTimestamp()-OriginTicks)*1000.0/System.Diagnostics.Stopwatch.Frequency; } }
        public static void Mark(string stage,string detail=null)
        {
            if(!Enabled)return;
            try{
                string configured=Environment.GetEnvironmentVariable("QUORILO_STARTUP_PROFILE_PATH");
                string file=String.IsNullOrWhiteSpace(configured)?Path.Combine(Product.Storage,"startup-profile.jsonl"):Path.GetFullPath(configured);
                string directory=Path.GetDirectoryName(file);if(!String.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
                string clean=(detail??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ");
                string line="{\"utc\":\""+DateTime.UtcNow.ToString("o",System.Globalization.CultureInfo.InvariantCulture)+"\",\"pid\":"+System.Diagnostics.Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"component\":\""+Product.StorageName+"\",\"stage\":\""+stage+"\",\"elapsedMs\":"+ElapsedMilliseconds.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+(String.IsNullOrEmpty(clean)?"":",\"detail\":\""+clean+"\"")+"}"+Environment.NewLine;
                lock(Sync)using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))writer.Write(line);
            }catch{}
        }
    }
    public static class Product





    {

#if TRIAL

        public const bool Trial = true;

#else

        public const bool Trial = false;

#endif

#if COMMERCIAL

        public const bool Commercial = true;

#else

        public const bool Commercial = false;

#endif







#if FREE_CORE && (COMMERCIAL || TRIAL || STAGING_QA)
#error Free Core cannot be combined with Account, Trial or private QA flags.
#endif
#if FREE_CORE
        public const bool FreeCore = true;
#else
        public const bool FreeCore = false;
#endif
        public const bool Owner = !Commercial && !FreeCore;
        public const bool LocalCoreAccess = FreeCore || Owner;
        public const string LocalCoreScope = FreeCore ? "free-core" : "owner";
        public const string Edition = FreeCore ? "Free Core" : StagingQa ? "Private QA" : Commercial ? "Account" : "Owner";
        public const bool AccountLicensed = Commercial;
#if STAGING_QA
        public const bool StagingQa=true;
#else
        public const bool StagingQa=false;
#endif


#if UNIFIED

        public const bool IsUnified=true, IsClaude=false, IsGemini=false;

        public const string Name=StagingQa?"Quorilo Monitor QA":Trial?"Quorilo Monitor Trial":"Quorilo Monitor", Label="ChatGPT", Source="Codex / Claude / Gemini", BaseStorageName=Trial?"CreditMonitorFirmImpulseTrial":"CreditMonitorFirmImpulse", Executable="CreditMonitor.exe", Version=FreeCore?"0.5.3-free-core-preview":StagingQa?"0.5.3-qa":"0.5.3-preview", AssemblyVersion=Commercial?"0.5.3.0":"0.5.3.0", Channel="Unified";

#elif GEMINI

        public const bool IsUnified=false;

        public const bool IsClaude = false, IsGemini = true;

        public const string Name = "Quorilo Monitor · Gemini"+(StagingQa?" QA":""), Label = "Gemini", Source = "Gemini · 5h / Weekly", BaseStorageName = Trial?"GeminiQuotaMonitorTrial":"GeminiQuotaMonitor", Executable = "GeminiQuotaMonitor.exe", Version = "0.1.8-preview", AssemblyVersion = "0.1.8.0", Channel = "Apps";

#elif CLAUDE

        public const bool IsUnified=false;

        public const bool IsGemini = false;

        public const string Channel = "Code";





        public const bool IsClaude = true;





        public const string Name = "Quorilo Monitor · Claude"+(StagingQa?" QA":""), Label = "Claude", Source = "Claude Code statusLine", BaseStorageName = Trial?"ClaudeCodeMonitorTrial":"ClaudeCodeMonitor", Executable = "ClaudeCodeMonitor.exe", Version = "0.3.6-preview", AssemblyVersion = "0.3.6.0";





#else

        public const bool IsUnified=false;

        public const bool IsGemini = false;

        public const string Channel = "Codex";





        public const bool IsClaude = false;





        public const string Name = "Quorilo Monitor · Codex"+(StagingQa?" QA":""), Label = "ChatGPT", Source = "Codex · บัญชี ChatGPT", BaseStorageName = Trial?"ChatGPTCreditMonitorTrial":"ChatGPTCreditMonitor", Executable = "ChatGPTCreditMonitor.exe", Version = "0.8.6-preview", AssemblyVersion = "0.8.6.0";





#endif





        public static string AccountSlot { get { var slot=Environment.GetEnvironmentVariable("QUORILO_ACCOUNT_SLOT")??""; if(slot.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(slot,"^[a-f0-9]{32}\\z"))throw new InvalidOperationException("Invalid account slot");return IsUnified?"":slot; } }
        public static string StorageName {get{return BaseStorageName+(StagingQa?"-StagingQA":"")+(AccountSlot.Length==0?"":"-account-"+AccountSlot);}}
        public static string Storage { get { string custom = Environment.GetEnvironmentVariable("CREDIT_MONITOR_DATA_DIR") ?? (IsClaude ? Environment.GetEnvironmentVariable("CLAUDE_MONITOR_DATA_DIR") : null); return String.IsNullOrWhiteSpace(custom) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StorageName) : Path.GetFullPath(custom); } }





        private static string cachedClaudePath;
        private static DateTime cachedClaudeAt = DateTime.MinValue;
        public static string FindClaude()
        {
            if (cachedClaudePath != null && DateTime.UtcNow - cachedClaudeAt < TimeSpan.FromSeconds(30) && File.Exists(cachedClaudePath)) return cachedClaudePath;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] priorityDirs = new[] { Path.Combine(userProfile, ".local", "bin"), Path.Combine(appData, "npm"), Path.Combine(localAppData, "Programs", "claude"), Path.Combine(localAppData, "AnthropicClaude") };
            string[] pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            // Prefer local directories and defer UNC paths so an unavailable network share cannot stall startup.
            foreach (bool allowUnc in new[] { false, true })
                foreach (string rawDir in allowUnc ? pathDirs : priorityDirs.Concat(pathDirs))
                    try {
                        string dir = (rawDir ?? "").Trim().Trim('"');
                        if (String.IsNullOrEmpty(dir) || dir.StartsWith(@"\\", StringComparison.Ordinal) != allowUnc) continue;
                        foreach (string name in new[] { "claude.exe", "claude.cmd", "claude" }) {
                            string path = Path.Combine(dir, name);
                            if (File.Exists(path)) { cachedClaudePath = path; cachedClaudeAt = DateTime.UtcNow; return path; }
                        }
                    } catch { }
            return null;
        }





        public static bool TestFeed { get { return Owner && !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CREDIT_MONITOR_FEED")); } }





    }





}
