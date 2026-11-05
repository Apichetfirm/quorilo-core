using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;

namespace CreditMonitor
{
    internal sealed class MonitorWindowLifecycle
    {
        private int state; // 0 = running, 1 = closing, 2 = closed
        private int closingCallbacks;

        public bool IsRunning { get { return Volatile.Read(ref state) == 0; } }
        public bool IsExitingOrClosed { get { return !IsRunning; } }
        public bool CanShow { get { return IsRunning && Volatile.Read(ref closingCallbacks) == 0; } }
        public bool IsClosed { get { return Volatile.Read(ref state) == 2; } }

        public void EnterClosingCallback() { Interlocked.Increment(ref closingCallbacks); }
        public void ExitClosingCallback() { Interlocked.Decrement(ref closingCallbacks); }

        public bool BeginClose()
        {
            return Interlocked.CompareExchange(ref state, 1, 0) == 0;
        }

        public void MarkClosed()
        {
            Interlocked.Exchange(ref state, 2);
        }
    }

    // Guard the entire WPF close dispatch, including later subscribers and nested frames.
    internal sealed class MonitorWindow : Window
    {
        private readonly MonitorWindowLifecycle lifecycle;
        internal MonitorWindow(MonitorWindowLifecycle lifecycle) { this.lifecycle = lifecycle; }
        protected override void OnClosing(CancelEventArgs args)
        {
            lifecycle.EnterClosingCallback();
            try { base.OnClosing(args); }
            finally { lifecycle.ExitClosingCallback(); }
        }
        protected override void OnClosed(EventArgs args)
        {
            lifecycle.MarkClosed();
            base.OnClosed(args);
        }
    }

    internal static class MonitorExceptionLog
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 1024 * 1024;
        private const int MaxRecordChars = 32768;
        private static int installed;
        private static string buildIdentity;
        private static readonly Regex OwnedArchive = new Regex(@"^crash-\d{8}-\d{9}-[a-f0-9]{8}\.log$");
        public static void Install(Application application, Func<string> storageRoot)
        {
            if(application==null)throw new ArgumentNullException("application");
            if(storageRoot==null)throw new ArgumentNullException("storageRoot");
            if(Interlocked.Exchange(ref installed,1)!=0)return;
            application.DispatcherUnhandledException+=(s,e)=>TryWrite(e.Exception,"DispatcherUnhandledException",storageRoot);
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>TryWrite(e.ExceptionObject as Exception,"AppDomain.UnhandledException",storageRoot);
            TaskScheduler.UnobservedTaskException+=(s,e)=>TryWrite(e.Exception,"TaskScheduler.UnobservedTaskException",storageRoot);
        }
        private static void TryWrite(Exception exception,string source,Func<string> root){try{Write(exception,source,root());}catch{}}
        private static string Source(string value)
        {
            switch(value){case "DispatcherUnhandledException":case "AppDomain.UnhandledException":case "TaskScheduler.UnobservedTaskException":case "Program.Main":case "LifecycleRegression":case "RedactionRegression":case "TruncationRegression":case "RotationSeed":case "RotationCheck":return value;default:return "Other";}
        }
        private static string BuildIdentity()
        {
            if(buildIdentity!=null)return buildIdentity;
            var assembly=typeof(MonitorExceptionLog).Assembly;string digest;
            using(var stream=File.OpenRead(assembly.Location))using(var sha=System.Security.Cryptography.SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
            return buildIdentity="version="+assembly.GetName().Version+" module="+assembly.ManifestModule.ModuleVersionId+" sha256="+digest;
        }
        private static string Describe(Exception exception)
        {
            var result=new StringBuilder();int depth=0;
            for(;exception!=null&&depth<12;exception=exception.InnerException,depth++){
                result.AppendLine(exception.GetType().FullName+" HResult=0x"+exception.HResult.ToString("X8"));
                // Capture real CLR frames without file paths. Never persist Message,
                // Data, ToString(), or a virtual StackTrace supplied by an exception.
                string stack=new System.Diagnostics.StackTrace(exception,false).ToString();
                if(stack.Length>MaxRecordChars)stack=stack.Substring(0,MaxRecordChars);
                result.AppendLine(String.IsNullOrEmpty(stack)?"[no stack captured]":stack);
                if(result.Length>=MaxRecordChars){result.Length=MaxRecordChars;break;}
            }
            return result.ToString();
        }
        public static void Write(Exception exception,string source,string storageRoot)
        {
            try{lock(Gate){
                string folder=Path.Combine(storageRoot,"logs");Directory.CreateDirectory(folder);string path=Path.Combine(folder,"crash.log");
                string record=DateTime.UtcNow.ToString("o")+" "+Source(source)+" "+BuildIdentity()+"\r\n"+Describe(exception)+"\r\n";
                byte[] bytes=new UTF8Encoding(false).GetBytes(record);
                if(File.Exists(path)&&new FileInfo(path).Length+bytes.Length>MaxBytes)File.Move(path,Path.Combine(folder,"crash-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".log"));
                using(var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read))file.Write(bytes,0,bytes.Length);
                foreach(var archive in new DirectoryInfo(folder).GetFiles("crash-*.log").Where(f=>OwnedArchive.IsMatch(f.Name)).OrderByDescending(f=>f.LastWriteTimeUtc).ThenByDescending(f=>f.Name).Skip(3))archive.Delete();
            }}catch{/* Diagnostics must never replace or swallow the original failure. */}
        }
    }
}
