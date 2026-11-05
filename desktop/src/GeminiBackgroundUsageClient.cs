#if GEMINI
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace CreditMonitor
{
    // A separate WebView2 session owned by this utility. Never imports a Chrome/Edge profile,
    // extracts cookies, changes the user agent, reads chats or invokes a model.
    public sealed class GeminiBackgroundUsageClient : IUsageClient
    {
        const string UsageUrl="https://gemini.google.com/usage?hl=th";
        string source;
        Dispatcher dispatcher; Action changed; GeminiConnectionWindow host;
        CoreWebView2Controller controller; CoreWebView2 web;
        DispatcherTimer timer; Task initialization; bool disposed, reading, navigating, hasQuota, documentReady, documentHasQuota, attemptFailed;
        int refreshSeconds, retrySeconds;
        DateTime captureAt, lastReloadAt;
        DateTime refreshAt=DateTime.MinValue, navigationAt, usageAt, projectionAt; string lastSnapshot, lastProjection, accountScope; int documentGeneration, landingRedirects; bool usageRedirect;
        bool observedCurrent, observedWeekly;
        public ConcurrentQueue<string> Updates {get;private set;}
        public bool IsRunning {get;private set;}
        public string LastError {get;private set;}
        public GeminiBackgroundUsageClient(int seconds=15){source=LoadSourceKey();refreshSeconds=ValidInterval(seconds);Updates=new ConcurrentQueue<string>();}
        static string SourceKeyPath{get{return Path.Combine(Product.Storage,"gemini-apps-source-id.txt");}}
        static string NewSourceKey(){return Guid.NewGuid().ToString("N").Substring(0,24);}
        static string LoadSourceKey()
        {
            try{string path=SourceKeyPath;if(File.Exists(path)){string saved=File.ReadAllText(path).Trim();if(System.Text.RegularExpressions.Regex.IsMatch(saved,"^[a-f0-9]{24}$"))return saved;}}
            catch{}
            return WriteNewSourceKey();
        }
        string RotateSourceKey(){return source=WriteNewSourceKey();}
        static string WriteNewSourceKey()
        {
            string key=NewSourceKey();string path=SourceKeyPath;string temporary=path+".tmp";
            try{Directory.CreateDirectory(Product.Storage);File.WriteAllText(temporary,key);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}catch{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
            return key;
        }
        static int ValidInterval(int seconds){return seconds==30 || seconds==60 ? seconds : 15;}
        public void SetRefreshInterval(int seconds)
        {
            refreshSeconds=ValidInterval(seconds);
            refreshAt=DateTime.UtcNow.AddSeconds(Math.Max(refreshSeconds,retrySeconds));
        }
        public static string ProfilePath {get{return Path.Combine(Product.Storage,"BackgroundWebProfile");}}
        public static string Inspect(){try{return "WebView2 "+CoreWebView2Environment.GetAvailableBrowserVersionString()+" · โปรไฟล์ของโปรแกรมแยกจาก Chrome/Edge";}catch{return "ยังไม่พบ Microsoft Edge WebView2 Runtime · ติดตั้งจาก Microsoft ก่อนเชื่อม";}}
        public void Start(string address,Dispatcher ui,Action callback)
        {
            dispatcher=ui;changed=callback;IsRunning=true;initialization=InitializeAsync();
        }
        static bool Allowed(string value)
        {
            Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" &&
                (uri.Host=="gemini.google.com" || uri.Host=="accounts.google.com");
        }
        static bool Usage(string value)
        {
            Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.Host=="gemini.google.com" &&
                System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,@"^/(?:u/\d+/)?usage/?$");
        }
        async Task InitializeAsync()
        {
            try {
                StartupProfile.Mark("gemini_webview2_init_start");
                // Start WebView2 environment creation asynchronously while constructing the host window and HWND.
                var environmentTask=CoreWebView2Environment.CreateAsync(null,ProfilePath);
                host=new GeminiConnectionWindow(OpenUsage,ManualReload,Conceal);
                host.Closing+=CloseToBackground;
                IntPtr handle=new WindowInteropHelper(host).EnsureHandle();
                var environment=await environmentTask;
                if(disposed)return;
                var created=await environment.CreateCoreWebView2ControllerAsync(handle);
                if(disposed){created.Close();return;}
                controller=created;controller.IsVisible=false;web=controller.CoreWebView2;host.Attach(controller);
                StartupProfile.Mark("gemini_webview2_controller_ready");
                
                web.Settings.IsPasswordAutosaveEnabled=false;web.Settings.IsGeneralAutofillEnabled=false;
                web.Settings.AreDevToolsEnabled=false;web.Settings.AreDefaultContextMenusEnabled=false;web.Settings.IsStatusBarEnabled=false;
                web.PermissionRequested+=(s,e)=>{e.State=CoreWebView2PermissionState.Deny;};
                web.DownloadStarting+=(s,e)=>{e.Cancel=true;};
                web.NewWindowRequested+=(s,e)=>{e.Handled=true;if(Allowed(e.Uri))web.Navigate(e.Uri);else SetError("WebNavigationBlocked");};
                web.NavigationStarting+=(s,e)=>{if(!Allowed(e.Uri)){e.Cancel=true;SetError("WebNavigationBlocked");return;}if(Usage(e.Uri)){var match=System.Text.RegularExpressions.Regex.Match(e.Uri,@"/u/(\d+)/");string scope=match.Success?match.Groups[1].Value:"0";if(accountScope!=null&&accountScope!=scope){RotateSourceKey();hasQuota=false;observedCurrent=observedWeekly=false;Updates.Enqueue("{\"monitorAccountChanged\":true}");Notify();}accountScope=scope;}usageRedirect=hasQuota && (Usage(e.Uri) || e.Uri.StartsWith("https://gemini.google.com/",StringComparison.Ordinal));documentGeneration++;lastSnapshot=null;lastProjection=null;documentReady=false;documentHasQuota=false;attemptFailed=false;navigating=true;navigationAt=lastReloadAt=DateTime.UtcNow;if(!Usage(e.Uri)&&!usageRedirect){RotateSourceKey();hasQuota=false;observedCurrent=observedWeekly=false;Updates.Enqueue("{\"monitorAccountChanged\":true}");Notify();}if((!Usage(e.Uri)&&!usageRedirect)||!hasQuota)SetError(Usage(e.Uri)?"WebUsageWaiting":LoginError(e.Uri));else host.ShowStatus("WebUsageWaiting");};
                web.NavigationCompleted+=(s,e)=>{
                    navigating=false;usageAt=DateTime.UtcNow;
                    if(!e.IsSuccess){documentReady=false;SetError("WebReadUnavailable");return;}
                    documentReady=true;captureAt=DateTime.MinValue;
                    refreshAt=usageAt.AddSeconds(Math.Max(refreshSeconds,retrySeconds));
                    if(Usage(web.Source)){CaptureAsync();return;}
                    Uri destination;
                    // Google can return to /app after a successful login. Resume our own Usage page
                    // once; never reload an account challenge or loop a missing/unsupported page.
                    if(Uri.TryCreate(web.Source,UriKind.Absolute,out destination) && destination.Host=="gemini.google.com"){
                        if(landingRedirects++<2)web.Navigate(UsageUrl);else SetError("WebUsageUnavailable");return;
                    }
                    SetError(LoginError(web.Source));
                };
                web.ProcessFailed+=(s,e)=>{IsRunning=false;if(timer!=null)timer.Stop();SetError("WebReadUnavailable");};
                timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(350)};
                timer.Tick+=(s,e)=>{
                    if(disposed || web==null)return;
                    if(navigating){if(DateTime.UtcNow-navigationAt>TimeSpan.FromSeconds(25)){SetError("WebNavigationSlow");web.Stop();navigating=false;documentReady=false;}return;}
                    // Wait for real quota (or an explicit failure) before reloading. A 15s
                    // schedule must not interrupt the initial Usage loading grace period.
                    if(Usage(web.Source) && DateTime.UtcNow>=refreshAt && (documentHasQuota || attemptFailed)){ReloadUsage();return;}
                    if(DateTime.UtcNow>=captureAt){
                        // Poll faster (350ms) during initial SPA hydration after NavigationCompleted; settle to 2s once quota is captured.
                        double pollStepSeconds = (!documentHasQuota && DateTime.UtcNow-usageAt<TimeSpan.FromSeconds(6)) ? 0.35 : 2.0;
                        captureAt=DateTime.UtcNow.AddSeconds(pollStepSeconds);
                        CaptureAsync();
                    }
                };
                web.Navigate(UsageUrl);timer.Start();
            } catch(WebView2RuntimeNotFoundException){IsRunning=false;SetError("WebRuntimeMissing");}
            catch {IsRunning=false;SetError("WebReadUnavailable");}
        }
        public async Task ShowConnectionAsync()
        {
            if(initialization!=null)await initialization;
            if(disposed || controller==null || host==null)return;
            // Only this explicit user action exposes the sign-in window. Google receives the login;
            // the utility does not intercept credentials or suppress Google's security errors.
            host.ShowInTaskbar=false;host.ShowActivated=true;if(host.WindowState==WindowState.Minimized)host.WindowState=WindowState.Normal;host.Show();host.UpdateLayout();host.UpdateBounds();controller.IsVisible=true;host.Activate();controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            if(!Usage(web.Source) && !Allowed(web.Source))web.Navigate(UsageUrl);
        }
        void OpenUsage(){if(web==null)return;landingRedirects=0;web.Navigate(UsageUrl);}
        void ReloadUsage(){landingRedirects=0;lastReloadAt=DateTime.UtcNow;refreshAt=lastReloadAt.AddSeconds(Math.Max(refreshSeconds,retrySeconds));web.Reload();}
        void ManualReload(){if(disposed || web==null || navigating || !Allowed(web.Source) || DateTime.UtcNow-lastReloadAt<TimeSpan.FromSeconds(10))return;lastReloadAt=DateTime.UtcNow;web.Reload();}
        void CloseToBackground(object sender,CancelEventArgs e)
        {
            if(disposed)return;e.Cancel=true;Conceal();
        }
        void Conceal(){if(controller!=null)controller.IsVisible=false;if(host!=null){host.Hide();host.ShowInTaskbar=false;}}
        async void CaptureAsync()
        {
            if(disposed || reading || navigating || web==null || !documentReady)return;
            if(!Usage(web.Source)){if(LastError!="WebNavigationBlocked" && LastError!="WebUsageUnavailable")SetError(LoginError(web.Source));return;}
            reading=true;int generation=documentGeneration;
            try {
                
                // Restrict inspection to the Usage component. Observation time only changes when the
                // quota DOM changes or a new document loads; rereading cannot disguise stale data.
                string result=await web.ExecuteScriptAsync(GeminiUsageText.DomProjection);
                if(disposed || navigating || generation!=documentGeneration)return;
                if(result=="null"){if(DateTime.UtcNow-usageAt>TimeSpan.FromSeconds(25))SetError("WebUsageUnavailable");else if(!hasQuota)SetError("WebUsageWaiting");return;}
                string projection=new JavaScriptSerializer().Deserialize<string>(result);
                if(projection!=lastProjection){lastProjection=projection;projectionAt=DateTime.UtcNow;}
                string snapshot=GeminiUsageText.Snapshot(projection,source,projectionAt,projectionAt.ToLocalTime());
                var data=GeminiQuota.Parse(snapshot);
                // A new Usage document briefly renders an empty/partial component. Wait
                // for it to settle before changing status or replacing a complete reading.
                // Existing observation/reset times stay untouched during this grace period.
                bool incomplete=!data.HasQuota || (observedCurrent&&data.Current==null&&!data.ResetPending) || (observedWeekly&&data.Weekly==null&&!data.ResetPending);
                if(incomplete && DateTime.UtcNow-usageAt<TimeSpan.FromSeconds(25))return;
                if(!data.HasQuota){SetError("WebUsageUnavailable");return;}
                hasQuota=true;documentHasQuota=true;
                observedCurrent=data.Current!=null;observedWeekly=data.Weekly!=null;
                if(retrySeconds!=0){retrySeconds=0;refreshAt=DateTime.UtcNow.AddSeconds(refreshSeconds);}
                attemptFailed=false;
                SetError(null);
                if(snapshot!=lastSnapshot){Updates.Enqueue(snapshot);lastSnapshot=snapshot;Notify();}
                if(data.HasQuota && host.IsVisible)Conceal();
            } catch {if(!disposed && generation==documentGeneration)SetError("WebReadUnavailable");}
            finally {reading=false;}
        }
        public void RequestSnapshot()
        {
            if(disposed || dispatcher==null)return;
            dispatcher.BeginInvoke(new Action(()=>{if(disposed || web==null || navigating)return;if(Usage(web.Source) && DateTime.UtcNow-lastReloadAt>=TimeSpan.FromSeconds(10))ReloadUsage();else CaptureAsync();}));
        }
        static string LoginError(string url){Uri uri;if(Uri.TryCreate(url,UriKind.Absolute,out uri) && uri.Host=="accounts.google.com"){if(uri.AbsolutePath.IndexOf("deniedsignin",StringComparison.OrdinalIgnoreCase)>=0 || uri.Query.IndexOf("disallowed_useragent",StringComparison.OrdinalIgnoreCase)>=0)return "WebSignInUnsupported";if(uri.AbsolutePath.IndexOf("passkey",StringComparison.OrdinalIgnoreCase)>=0)return "WebPasskeyStep";}return "WebLoginRequired";}
        void SetError(string value)
        {
            if(!attemptFailed && (value=="WebReadUnavailable" || value=="WebUsageUnavailable" || value=="WebNavigationSlow")){
                attemptFailed=true;
                retrySeconds=retrySeconds==0 ? Math.Max(30,refreshSeconds*2) : Math.Min(60,retrySeconds*2);
                refreshAt=DateTime.UtcNow.AddSeconds(retrySeconds);
            }
            if(host!=null)host.ShowStatus(value);if(LastError==value)return;LastError=value;Notify();
        }
        void Notify(){if(!disposed && dispatcher!=null && !dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(new Action(()=>{if(!disposed && changed!=null)changed();}));}
        public void Dispose()
        {
            if(disposed)return;disposed=true;IsRunning=false;changed=null;
            if(timer!=null)timer.Stop();if(host!=null)host.Detach();if(controller!=null){controller.Close();controller=null;}web=null;
            if(host!=null){host.Close();host=null;}
        }
    }
}
#endif
