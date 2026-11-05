using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CreditMonitor
{
    internal sealed class QuotaNotice
    {
        public string AccountId,Kind,Window,Message;
    }
    internal sealed class QuotaNotificationPolicy
    {
        sealed class Observation { public DateTime Reset,At;public string Identity; }
        readonly Dictionary<string,Observation> previous=new Dictionary<string,Observation>();
        readonly HashSet<string> keys;readonly Queue<string> keyOrder=new Queue<string>();
        public IEnumerable<string> Keys{get{return keys;}}
        public QuotaNotificationPolicy(HashSet<string> consumed=null){keys=consumed??new HashSet<string>();foreach(var key in keys)keyOrder.Enqueue(key);while(keyOrder.Count>512)keys.Remove(keyOrder.Dequeue());}
        static string Digest(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
        bool Consume(string key){string digest=Digest(key);if(!keys.Add(digest))return false;keyOrder.Enqueue(digest);while(keyOrder.Count>512)keys.Remove(keyOrder.Dequeue());return true;}
        public void Forget(string id){previous.Remove(id+":0");previous.Remove(id+":1");}
        internal static bool CanNotify(MonitorChoice choice, UnifiedPreferences preferences, bool allowed, DateTime localNow)
        {
            if (choice == null || preferences == null || !allowed || !preferences.AlertsEnabled || !choice.Alerts || choice.Paused) return false;
            bool quiet = preferences.QuietHours && (preferences.QuietStart == preferences.QuietEnd || (preferences.QuietStart < preferences.QuietEnd ? localNow.Hour >= preferences.QuietStart && localNow.Hour < preferences.QuietEnd : localNow.Hour >= preferences.QuietStart || localNow.Hour < preferences.QuietEnd));
            return !quiet && (!preferences.SnoozeUntil.HasValue || preferences.SnoozeUntil.Value <= localNow);
        }
        internal static bool CanNotifyWindow(MonitorChoice choice, HubSnapshot data, string window, UnifiedPreferences preferences, bool allowed, DateTime localNow)
        {
            if (!CanNotify(choice, preferences, allowed, localNow) || data == null || String.IsNullOrWhiteSpace(window)) return false;
            // Ambiguous equal labels must satisfy both window preferences.
            bool first = window == data.primaryLabel, second = window == data.secondaryLabel;
            return (first || second) && (!first || choice.AlertCurrent) && (!second || choice.AlertWeekly);
        }
        public List<QuotaNotice> Observe(MonitorChoice choice,HubSnapshot data,UnifiedPreferences preferences,DateTime utcNow,DateTime localNow,bool allowed)
        {
            var output=new List<QuotaNotice>();
            if(!CanNotify(choice,preferences,allowed,localNow)){if(choice!=null)Forget(choice.Id);return output;}
            string identity=data==null?"":data.source??"";
#if !TRIAL
            identity+="|"+(data==null?"":data.sourceKey??"");
#endif
            foreach(var item in previous.Where(pair=>pair.Key.StartsWith(choice.Id+":",StringComparison.Ordinal)).ToArray())if(data!=null&&item.Value.Identity!=identity||utcNow-item.Value.Reset>TimeSpan.FromMinutes(5))previous.Remove(item.Key);
            if(!ReadingPresentation.Describe(HubNames.Provider(choice.Id),data,false,true,false,utcNow).Verified)return output;
            int threshold=choice.AlertThreshold>0?Math.Max(1,Math.Min(50,choice.AlertThreshold)):preferences.AlertThreshold;
            for(int index=0;index<2;index++){
                string slot=choice.Id+":"+index;var raw=index==0?data.primary:data.secondary;var metric=raw==null?null:raw.ToMetric();
                if(metric==null||!metric.ResetAt.HasValue||metric.ResetAt.Value<=utcNow)continue;
                var reset=metric.ResetAt.Value.ToUniversalTime();var label=index==0?data.primaryLabel:data.secondaryLabel;Observation last;
                if(preferences.ResetAlertsEnabled&&choice.ResetAlerts&&(index==0?choice.AlertCurrent:choice.AlertWeekly)&&previous.TryGetValue(slot,out last)&&last.Identity==identity&&last.Reset<=utcNow&&reset>last.Reset&&data.Received.Value>=last.Reset&&utcNow-last.Reset<=TimeSpan.FromMinutes(5)&&Consume(slot+identity+":reset:"+reset.Ticks))
                    output.Add(new QuotaNotice{AccountId=choice.Id,Kind="reset",Window=label,Message=label+" · รับข้อมูลรอบใหม่แล้ว"});
                previous[slot]=new Observation{Reset=reset,At=data.Received.Value,Identity=identity};
                if((index==0?choice.AlertCurrent:choice.AlertWeekly)&&metric.Remaining<=threshold&&Consume(slot+identity+":low:"+reset.Ticks+":"+threshold))
                    output.Add(new QuotaNotice{AccountId=choice.Id,Kind="low",Window=label,Message=label+" เหลือ "+metric.Display});
            }
            return output;
        }
    }
    internal static class NotificationQueue
    {
        public static void Merge(List<QuotaNotice> target,IEnumerable<QuotaNotice> incoming,IEnumerable<string> accounts)
        {
            var allowed=new HashSet<string>(accounts);target.RemoveAll(notice=>!allowed.Contains(notice.AccountId));
            foreach(var notice in incoming){if(!allowed.Contains(notice.AccountId))continue;target.RemoveAll(old=>old.AccountId==notice.AccountId&&old.Kind==notice.Kind&&old.Window==notice.Window);target.Add(notice);}
        }
    }
    public sealed partial class UnifiedMonitor
    {
        QuotaNotificationPolicy notificationPolicy;
        readonly List<QuotaNotice> currentNotices=new List<QuotaNotice>();
        void CheckAccountNotifications()
        {
            if(tray==null)return;
            if(notificationPolicy==null)notificationPolicy=new QuotaNotificationPolicy(alerted);
            var pending=new List<QuotaNotice>();
            foreach(var choice in preferences.Monitors){HubSnapshot data;snapshots.TryGetValue(choice.Id,out data);pending.AddRange(notificationPolicy.Observe(choice,data,preferences,DateTime.UtcNow,DateTime.Now,AccountAllowed(choice.Id)));}
            if(pending.Count==0||tray==null)return;
            NotificationQueue.Merge(currentNotices,pending,preferences.Monitors.Select(choice=>choice.Id));
            try{System.IO.Directory.CreateDirectory(Product.Storage);System.IO.File.WriteAllText(alertPath,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(notificationPolicy.Keys.Take(512).ToArray()));}catch{}
            var messages=currentNotices.Select(notice=>{var choice=preferences.Monitors.FirstOrDefault(c=>c.Id==notice.AccountId);return ServiceLabel(notice.AccountId)+" · "+(choice==null?"":choice.Nickname)+" · "+notice.Message;});
            tray.ShowBalloonTip(8000,"Quorilo Monitor · เครดิตและรอบรีเซ็ต",NotificationSummary(currentNotices.Count,messages),System.Windows.Forms.ToolTipIcon.Info);
        }
        static string NotificationSummary(int count,IEnumerable<string> messages){string text="มีแจ้งเตือน "+count+" รายการ\n"+String.Join("\n",messages.Take(2))+"\nคลิกเพื่อเลือกบัญชีและดูรายละเอียด";return text.Length>240?text.Substring(0,205)+"…\nคลิกดูทุกบัญชีที่แจ้ง":text;}
        void ShowNotificationAccounts()
        {
            if(disposed||!windowLifecycle.CanShow)return;
            var ids=currentNotices.Select(x=>x.AccountId).Distinct().Where(id=>preferences.Monitors.Any(c=>c.Id==id)).ToArray();
            if(ids.Length==0){Show();return;}
            if(ids.Length==1){ShowNotificationAccount(ids[0]);return;}
            var menu=new System.Windows.Forms.ContextMenuStrip();foreach(var id in ids){var selected=id;var choice=preferences.Monitors.First(c=>c.Id==id);menu.Items.Add(ServiceLabel(id)+" · "+choice.Nickname,null,(s,e)=>ShowNotificationAccount(selected));}
            menu.Closed+=(s,e)=>menu.Dispose();menu.Show(System.Windows.Forms.Cursor.Position);
        }
        void ShowNotificationAccount(string id)
        {
            var choice=preferences.Monitors.FirstOrDefault(c=>c.Id==id);if(choice==null)return;currentNotices.RemoveAll(notice=>notice.AccountId==id);choice.Visible=true;Save();LayoutCards();Show();
            MonitorView view;if(views.TryGetValue(id,out view)){view.Card.BringIntoView();view.Primary.Track.Focus();}
        }
    }
}
