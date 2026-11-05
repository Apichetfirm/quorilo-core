using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public sealed class MonitorChoice
    {
        public string Id;
        public string Nickname="ส่วนตัว";
        public bool ShowServiceName=true;
        public string FullTitle(string serviceName){return serviceName+" · "+Nickname;}
        public string CardTitle(string serviceName){return ShowServiceName?FullTitle(serviceName):Nickname;}
        public const int NicknameMaxLength=64;
        public static string NormalizeNickname(string value)
        {
            if(String.IsNullOrWhiteSpace(value))return "ส่วนตัว";
            value=value.Trim();int length=Math.Min(NicknameMaxLength,value.Length);
            // UTF-16 limits must not split a supplementary character at the boundary.
            if(length<value.Length&&length>0&&Char.IsHighSurrogate(value[length-1])&&Char.IsLowSurrogate(value[length]))length--;
            return value.Substring(0,length);
        }
        public bool Visible=true;
        public bool Paused;
        public bool Alerts=true;
        public bool AlertCurrent=true,AlertWeekly=true,ResetAlerts=true;
        public int AlertThreshold;
        public string Group="";
    }
    public sealed class UnifiedPreferences
    {
        public int Schema=2;
        public List<MonitorChoice> Monitors=new List<MonitorChoice>();
        public string Theme="Original",TimeFormat="24H",EdgeSide="Nearest";
        public bool Compact,Topmost=true,Pinned=true,EdgeShowPercent=true,Motion=true,AnimateStatus=true,AlertsEnabled;
        public bool QuietHours=true,ResetAlertsEnabled;
        public int QuietStart=22,QuietEnd=8,AlertThreshold=20,GeminiRefreshSeconds=15;
        public double Width=320,Height=580;
        public double? Left,Top;
        public DateTime? SnoozeUntil;
        public bool OnboardingViewed;
        public bool SuppressAccountLoginPrompt;
        public bool ShouldShowWelcome(bool accountLicensed,bool allowed)
        {
            return accountLicensed&&!allowed ? !SuppressAccountLoginPrompt : !OnboardingViewed;
        }
        public bool ShowInTaskbar;
        public bool KeepAwake=true;
#if !TRIAL
        public bool ShowBurnRate;
        public bool ShowQuotaHistory;
#endif
        public static readonly string[] Ids={"ChatGPT","Claude","Gemini"};
        public static readonly string[] OptionalIds={"Cursor","GitHubCopilot"};
        public static UnifiedPreferences Load(string path)
        {
            UnifiedPreferences p=null;
            try{if(File.Exists(path)&&new FileInfo(path).Length<65536)p=new JavaScriptSerializer().Deserialize<UnifiedPreferences>(File.ReadAllText(path));}catch{}
            p=p??new UnifiedPreferences();
            // Retain the legacy JSON field, but migrate every install to tray-only.
            p.ShowInTaskbar=false;
            if(p.Monitors==null)p.Monitors=new List<MonitorChoice>();
            p.Monitors=p.Monitors.Where(x=>x!=null&&HubNames.Valid(x.Id)).GroupBy(x=>x.Id).Select(x=>x.First()).ToList();
            foreach(var id in Ids)if(!p.Monitors.Any(x=>x.Id==id))p.Monitors.Add(new MonitorChoice{Id=id});
            foreach(var item in p.Monitors){item.Group=String.IsNullOrWhiteSpace(item.Group)?"":new String(item.Group.Where(c=>!Char.IsControl(c)).Take(24).ToArray()).Trim();item.AlertThreshold=item.AlertThreshold<=0?0:Math.Max(1,Math.Min(50,item.AlertThreshold));item.Nickname=MonitorChoice.NormalizeNickname(item.Nickname);}
            p.Width=Bound(p.Width,280,1400,320);p.Height=Bound(p.Height,100,1000,580);
            if(p.Left.HasValue&&(Double.IsNaN(p.Left.Value)||Double.IsInfinity(p.Left.Value)))p.Left=null;
            if(p.Top.HasValue&&(Double.IsNaN(p.Top.Value)||Double.IsInfinity(p.Top.Value)))p.Top=null;
            if(!ThemeCatalog.Contains(p.Theme))p.Theme="Original";
            if(p.TimeFormat!="12H")p.TimeFormat="24H";
            if(p.EdgeSide!="Left"&&p.EdgeSide!="Right")p.EdgeSide="Nearest";
            p.GeminiRefreshSeconds=p.GeminiRefreshSeconds==30||p.GeminiRefreshSeconds==60?p.GeminiRefreshSeconds:15;
            p.AlertThreshold=Math.Max(1,Math.Min(50,p.AlertThreshold));p.QuietStart=Math.Max(0,Math.Min(23,p.QuietStart));p.QuietEnd=Math.Max(0,Math.Min(23,p.QuietEnd));
            // Only import visual preferences on the first run. Accounts and license vault remain in place.
            if(!File.Exists(path))try{
                string old=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GeminiQuotaMonitor","settings.json");
                if(File.Exists(old)&&new FileInfo(old).Length<65536){var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(old));object v;
                    if(d.TryGetValue("Theme",out v)&&ThemeCatalog.Contains(v as string))p.Theme=(string)v;
                    if(d.TryGetValue("TimeFormat",out v)&&(v as string)=="12H")p.TimeFormat="12H";
                    if(d.TryGetValue("GeminiRefreshSeconds",out v)) {int seconds=Convert.ToInt32(v);if(seconds==15||seconds==30||seconds==60)p.GeminiRefreshSeconds=seconds;}
                }
            }catch{}
            if(!File.Exists(path))foreach(var choice in p.Monitors)try{
                string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),HubNames.Storage(choice.Id),"settings.json");
                if(File.Exists(legacy)&&new FileInfo(legacy).Length<65536){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(legacy));object value;if(data.TryGetValue("MonitoringPaused",out value)&&value is bool)choice.Paused=(bool)value;}
            }catch{}
            return p;
        }
        static double Bound(double n,double lo,double hi,double fallback){return Double.IsNaN(n)||Double.IsInfinity(n)?fallback:Math.Max(lo,Math.Min(hi,n));}
        public void Save(string path)
        {
            ShowInTaskbar=false;
            foreach(var item in Monitors)item.Nickname=MonitorChoice.NormalizeNickname(item.Nickname);
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
    }
    public sealed class HubMetric
    {
        public double remaining;
        public string resetsAt;
        public Metric ToMetric()
        {
            if(Double.IsNaN(remaining)||Double.IsInfinity(remaining)||remaining<0||remaining>100)return null;
            DateTime time;var metric=new Metric{Remaining=remaining};
            if(!String.IsNullOrEmpty(resetsAt)&&DateTime.TryParse(resetsAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out time))metric.ResetAt=time;
            return metric;
        }
    }
    public sealed class HubSnapshot
    {
        public int schema,processId,refreshSeconds;
        public bool commercial,hubWorker,paused,freshAvailable;
        public string flavor,version,state,source,detail,capability,lastReceived,updatedAt,primaryLabel,secondaryLabel,reportSummary,reportUnit,reportPeriod;
        public double? reportUsed,reportSpendUsd,reportBudgetUsd;
        public HubMetric primary,secondary;
#if !TRIAL
        public string sourceKey;
        public int observationScope;
#endif
        public bool HeartbeatCurrent {get{DateTime date;return DateTime.TryParse(updatedAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date)&&DateTime.UtcNow-date<TimeSpan.FromSeconds(25)&&DateTime.UtcNow-date>TimeSpan.FromSeconds(-5);}}
        public DateTime? Received {get{DateTime date;return DateTime.TryParse(lastReceived,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out date)?date:(DateTime?)null;}}
    }
    internal static class HubNames
    {
        public static bool Valid(string id){return id!=null&&System.Text.RegularExpressions.Regex.IsMatch(id,"^(ChatGPT|Claude|Gemini|Cursor|GitHubCopilot)(\\.[a-f0-9]{32})?\\z");}
        public static string Provider(string id){if(!Valid(id))throw new ArgumentException("Invalid monitor identity");return id.Split('.')[0];}
        public static string Slot(string id){Provider(id);return id.Contains(".")?id.Substring(id.IndexOf('.')+1):"";}
        public static bool External(string id){var provider=Provider(id);return provider=="Cursor"||provider=="GitHubCopilot";}
        public static bool Additional(string id){return Slot(id).Length!=0;}
        public static string Label(string id){return Label(id,null);}
        public static string Label(string id,string source){var provider=Provider(id);return provider=="Cursor"?"Cursor":provider=="GitHubCopilot"?"GitHub Copilot":provider=="ChatGPT"?"Codex":provider=="Claude"?"Claude Code":source!=null&&source.StartsWith("Gemini CLI",StringComparison.Ordinal)?"Gemini CLI":"Gemini Apps";}
        public static string Storage(string id){var provider=Provider(id);return (provider=="Cursor"?"QuoriloCursorMonitor":provider=="GitHubCopilot"?"QuoriloGitHubCopilotMonitor":provider=="ChatGPT"?"ChatGPTCreditMonitor":provider=="Claude"?"ClaudeCodeMonitor":"GeminiQuotaMonitor")+(Product.Trial?"Trial":"")+(Additional(id)?"-account-"+Slot(id):"");}
        public static string Executable(string id){var provider=Provider(id);return (provider=="Cursor"?"QuoriloCursorMonitor":provider=="GitHubCopilot"?"QuoriloGitHubCopilotMonitor":provider=="ChatGPT"?"ChatGPTCreditMonitor":provider=="Claude"?"ClaudeCodeMonitor":"GeminiQuotaMonitor")+".exe";}
    }
}
