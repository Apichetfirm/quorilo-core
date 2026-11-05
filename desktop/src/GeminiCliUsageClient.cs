using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
namespace CreditMonitor
{
    // Read-only quota calls used by Gemini CLI. No model calls, login launch, onboarding or billing.
    public sealed class GeminiCliUsageClient : IUsageClient
    {
        public ConcurrentQueue<string> Updates {get;private set;}
        public bool IsRunning {get {return Volatile.Read(ref running)==1;}}
        public string LastError {get;private set;}
        int running,reading,notified;Timer timer;Dispatcher dispatcher;Action changed;
        HttpWebRequest active;readonly object gate=new object();
        string token,project,identity;DateTime tokenUntil,attemptAt;
        bool lastReadSucceeded;
        public static string CredentialsPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".gemini","oauth_creds.json");}}
        public GeminiCliUsageClient(){Updates=new ConcurrentQueue<string>();}
        public void Start(string address,Dispatcher ui,Action callback){dispatcher=ui;changed=callback;Interlocked.Exchange(ref running,1);timer=new Timer(s=>Read(),null,0,60000);}
        public void RequestSnapshot(){ThreadPool.QueueUserWorkItem(s=>Read());}
        public static string Inspect(){if(Product.FreeCore)return "Free Core อ่านได้เฉพาะ session ที่ Gemini CLI ลงชื่อเข้าใช้ไว้และยังไม่หมดอายุ · ไม่ refresh OAuth ให้ หากหมดอายุให้เชื่อมใหม่ผ่าน CLI ของผู้ให้บริการ";return File.Exists(CredentialsPath) ? "พบข้อมูลลงชื่อเข้าใช้ Gemini CLI · ตรวจบัญชีเมื่อรีเฟรช" : "ยังไม่เชื่อม · ติดตั้ง Gemini CLI และลงชื่อเข้าใช้ด้วย Google ก่อน";}
        static string Text(Dictionary<string,object> data,string key){object raw;return data!=null && data.TryGetValue(key,out raw) ? raw as string : null;}
        Dictionary<string,object> Post(string url,string payload,string bearer,bool form)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(url);request.Method="POST";request.AllowAutoRedirect=false;request.Timeout=6000;request.ReadWriteTimeout=6000;
            request.ContentType=form ? "application/x-www-form-urlencoded" : "application/json";request.UserAgent="QuotaMonitor-GeminiCLI/0.1.1";
            if(bearer!=null)request.Headers[HttpRequestHeader.Authorization]="Bearer "+bearer;
            byte[] bytes=Encoding.UTF8.GetBytes(payload);request.ContentLength=bytes.Length;
            lock(gate){if(!IsRunning)throw new OperationCanceledException();active=request;}
            try {
                using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
                using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    var text=new StringBuilder();var buffer=new char[2048];int n;
                    while((n=reader.Read(buffer,0,buffer.Length))>0){if(text.Length+n>131072)throw new FormatException();text.Append(buffer,0,n);}
                    var data=new JavaScriptSerializer().DeserializeObject(text.ToString()) as Dictionary<string,object>;if(data==null)throw new FormatException();return data;
                }
            } finally {lock(gate){if(active==request)active=null;}}
        }
        void Read()
        {
            if(!IsRunning || Interlocked.Exchange(ref reading,1)!=0)return;
            try {
                // Prevent repeated clicks from hammering Google's quota endpoint, while allowing fast retry after failure.
                double minSeconds = lastReadSucceeded ? 10.0 : 2.0;
                if(DateTime.UtcNow-attemptAt < TimeSpan.FromSeconds(minSeconds))return;attemptAt=DateTime.UtcNow;
                if(!File.Exists(CredentialsPath)){lastReadSucceeded=false;SetError("LoginRequired");return;}
                string settings=Path.Combine(Path.GetDirectoryName(CredentialsPath),"settings.json");
                if(File.Exists(settings)) {
                    if(new FileInfo(settings).Length>1048576)throw new FormatException();
                    var config=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(settings,Encoding.UTF8)) as Dictionary<string,object>;
                    object security,auth;var securityObject=config!=null && config.TryGetValue("security",out security) ? security as Dictionary<string,object> : null;
                    var authObject=securityObject!=null && securityObject.TryGetValue("auth",out auth) ? auth as Dictionary<string,object> : null;
                    string selected=Text(authObject,"selectedType");if(selected!=null && selected!="oauth-personal"){lastReadSucceeded=false;SetError("UnsupportedAccount");return;}
                }
                if(new FileInfo(CredentialsPath).Length>65536)throw new FormatException();
                var json=new JavaScriptSerializer();var credentials=json.DeserializeObject(File.ReadAllText(CredentialsPath,Encoding.UTF8)) as Dictionary<string,object>;
#if FREE_CORE
                string existing,source;DateTime expiry;
                if(!GeminiExistingSession.TryRead(credentials,DateTime.UtcNow,out existing,out expiry,out source)){
                    token=null;project=null;tokenUntil=DateTime.MinValue;lastReadSucceeded=false;SetError("LoginRequired");return;
                }
                if(identity!=source||token!=existing)project=null;
                identity=source;token=existing;tokenUntil=expiry;
#else
                string refresh=Text(credentials,"refresh_token");if(String.IsNullOrWhiteSpace(refresh) || refresh.Length>8192){lastReadSucceeded=false;SetError("UnsupportedAccount");return;}
                string nextIdentity;using(var sha=SHA256.Create())nextIdentity=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(refresh))).Replace("-","").ToLowerInvariant().Substring(0,24);
                if(identity!=nextIdentity){identity=nextIdentity;token=null;project=null;tokenUntil=DateTime.MinValue;}
                if(token==null || tokenUntil<=DateTime.UtcNow.AddMinutes(2)) {
                    // Public installed-app client identity from Google's Apache-2.0 Gemini CLI source.
                    var auth=Post("https://oauth2.googleapis.com/token","client_id="+Uri.EscapeDataString(GeminiOAuthIdentity.ClientId)+"&client_secret="+Uri.EscapeDataString(GeminiOAuthIdentity.ClientSecret)+"&refresh_token="+Uri.EscapeDataString(refresh)+"&grant_type=refresh_token",null,true);
                    token=Text(auth,"access_token");object seconds;if(String.IsNullOrWhiteSpace(token) || !auth.TryGetValue("expires_in",out seconds))throw new FormatException();
                    tokenUntil=DateTime.UtcNow.AddSeconds(Math.Min(3600,Math.Max(0,Convert.ToDouble(seconds))));
                }
