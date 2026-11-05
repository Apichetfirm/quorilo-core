using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace CreditMonitor
{
    public sealed partial class Widget
    {
        readonly List<CreditMonitorWindowSignal> hubSignals = new List<CreditMonitorWindowSignal>();
        readonly DispatcherTimer hubHeartbeat = new DispatcherTimer();
        void InitializeHubWorker()
        {
            if (!Program.HubWorker) return;
            string prefix = Program.InstanceName;
            Action<string,Action> add = (name,action) => hubSignals.Add(new CreditMonitorWindowSignal(prefix+"-"+name,Window.Dispatcher,action));
            add("Refresh", async () => await RefreshAsync());
            add("Pause", () => { if(!preferences.MonitoringPaused)ToggleMonitoring(); });
            add("Resume", () => { if(preferences.MonitoringPaused)ToggleMonitoring(); });
            add("Connect", async () => {
                if(Product.AccountLicensed&&!CommercialAllowed){
                    await CheckLicenseAsync(false,true);
                    if(!CommercialAllowed){EnforceTrialExpiry();return;}
                }
                if(Product.AccountSlot.Length>0&&!Product.IsGemini){if(Product.IsClaude)ConnectClaude();await OpenAccountCli(true);return;}
                if(Product.IsGemini){await SelectGeminiSource("Background");await OpenGeminiBackgroundConnection();}
                else if(Product.IsClaude)ConnectClaude();
                else {await RefreshAsync();if(State!="Live"&&State!="Partial")System.Windows.MessageBox.Show("ลงชื่อเข้าใช้ Codex ด้วยบัญชี ChatGPT ใน Codex ก่อน แล้วกดรีเฟรช ไม่ต้องล็อกอินซ้ำใน Quorilo Monitor",Product.Name);}
            });
            add("OpenAccount",async ()=>{if(Product.AccountSlot.Length>0&&(!Product.AccountLicensed||CommercialAllowed))await OpenAccountCli(false);});
            add("Gemini15", () => SetGeminiRefreshInterval(15));
            add("Gemini30", () => SetGeminiRefreshInterval(30));
            add("Gemini60", () => SetGeminiRefreshInterval(60));
            add("LicenseReload", async () => {
                if(Product.FreeCore){await RefreshAsync();return;}
                if(licensingBusy)return;
                try { license=new LicenseClient(commerce,LicenseClient.Storage,LicenseClient.DeviceIdentity());await CheckLicenseAsync(pendingCommercialData!=null&&pendingCommercialData.HasQuota,true);await RefreshAsync(); }
                catch {SetState("LicenseRequired","ตรวจสิทธิ์ไม่ได้ · เปิดหน้าสิทธิ์ใช้งานอีกครั้ง");}
            });
            // Write initial status immediately so UnifiedMonitor observes this worker PID without waiting 5s.
            WriteStatus();
            hubHeartbeat.Interval=TimeSpan.FromSeconds(5);
            hubHeartbeat.Tick+=(s,e)=>{
                if(Program.HubParent>0){try{using(var parent=Process.GetProcessById(Program.HubParent))if(parent.HasExited){Exit();return;}}catch(ArgumentException){Exit();return;}}
                WriteStatus();
            };
            hubHeartbeat.Start();
        }
        async Task OpenAccountCli(bool login)
        {
            try{
                if(Product.AccountLicensed&&(license==null||!license.MultipleAccounts))return;
                string executable=Product.IsClaude?Product.FindClaude():await CodexUsageClient.DiscoverAsync();
                if(String.IsNullOrEmpty(executable))throw new IOException("ไม่พบ CLI ของบริการนี้");
                string command="& '"+executable.Replace("'","''")+"'"+(login?(Product.IsClaude?" auth login":" login"):"");
                command+="; Write-Host 'Quorilo Monitor: this window uses the selected isolated account.'";
                var start=new ProcessStartInfo("powershell.exe","-NoProfile -NoExit -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command))){UseShellExecute=false,CreateNoWindow=false,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)};
                Process.Start(start);
            }catch{System.Windows.MessageBox.Show("เปิด CLI ไม่สำเร็จ · ติดตั้ง CLI ของบริการแล้วลองอีกครั้ง",Product.Name);}
        }
        object SafeHubMetric(Metric metric)
        {
            return metric==null ? null : new {remaining=metric.Remaining,resetsAt=metric.ResetAt.HasValue ? metric.ResetAt.Value.ToUniversalTime().ToString("o") : null};
        }
        bool HubFreshData()
        {
            var data=pendingCommercialData??latestData;
            return data!=null&&data.HasQuota&&!data.ResetPending&&(DateTime.Now-data.ReadAt).TotalSeconds>=-5&&(DateTime.Now-data.ReadAt).TotalSeconds<=90;
        }
        void DisposeHubWorker(){hubHeartbeat.Stop();foreach(var signal in hubSignals)signal.Dispose();hubSignals.Clear();}
    }
}
