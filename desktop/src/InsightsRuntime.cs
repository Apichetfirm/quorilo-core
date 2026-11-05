using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace CreditMonitor.Insights {
 public sealed class ProviderHealth {
  public string Provider,State,Help,Scope,AccountId,AccountName;
  public DateTime? At;
  public List<QuotaSample> Quotas=new List<QuotaSample>();
 }
 public sealed class PaceNotice { public string AccountId, Scope, Window, Title, Message; }
 // Uses the existing notification channel and caller preferences. No forecast,
 // extra telemetry or persistence; accepting a tray call is not proof it was seen.
 internal sealed class PaceNotificationPolicy {
  readonly Dictionary<string,DateTime> accepted = new Dictionary<string,DateTime>(StringComparer.Ordinal);
  public bool Observe(ProviderHealth reading, DateTime utc, Func<PaceNotice,bool> tryNotify) {
   if(reading==null||reading.State!="ready"||String.IsNullOrWhiteSpace(reading.AccountId)||String.IsNullOrWhiteSpace(reading.Scope)||reading.Quotas==null||tryNotify==null||utc.Kind!=DateTimeKind.Utc||!Fresh(reading.At,utc))return false;
   bool notified=false;
   foreach(var q in reading.Quotas.Take(32)) {
    if(q==null||q.Provider!=reading.Provider||q.Scope!=reading.Scope||String.IsNullOrWhiteSpace(q.Window)||!Fresh(q.At,utc)||!q.Minutes.HasValue||Double.IsNaN(q.Minutes.Value)||Double.IsInfinity(q.Minutes.Value)||q.Minutes<=0||!q.ResetAt.HasValue||q.ResetAt.Value.Kind!=DateTimeKind.Utc||q.ResetAt.Value<=utc||Double.IsNaN(q.Remaining)||Double.IsInfinity(q.Remaining)||q.Remaining<0||q.Remaining>100)continue;
    double elapsed=100*(1-(q.ResetAt.Value-utc).TotalMinutes/q.Minutes.Value);
    if(elapsed<0||elapsed>100||Pace.Assess(100-q.Remaining,elapsed).Status!="ahead")continue;
    string key=InsightIdentity.Hash(reading.AccountId+"|"+q.Provider+"|"+q.Scope+"|"+q.Window+"|"+q.ResetAt.Value.Ticks);DateTime prior;
    if(accepted.TryGetValue(key,out prior)&&(utc<prior||utc-prior<=TimeSpan.FromHours(1)))continue;
    var notice=new PaceNotice{AccountId=reading.AccountId,Scope=q.Scope,Window=q.Window,Title="ใช้เครดิตเร็วกว่ารอบเวลา",Message=q.Provider+" · "+q.Window+" ใช้ไป "+(100-q.Remaining).ToString("0")+"% · ผ่านรอบมา "+elapsed.ToString("0")+"%"};
    bool delivered=false;try{delivered=tryNotify(notice);}catch{delivered=false;}
    if(!delivered)continue;
    accepted[key]=utc;notified=true;
    if(accepted.Count>512)accepted.Remove(accepted.OrderBy(x=>x.Value).ThenBy(x=>x.Key,StringComparer.Ordinal).First().Key);
   }
   return notified;
  }
  static bool Fresh(DateTime? at,DateTime now){return at.HasValue&&at.Value.Kind==DateTimeKind.Utc&&at.Value>DateTime.MinValue&&at.Value<=now&&now-at.Value<=TimeSpan.FromSeconds(90);}
 }
 public sealed class InsightsRuntime : IDisposable {
  readonly Func<bool> allowed;readonly Func<string,bool> toolAllowed;
  readonly object healthGate=new object();readonly Dictionary<string,ProviderHealth> health=new Dictionary<string,ProviderHealth>();
  readonly PaceNotificationPolicy paceNotifications=new PaceNotificationPolicy();
  readonly CancellationTokenSource cancel=new CancellationTokenSource();
  int scanning,recovering;DateTime nextScan;bool disposed;volatile bool enabled;readonly object lifecycleGate=new object();
  public HistoryStore Store {get;private set;}public InsightQueries Queries {get;private set;}
  public string Device {get;private set;}
  public bool IsHistoryReady {get{return Store!=null&&!disposed;}}public bool RecoveryRequired {get;private set;}public string RecoveryBackup {get;private set;}
  public MonthlyBudgetStore Budgets {get;private set;}
  public Func<PaceNotice,bool> TryAlert;public Func<string> TaskbarStatus;public Action RequestSync;public bool CloudServicesAvailable=true;public bool HasAccess{get{return !disposed&&allowed();}}
  public string Status="ยังไม่ได้เปิดเก็บประวัติในเครื่อง";
  public bool Enabled {get{return enabled&&IsHistoryReady;}set{if(!IsHistoryReady)return;Store.Set("enabled",value?"true":"false");enabled=value;}}
  public bool Privacy {get{return !IsHistoryReady||Store.Setting("privacy","false")=="true";}set{if(IsHistoryReady)Store.Set("privacy",value?"true":"false");}}
  public bool PaceAlerts {get{return IsHistoryReady&&Store.Setting("paceAlerts","false")=="true";}set{if(IsHistoryReady)Store.Set("paceAlerts",value?"true":"false");}}
  public InsightsRuntime(string root,Func<bool> hasAccess,Func<string,bool> enabledTool){allowed=hasAccess;toolAllowed=enabledTool;StorePath=Path.Combine(root,"Insights","history.sqlite");if(InsightRecovery.IsCorrupt(StorePath)){RecoveryRequired=true;Status="ฐานประวัติเสียหาย · เปิด Insights เพื่อเก็บสำเนาและเริ่มประวัติใหม่";return;}try{OpenHistory();}catch{RecoveryRequired=InsightRecovery.IsCorrupt(StorePath);Status=RecoveryRequired?"ฐานประวัติเสียหาย · เปิด Insights เพื่อเก็บสำเนาและเริ่มประวัติใหม่":"เปิดฐานประวัติไม่ได้ · ตรวจสิทธิ์พื้นที่เก็บข้อมูลหรือใช้แอปรุ่นที่รองรับ";}}
  void OpenHistory(){var opened=new HistoryStore(StorePath);try{string device=opened.Setting("device","");if(device==""){device=Guid.NewGuid().ToString("N");opened.Set("device",device);}Device=device;Queries=new InsightQueries(opened,InsightPrices.Defaults());Budgets=new MonthlyBudgetStore(opened);enabled=opened.Setting("enabled","false")=="true";Store=opened;}catch{opened.Dispose();throw;}}
  public async Task RecoverHistoryAsync(){if(disposed||!allowed()||!RecoveryRequired||IsHistoryReady||Interlocked.CompareExchange(ref recovering,1,0)!=0)throw new InvalidOperationException("ประวัติยังไม่พร้อมกู้คืน");try{await Task.Run(()=>{lock(lifecycleGate){if(disposed||!allowed())throw new InvalidOperationException("ประวัติยังไม่พร้อมกู้คืน");RecoveryBackup=InsightRecovery.Preserve(StorePath);RecoveryRequired=false;OpenHistory();enabled=false;Store.Set("enabled","false");Store.Set("cloudSync","false");Status="เก็บสำเนาฐานเดิมและเริ่มประวัติใหม่แล้ว · การเก็บและซิงค์ยังปิดอยู่";}});}finally{Interlocked.Exchange(ref recovering,0);}}
  public List<ProviderHealth> Health(){lock(healthGate)return health.Values.OrderBy(x=>x.Provider).Select(x=>new ProviderHealth{Provider=x.Provider,AccountId=x.AccountId,AccountName=x.AccountName,State=x.State,Help=x.Help,Scope=x.Scope,At=x.At,Quotas=x.Quotas.ToList()}).ToList();}
  public void ForgetAccount(string id){lock(healthGate)health.Remove(id);}
  public void Observe(ProviderHealth reading){if(disposed||reading==null)return;lock(healthGate)health[String.IsNullOrEmpty(reading.AccountId)?reading.Provider:reading.AccountId]=reading;if(!allowed()||!Enabled||reading.State!="ready")return;foreach(var q in reading.Quotas)Store.Quota(q);if(PaceAlerts)paceNotifications.Observe(reading,DateTime.UtcNow,TryAlert);}
  public Task ScanAsync(bool force=false){if(disposed||!Enabled||!allowed()||(!force&&DateTime.UtcNow<nextScan)||Interlocked.CompareExchange(ref scanning,1,0)!=0)return Task.FromResult(0);nextScan=DateTime.UtcNow.AddMinutes(2);return Task.Run(()=>{int scanned=0,failed=0;try{Status="กำลังจัดทำดัชนีประวัติในเครื่อง…";string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var roots=new[]{new{Tool="Codex",Path=Path.Combine(user,".codex","sessions")},new{Tool="Claude",Path=Path.Combine(user,".claude","projects")}};foreach(var root in roots){if(!toolAllowed(root.Tool)||!Directory.Exists(root.Path))continue;foreach(string file in Directory.EnumerateFiles(root.Path,"*.jsonl",SearchOption.AllDirectories)){if(cancel.IsCancellationRequested||!Enabled||!allowed())return;try{var info=new FileInfo(file);string mark=info.Length+"|"+info.LastWriteTimeUtc.Ticks;string key="scan:"+InsightIdentity.Hash(file);if(Store.Setting(key,"")==mark)continue;if(info.Length>128*1024*1024){failed++;continue;}if(new FileInfo(Path.Combine(userStoreRoot(),"history.sqlite")).Length>250L*1024*1024){Status="ฐานข้อมูลถึงเพดาน 250 MB · หยุดนำเข้าเพื่อรักษาประวัติ";return;}UsageImportCoordinator.ImportFile(Store,root.Tool,file,Device,"local-"+InsightIdentity.Hash(root.Path).Substring(0,12),()=>!cancel.IsCancellationRequested&&Enabled&&allowed());if(!cancel.IsCancellationRequested&&Enabled&&allowed())Store.Set(key,mark);scanned++;}catch{failed++;}}}Store.Maintain(DateTime.UtcNow);Status="จัดทำดัชนีแล้ว "+scanned+" ไฟล์ที่เปลี่ยน · ข้าม/อ่านไม่ได้ "+failed+" · "+DateTime.Now.ToString("HH:mm");Store.Set("lastScan",InsightIdentity.Time(DateTime.UtcNow));}catch{Status="อ่านประวัติไม่สำเร็จ · quota เดิมยังแสดงได้";}finally{Interlocked.Exchange(ref scanning,0);}},cancel.Token);}
  string userStoreRoot(){return Path.GetDirectoryName(StorePath);}
  public string StorePath {get;private set;}
  public void SaveBudget(string currency,double limit,double paid){if(!IsHistoryReady)throw new FormatException("ประวัติยังไม่พร้อม");string month=MonthlyBudgetStore.CurrentMonth;Budgets.Save(new MonthlyBudgetItem{Id="legacy-"+month,Month=month,Name="ยอดรวมที่กรอก",Currency=currency,Limit=limit,Paid=paid});}
  public string BudgetText(){if(!IsHistoryReady)return "ประวัติยังไม่พร้อม";var totals=Budgets.Totals(MonthlyBudgetStore.CurrentMonth);return totals.Count==0?"ยังไม่ตั้งงบและยอดจ่ายจริงของเดือนนี้":String.Join(" / ",totals.Select(x=>"ยอดจ่าย "+x.Paid.ToString("N2")+" / งบ "+x.Limit.ToString("N2")+" "+x.Currency))+" · แยกจาก API estimate";}
  public async Task ClearAsync(){if(!IsHistoryReady)return;Enabled=false;Store.Set("cloudSync","false");while(Interlocked.CompareExchange(ref scanning,0,0)!=0)await Task.Delay(50);await Task.Run(()=>Store.ClearHistory());Status="ลบประวัติในเครื่องแล้ว · ปิดเก็บและ sync · หากเปิดเก็บใหม่จะอ่าน logs ที่ยังอยู่";}
  public void Dispose(){lock(lifecycleGate){if(disposed)return;disposed=true;cancel.Cancel();if(Store==null)return;if(Interlocked.CompareExchange(ref scanning,0,0)==0)Store.Dispose();else Task.Run(()=>{while(Interlocked.CompareExchange(ref scanning,0,0)!=0)Thread.Sleep(50);Store.Dispose();});}}
 }
}
