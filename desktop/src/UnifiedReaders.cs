using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    internal sealed class UnifiedReaders:IDisposable
    {
        static readonly string UserSid=WindowsIdentity.GetCurrent().User.Value;
        readonly string root;
        readonly string dataRoot;
        readonly AdditionalProviderRuntime additional;
        readonly object sync=new object();
        readonly Dictionary<string,Process> owned=new Dictionary<string,Process>();
        readonly HashSet<string> starting=new HashSet<string>();
        readonly HashSet<string> retired=new HashSet<string>();
        readonly HashSet<string> stopping=new HashSet<string>();
        readonly Dictionary<string,string> errors=new Dictionary<string,string>();
        readonly Dictionary<string,DateTime> retryAt=new Dictionary<string,DateTime>();
        readonly Dictionary<string,HubSnapshot> lastStatus=new Dictionary<string,HubSnapshot>();
        readonly Dictionary<string,DateTime> lastWriteUtc=new Dictionary<string,DateTime>();
        bool disposed;
        readonly Dictionary<string,DateTime> refreshAt=new Dictionary<string,DateTime>();
        public bool Refreshing(string id){if(HubNames.External(id))return additional.Refreshing(id);lock(sync){DateTime at;return refreshAt.TryGetValue(id,out at)&&DateTime.UtcNow-at<TimeSpan.FromSeconds(10);}}
        public UnifiedReaders(string directory,string profileRoot=null){root=directory;dataRoot=profileRoot==null?Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData):Path.GetFullPath(profileRoot);additional=new AdditionalProviderRuntime(Path.Combine(dataRoot,Product.StorageName));}
        public void SetAccountScope(string value){additional.SetScope(value);}
        public AdditionalProviderCredential ExternalCredential(string id){return additional.Credential(id);}
        public void ConfigureExternal(string id,AdditionalProviderCredential credential){additional.Configure(id,credential);}
        public void ForgetExternal(string id){additional.Forget(id);}
        string SignalName(string id,string action){return "Local\\"+HubNames.Storage(id)+"-"+UserSid+"-"+action;}
        string MutexName(string id){return "Local\\"+HubNames.Storage(id)+"-"+UserSid;}
        static bool IsMutexFree(string mutexName)
        {
            try{
                using(var m=new Mutex(false,mutexName)){
                    bool acquired;
                    try{acquired=m.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(acquired){m.ReleaseMutex();return true;}
                    return false;
                }
            }catch{return false;}
        }
        public bool Send(string id,string action){if(HubNames.External(id))return additional.Send(id,action);lock(sync){if(action!="Stop"&&(disposed||retired.Contains(id)||stopping.Contains(id)))return false;}if(action=="Refresh"&&Refreshing(id))return false;try{using(var e=EventWaitHandle.OpenExisting(SignalName(id,action)))e.Set();if(action=="Refresh")lock(sync)refreshAt[id]=DateTime.UtcNow;return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
        public string Error(string id){lock(sync){string value;return errors.TryGetValue(id,out value)?value:null;}}
        public bool Running(string id){if(HubNames.External(id))return additional.Read(id)!=null;lock(sync){Process p;try{return owned.TryGetValue(id,out p)&&!p.HasExited;}catch{return false;}}}
        public async Task EnsureAsync(string id)
        {
            if(HubNames.External(id)){additional.Ensure(id);return;}
            lock(sync){
                if(disposed||retired.Contains(id)||stopping.Contains(id)||starting.Contains(id))return;
                Process existing;
                try{if(owned.TryGetValue(id,out existing)&&!existing.HasExited)return;}catch{}
                DateTime next;if(retryAt.TryGetValue(id,out next)&&DateTime.UtcNow<next)return;
                starting.Add(id);errors.Remove(id);
            }
            StartupProfile.Mark("reader_ensure_start",id);
            int parentId=Process.GetCurrentProcess().Id;
            try{
                Process launched=await Task.Run(async ()=>{
                    string executable=Path.Combine(root,"Readers",HubNames.Provider(id),HubNames.Executable(id));
                    if(!File.Exists(executable))throw new IOException("ไฟล์ตัวอ่านไม่ครบ · ติดตั้งโปรแกรมใหม่");
                    string mutexName=MutexName(id);
                    bool ready=IsMutexFree(mutexName);
                    if(!ready){
                        // Gracefully replace the previous monitor owned by this same Windows account.
                        Send(id,"Stop");
                        for(int n=0;n<30&&!disposed;n++){
                            await Task.Delay(80).ConfigureAwait(false);
                            if(IsMutexFree(mutexName)){ready=true;break;}
                        }
                    }
                    if(disposed)return null;
                    if(!ready)throw new IOException("ตัวอ่านรุ่นเดิมยังเปิดอยู่ · ปิดวิดเจ็ตเดิมแล้วลองใหม่");
                    var start=new ProcessStartInfo(executable,"--hub-worker --hub-parent "+parentId){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(executable)};
                    foreach(string key in new[]{"CREDIT_MONITOR_FEED","CREDIT_MONITOR_DATA_DIR","CLAUDE_MONITOR_DATA_DIR"})start.EnvironmentVariables.Remove(key);
                    start.EnvironmentVariables.Remove("QUORILO_ACCOUNT_SLOT");
                    if(HubNames.Additional(id)){
                        start.EnvironmentVariables["QUORILO_ACCOUNT_SLOT"]=HubNames.Slot(id);
                        foreach(string key in new[]{"OPENAI_API_KEY","CODEX_API_KEY","CODEX_ACCESS_TOKEN","CLAUDE_CODE_OAUTH_TOKEN","ANTHROPIC_API_KEY","ANTHROPIC_AUTH_TOKEN"})start.EnvironmentVariables.Remove(key);
                        string home=Path.Combine(dataRoot,HubNames.Storage(id),"provider-home");
                        Directory.CreateDirectory(home);
                        if(HubNames.Provider(id)=="ChatGPT")start.EnvironmentVariables["CODEX_HOME"]=home;
                        if(HubNames.Provider(id)=="Claude")start.EnvironmentVariables["CLAUDE_CONFIG_DIR"]=home;
                    }
                    return Process.Start(start);
                });
                bool stopLaunched=false;
                lock(sync){
                    if(disposed||retired.Contains(id)){stopLaunched=true;if(launched!=null)owned[id]=launched;}
                    else{
                        lastStatus.Remove(id);lastWriteUtc.Remove(id);
                        Process old;if(owned.TryGetValue(id,out old)){old.Dispose();owned.Remove(id);}
                        if(launched!=null){owned[id]=launched;retryAt[id]=DateTime.UtcNow.AddSeconds(10);}
                    }
                }
                if(stopLaunched){
                    if(launched!=null)Stop(id);
                    return;
                }
                StartupProfile.Mark("reader_spawned",id);
            }catch(Exception e){
                lock(sync){errors[id]=e is IOException?e.Message:"เริ่มตัวอ่านไม่ได้ · ลองใหม่อีกครั้ง";retryAt[id]=DateTime.UtcNow.AddSeconds(15);}
                StartupProfile.Mark("reader_spawn_error",id+":"+e.GetType().Name);
            }
            finally{lock(sync)starting.Remove(id);}
        }
        public HubSnapshot Read(string id)
        {
            if(HubNames.External(id))return additional.Read(id);
            Process p;
            lock(sync){
                if(!owned.TryGetValue(id,out p))return null;
                try{if(p.HasExited){lastStatus.Remove(id);lastWriteUtc.Remove(id);return null;}}catch{lastStatus.Remove(id);lastWriteUtc.Remove(id);return null;}
            }
            try{
                string path=Path.Combine(dataRoot,HubNames.Storage(id),"status.json");
                var info=new FileInfo(path);
                if(!info.Exists)return RecentStatus(id);
                if(info.Length>32768){lock(sync){lastStatus.Remove(id);lastWriteUtc.Remove(id);}return null;}
                DateTime writeUtc=info.LastWriteTimeUtc;
                lock(sync){
                    DateTime previousWrite;HubSnapshot cached;
                    if(lastWriteUtc.TryGetValue(id,out previousWrite)&&previousWrite==writeUtc&&lastStatus.TryGetValue(id,out cached)){
                        return cached.HeartbeatCurrent&&cached.processId==p.Id?cached:null;
                    }
                }
                string text;
                // Atomic replacement can overlap a poll. Allow deletion while reading the
                // opened file; a brief read failure must not blank a live worker's last status.
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var reader=new StreamReader(stream))text=reader.ReadToEnd();
                var data=new JavaScriptSerializer().Deserialize<HubSnapshot>(text);
                if(data==null||data.schema!=2||data.flavor!=HubNames.Provider(id)||data.processId!=p.Id||data.commercial!=Product.Commercial||!data.hubWorker||!data.HeartbeatCurrent){
                    lock(sync){lastStatus.Remove(id);lastWriteUtc.Remove(id);}
                    return null;
                }
                bool firstRead;
                lock(sync){
                    firstRead=!lastStatus.ContainsKey(id);
                    lastStatus[id]=data;
                    lastWriteUtc[id]=writeUtc;
                }
                if(firstRead)StartupProfile.Mark("reader_first_status",id+":"+data.state);
                return data;
            }catch(IOException){return RecentStatus(id);}catch{lock(sync){lastStatus.Remove(id);lastWriteUtc.Remove(id);}return null;}
        }
        HubSnapshot RecentStatus(string id)
        {
            lock(sync){
                HubSnapshot data;Process process;
                return lastStatus.TryGetValue(id,out data)&&data.HeartbeatCurrent&&owned.TryGetValue(id,out process)&&data.processId==process.Id ? data : null;
            }
        }
        public void Stop(string id){if(HubNames.External(id)){additional.Stop(id);return;}lock(sync){lastStatus.Remove(id);lastWriteUtc.Remove(id);if(!stopping.Add(id))return;}var unused=StopUntilExitedAsync(id);}
        async Task StopUntilExitedAsync(string id)
        {
            try{int attempts=0;while(true){
                Process process;bool pending;lock(sync){owned.TryGetValue(id,out process);pending=starting.Contains(id);}
                if(process==null){if(!pending)return;}
                else{
                    bool exited;try{exited=process.HasExited;}catch{exited=true;}
                    // An exited/disposed process may be the previous instance while a
                    // replacement is starting. Keep the stop request until startup settles.
                    if(exited){if(!pending)return;}
                    else{
                        // Workers register signals after Process.Start returns. Keep retrying
                        // rather than losing the stop when a card is removed during startup.
                        Send(id,"Stop");
                    }
                }
                await Task.Delay(++attempts<50?100:1000).ConfigureAwait(false);
            }}finally{lock(sync)stopping.Remove(id);}
        }
        public void Retire(string id){if(HubNames.External(id)){additional.Retire(id);return;}lock(sync)retired.Add(id);Stop(id);}
        public async Task ForgetProfileAsync(string id)
        {
            if(!HubNames.Additional(id)||HubNames.External(id))throw new InvalidOperationException("Only isolated reader profiles can be removed");
            Retire(id);bool stopped=false;
            for(int attempt=0;attempt<150;attempt++){lock(sync){stopped=!starting.Contains(id)&&!stopping.Contains(id)&&!Running(id);}if(stopped)break;await Task.Delay(100);}
            if(!stopped)throw new IOException("Reader is still stopping");
            string target=Path.GetFullPath(Path.Combine(dataRoot,HubNames.Storage(id))),prefix=Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||target==Path.GetPathRoot(target))throw new IOException("Invalid isolated profile path");
            if(Directory.Exists(target)){Lifecycle.ValidateTree(target,target);foreach(var file in Directory.GetFiles(target,"*",SearchOption.AllDirectories))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked profile file refused");Lifecycle.DeleteOwnedDirectory(target,target);}
        }
        public void StopTrialReaders(){additional.SetScope(null);string[] ids;lock(sync)ids=owned.Keys.Concat(starting).Distinct().ToArray();foreach(var id in ids)Stop(id);}
        public void Broadcast(string action){additional.Broadcast(action);string[] ids;lock(sync)ids=owned.Keys.ToArray();foreach(var id in ids)Send(id,action);}
        public void Dispose(){disposed=true;additional.Dispose();List<KeyValuePair<string,Process>> list;lock(sync){list=owned.ToList();owned.Clear();lastStatus.Clear();lastWriteUtc.Clear();}foreach(var pair in list){try{if(!pair.Value.HasExited)Send(pair.Key,"Stop");}catch{}pair.Value.Dispose();}}
    }
}