#endif
                if(project==null) {
                    var assist=Post("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist","{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}",token,false);
                    project=Text(assist,"cloudaicompanionProject");
                    if(String.IsNullOrWhiteSpace(project)){lastReadSucceeded=false;SetError("UnsupportedAccount");return;}
                }
                var quota=Post("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota",json.Serialize(new {project=project}),token,false);
                object buckets;quota.TryGetValue("buckets",out buckets);
                // Never serialize raw server responses, credentials, project IDs or account details.
                var safe=new System.Collections.Generic.List<object>();var values=buckets as object[];
                if(values!=null && values.Length<=128)foreach(var value in values){var b=value as Dictionary<string,object>;object fraction;if(b!=null && b.TryGetValue("remainingFraction",out fraction) && (fraction is int || fraction is long || fraction is double || fraction is decimal))safe.Add(new {modelId=Text(b,"modelId"),remainingFraction=fraction,resetTime=Text(b,"resetTime")});}
                string snapshot=json.Serialize(new {schema=1,provider="gemini-cli",source_key=identity,captured_at=DateTime.UtcNow.ToString("o"),buckets=safe});GeminiCliQuota.Parse(snapshot);
                if(!IsRunning)return;string ignored;while(Updates.TryDequeue(out ignored)){}Updates.Enqueue(snapshot);lastReadSucceeded=true;SetError(null);Notify();
                StartupProfile.Mark("gemini_cli_first_snapshot_published");
            } catch(WebException e) {
                lastReadSucceeded=false;
                var response=e.Response as HttpWebResponse;int status=response==null ? 0 : (int)response.StatusCode;if(response!=null)response.Dispose();
                if(status==401 || status==400){token=null;project=null;}SetError(status==401 || status==400 ? "LoginRequired" : status==403 ? "UnsupportedAccount" : status==429 ? "RateLimited" : "QuotaUnavailable");
            } catch {lastReadSucceeded=false;SetError("QuotaUnavailable");}
            finally {Interlocked.Exchange(ref reading,0);}
        }
        void SetError(string error){if(LastError==error)return;LastError=error;Notify();}
        void Notify(){if(!IsRunning || dispatcher==null || dispatcher.HasShutdownStarted || Interlocked.Exchange(ref notified,1)!=0)return;try{dispatcher.BeginInvoke(new Action(()=>{Interlocked.Exchange(ref notified,0);if(IsRunning && changed!=null)changed();}));}catch{Interlocked.Exchange(ref notified,0);}}
        public void Dispose(){Interlocked.Exchange(ref running,0);if(timer!=null)timer.Dispose();lock(gate){if(active!=null)active.Abort();}token=null;project=null;changed=null;}
    }
}
