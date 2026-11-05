using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CreditMonitor
{
    internal sealed class AdditionalProviderRuntime:IDisposable
    {
        sealed class Entry { public HubSnapshot Data;public DateTime Next;public bool Loading,Paused;public CancellationTokenSource Cancel; }
        readonly object sync=new object();readonly string root;readonly AdditionalProviderClient client;
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();readonly HashSet<string> retired=new HashSet<string>();
        string scope;bool disposed;
        public AdditionalProviderRuntime(string rootDirectory,AdditionalProviderClient transport=null){root=Path.GetFullPath(rootDirectory);client=transport??new AdditionalProviderClient();}
        AdditionalProviderVault Vault(){if(scope==null)throw new InvalidOperationException("AccountScopeRequired");return new AdditionalProviderVault(Path.Combine(root,"additional-api",scope));}
        public void SetScope(string value)
        {
            if(value!=null&&value!="owner"&&!(Product.FreeCore&&value==Product.LocalCoreScope)&&!System.Text.RegularExpressions.Regex.IsMatch(value,"^[a-f0-9]{64}\\z"))throw new ArgumentException("Invalid account scope");
            lock(sync){if(scope==value)return;StopAll();retired.Clear();scope=value;}
        }
        public AdditionalProviderCredential Credential(string id){lock(sync){return scope==null?null:Vault().Load(id);}}
        public void Configure(string id,AdditionalProviderCredential credential){lock(sync){if(disposed||scope==null)throw new InvalidOperationException("AccountScopeRequired");Stop(id);retired.Remove(id);Vault().Save(id,credential);}}
        public void Forget(string id){lock(sync){Stop(id);retired.Add(id);if(scope!=null)Vault().Forget(id);}}
        public void Ensure(string id)
        {
            Entry entry;AdditionalProviderCredential credential;string expectedScope;
            lock(sync){
                if(disposed||scope==null||retired.Contains(id))return;
                if(!entries.TryGetValue(id,out entry)){entry=new Entry();entries[id]=entry;}
                if(entry.Paused||entry.Loading||entry.Next>DateTime.UtcNow)return;
                credential=Vault().Load(id);
                if(credential==null){entry.Data=Status(id,"LoginRequired","เปิดการเชื่อมต่อเพื่อใส่ credential แบบ read-only",null);entry.Next=DateTime.UtcNow.AddMinutes(1);return;}
                entry.Loading=true;entry.Cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));expectedScope=scope;
            }
            var unused=Read(id,entry,credential,expectedScope);
        }
        async Task Read(string id,Entry entry,AdditionalProviderCredential credential,string expectedScope)
        {
            var cancellation=entry.Cancel;HubSnapshot result=null;
            try{result=await client.ReadAsync(id,credential,DateTime.UtcNow,cancellation.Token).ConfigureAwait(false);}
            catch(OperationCanceledException){}
            catch(Exception error){result=Status(id,error is InvalidOperationException&&error.Message=="ProviderPermissionRequired"?"LoginRequired":"Error",error is InvalidOperationException&&error.Message=="ProviderRateLimited"?"ผู้ให้บริการจำกัดคำขอ · รอรอบถัดไป":"อ่านรายงานไม่ได้ · ตรวจ credential แบบ read-only และสิทธิ์บัญชี",entry.Data);}
            finally{
                lock(sync){Entry active;if(!disposed&&scope==expectedScope&&entries.TryGetValue(id,out active)&&Object.ReferenceEquals(active,entry)&&!cancellation.IsCancellationRequested){if(result!=null)entry.Data=result;entry.Next=DateTime.UtcNow.AddSeconds(HubNames.Provider(id)=="Cursor"?3600:300);}entry.Loading=false;}
                cancellation.Dispose();
            }
        }
        static HubSnapshot Status(string id,string state,string detail,HubSnapshot previous)
        {
            return new HubSnapshot{schema=2,flavor=HubNames.Provider(id),commercial=Product.Commercial,hubWorker=true,processId=System.Diagnostics.Process.GetCurrentProcess().Id,state=state,detail=detail,capability=previous==null?"Unknown":previous.capability,source=HubNames.Label(id)+" official API",primaryLabel=previous==null?"Usage":previous.primaryLabel,secondaryLabel="Report",updatedAt=DateTime.UtcNow.ToString("o"),lastReceived=previous==null?null:previous.lastReceived,reportSummary=previous==null?null:previous.reportSummary,reportPeriod=previous==null?null:previous.reportPeriod,reportUnit=previous==null?null:previous.reportUnit,reportUsed=previous==null?null:previous.reportUsed,reportSpendUsd=previous==null?null:previous.reportSpendUsd,reportBudgetUsd=previous==null?null:previous.reportBudgetUsd,primary=previous==null?null:previous.primary};
        }
        public HubSnapshot Read(string id){lock(sync){Entry entry;if(!entries.TryGetValue(id,out entry)||entry.Data==null)return null;entry.Data.updatedAt=DateTime.UtcNow.ToString("o");entry.Data.paused=entry.Paused;return entry.Data;}}
        public bool Refreshing(string id){lock(sync){Entry e;return entries.TryGetValue(id,out e)&&e.Loading;}}
        public bool Send(string id,string action)
        {
            lock(sync){if(disposed||scope==null||retired.Contains(id))return false;Entry entry;if(!entries.TryGetValue(id,out entry)){entry=new Entry();entries[id]=entry;}
                if(action=="Stop"){Stop(id);return true;}
                if(action=="Pause"){entry.Paused=true;if(entry.Cancel!=null&&entry.Loading)entry.Cancel.Cancel();return true;}
                if(action=="Resume"){entry.Paused=false;return true;}
                // Cursor's documented report is hourly; manual refresh never defeats its cooldown.
                if(action=="Refresh"){if(HubNames.Provider(id)!="Cursor"&&!entry.Loading&&entry.Next<=DateTime.UtcNow.AddMinutes(4))entry.Next=DateTime.MinValue;Ensure(id);return true;}
                return false;
            }
        }
        public void Stop(string id){lock(sync){Entry e;if(entries.TryGetValue(id,out e)){entries.Remove(id);if(e.Cancel!=null&&e.Loading)e.Cancel.Cancel();}}}
        public void Retire(string id){lock(sync){retired.Add(id);Stop(id);}}
        public void StopAll(){lock(sync){foreach(var id in entries.Keys.ToArray())Stop(id);}}
        public void Broadcast(string action){lock(sync){foreach(var id in entries.Keys.ToArray())Send(id,action);}}
        public void Dispose(){lock(sync){disposed=true;StopAll();client.Dispose();}}
    }
}
