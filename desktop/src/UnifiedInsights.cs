using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using CreditMonitor.Insights;
namespace CreditMonitor {
 public sealed partial class UnifiedMonitor {
  InsightsRuntime insights;InsightsView insightsWindow;InsightsTaskbar taskbarMeter;DateTime insightNext;string insightError;
  void SetupInsights(){try{insights=new InsightsRuntime(Product.Storage,()=>Allowed,id=>Allowed&&preferences.Monitors.Any(x=>x.Id==(id=="Codex"?"ChatGPT":id)&&!x.Paused));insights.CloudServicesAvailable=!Product.FreeCore;insights.RequestSync=()=>{if(Product.FreeCore)return;Companion.RetryHistory();companionAt=DateTime.MinValue;SyncCompanion();};insights.TaskbarStatus=()=>taskbarMeter==null?"Taskbar meter ปิดอยู่":taskbarMeter.Status;if(insights.IsHistoryReady&&insights.Store.Setting("meterEnabled","false")=="true"){taskbarMeter=new InsightsTaskbar(OpenInsights);taskbarMeter.SetEnabled(true);}insights.TryAlert=notice=>{
    var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==notice.AccountId);HubSnapshot current;snapshots.TryGetValue(notice.AccountId,out current);
    if(tray==null||disposed||choice==null||!QuotaNotificationPolicy.CanNotifyWindow(choice,current,notice.Window,preferences,AccountAllowed(notice.AccountId),DateTime.Now)||!Fresh(current)||!current.HeartbeatCurrent)return false;
    string scope=choice.Id;
#if !TRIAL
    scope+="|"+current.sourceKey+"|"+current.observationScope;
#endif
    if(InsightIdentity.Hash(scope)!=notice.Scope)return false;
    tray.ShowBalloonTip(6000,notice.Title,notice.Message,System.Windows.Forms.ToolTipIcon.Warning);return true;
   };}catch{insightError="เปิดฐานประวัติไม่ได้ · ตรวจสิทธิ์พื้นที่เก็บข้อมูลหรือ Windows SQLite";}}
  void OpenInsights(){if(!Allowed){OpenSettings();return;}if(insights==null){MessageBox.Show(insightError??"ประวัติยังไม่พร้อม",Product.Name);return;}if(insightsWindow==null){insightsWindow=new InsightsView(insights,OpenSettings,enabled=>{if(!insights.IsHistoryReady)return;if(taskbarMeter==null)taskbarMeter=new InsightsTaskbar(OpenInsights);insights.Store.Set("meterEnabled",enabled?"true":"false");taskbarMeter.SetEnabled(enabled);},!Theme.Light);insightsWindow.Closed+=(s,e)=>insightsWindow=null;}insightsWindow.Show();insightsWindow.Activate();}
  void UpdateInsights(){if(insights==null)return;try{if(!Allowed&&insightsWindow!=null){insightsWindow.Close();insightsWindow=null;}foreach(var choice in preferences.Monitors){HubSnapshot data;snapshots.TryGetValue(choice.Id,out data);bool live=AccountAllowed(choice.Id)&&!choice.Paused&&Fresh(data)&&data.HeartbeatCurrent;string scope=choice.Id;
#if !TRIAL
    if(data!=null)scope+="|"+data.sourceKey+"|"+data.observationScope;
#endif
    var reading=new ProviderHealth{Provider=ServiceLabel(choice.Id),AccountId=choice.Id,AccountName=choice.Nickname,Scope=InsightIdentity.Hash(scope),State=live?"ready":choice.Paused?"paused":!Allowed?"access-required":data==null?"waiting":data.state??"unavailable",Help=live?"อ่านโควตาได้ · กิจกรรม token มาจาก local logs แยกต่างหาก":choice.Paused?"พักการอ่านอยู่":data==null?"เปิดการเชื่อมต่อในตั้งค่า":data.detail??"ตรวจการเชื่อมบัญชีในตั้งค่า",At=data==null?null:data.Received};
    if(live)for(int i=0;i<2;i++){var metric=CurrentMetric(data,i==0);if(metric==null)continue;string label=i==0?data.primaryLabel:data.secondaryLabel;double? minutes=HubNames.Provider(choice.Id)=="Gemini"?null:label=="5h"?(double?)300:label=="Weekly"?(double?)10080:null;reading.Quotas.Add(new QuotaSample{Provider=reading.Provider,Scope=reading.Scope,Window=label??"Quota",At=data.Received.Value,Remaining=metric.Remaining,ResetAt=metric.ResetAt,Minutes=minutes});}insights.Observe(reading);}
    if(taskbarMeter!=null&&insights.IsHistoryReady){taskbarMeter.SetEnabled(Allowed&&insights.Store.Setting("meterEnabled","false")=="true");taskbarMeter.ProviderFilter=insights.Store.Setting("meterProvider","");taskbarMeter.WindowFilter=insights.Store.Setting("meterWindow","");taskbarMeter.UpdateReadings(insights.Health());}
    if(DateTime.UtcNow>insightNext){insightNext=DateTime.UtcNow.AddSeconds(30);var scanning=insights.ScanAsync();string text=String.Join(" · ",insights.Health().Where(h=>h.State=="ready").SelectMany(h=>h.Quotas).OrderBy(q=>q.Remaining).Take(2).Select(q=>q.Provider+" "+q.Remaining.ToString("0")+"%"));tray.Text=("Quorilo · "+(text==""?"รอค่าจากบริการ":text)).Substring(0,Math.Min(63,("Quorilo · "+(text==""?"รอค่าจากบริการ":text)).Length));}
   }catch{insightError="ประวัติไม่พร้อม · Monitor โควตายังทำงานต่อ";}}
  void DisposeInsights(){if(insightsWindow!=null)insightsWindow.Close();if(taskbarMeter!=null)taskbarMeter.Dispose();if(insights!=null)insights.Dispose();}
 }
}
