using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    internal sealed class MobileSnapshotEntry { public MonitorChoice Choice;public HubSnapshot Data; }
    internal static class MobileSnapshotPacking
    {
        static long Ms(DateTime time){return (time.ToUniversalTime().Ticks-621355968000000000L)/TimeSpan.TicksPerMillisecond;}
        static string Short(string value,int count){value=value??"";return value.Length<=count?value:value.Substring(0,Math.Max(0,count-1))+"…";}
        static object Metric(HubMetric raw,DateTime now){var value=raw==null?null:raw.ToMetric();if(value==null||value.ResetAt.HasValue&&value.ResetAt.Value<=now)return null;return new{remaining=Math.Round(value.Remaining,2),resetAt=value.ResetAt.HasValue?(long?)Ms(value.ResetAt.Value):null};}
        public static object Build(IEnumerable<MobileSnapshotEntry> source,DateTime now)
        {
            var entries=source.Take(49).ToArray();if(entries.Length>48)throw new InvalidDataException("Too many monitor accounts");
            foreach(int nameLength in new[]{24,12,4}){
                var providers=entries.Select(entry=>{
                    var choice=entry.Choice;var data=entry.Data;var result=new Dictionary<string,object>{{"id",HubNames.Provider(choice.Id)},{"observedAt",data.Received.HasValue?(object)Ms(data.Received.Value):null},{"paused",choice.Paused},{"state",data.state??"Waiting"}};
                    if(HubNames.Additional(choice.Id))result["accountSlot"]=HubNames.Slot(choice.Id);
                    if(!String.IsNullOrEmpty(choice.Nickname))result["nickname"]=Short(choice.Nickname,nameLength);
                    if(!String.IsNullOrEmpty(choice.Group))result["group"]=Short(choice.Group,nameLength);
                    var current=Metric(data.primary,now);var weekly=Metric(data.secondary,now);if(current!=null)result["current"]=current;if(weekly!=null)result["weekly"]=weekly;
                    result["currentLabel"]=data.primaryLabel;result["weeklyLabel"]=data.secondaryLabel;
                    if(HubNames.External(choice.Id)&&!String.IsNullOrEmpty(data.reportPeriod)){
                        var report=new Dictionary<string,object>{{"kind",HubNames.Provider(choice.Id)=="Cursor"?"budget":"usage"},{"period",data.reportPeriod}};
                        if(data.reportUsed.HasValue)report["used"]=data.reportUsed.Value;if(data.reportUnit!=null)report["unit"]=data.reportUnit;
                        if(data.reportSpendUsd.HasValue)report["spent"]=data.reportSpendUsd.Value;if(data.reportBudgetUsd.HasValue)report["budget"]=data.reportBudgetUsd.Value;
                        result["report"]=report;
                    }
                    return result;
                }).ToArray();
                object payload=new{observedAt=Ms(now),providers=providers};if(Encoding.UTF8.GetByteCount(new JavaScriptSerializer().Serialize(payload))<=16000)return payload;
            }
            throw new InvalidDataException("Snapshot exceeds bounded gateway contract");
        }
    }
}
