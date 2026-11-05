using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CreditMonitor
{
    internal static class PublisherTrust
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        sealed class FileInfo : IDisposable
        {
            public uint size=(uint)Marshal.SizeOf(typeof(FileInfo));
            public IntPtr path,handle=IntPtr.Zero,subject=IntPtr.Zero;
            public FileInfo(string file){path=Marshal.StringToCoTaskMemUni(Path.GetFullPath(file));}
            public void Dispose(){Marshal.FreeCoTaskMem(path);}
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        sealed class TrustData
        {
            public uint size=(uint)Marshal.SizeOf(typeof(TrustData));
            public IntPtr callback=IntPtr.Zero,client=IntPtr.Zero;
            public uint ui=2,revocation=1,choice=1;
            public IntPtr file;
            public uint action=1;
            public IntPtr state=IntPtr.Zero,url=IntPtr.Zero;
            public uint flags=0,context=0;
        }
        [DllImport("wintrust.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr window,[In] ref Guid action,[In,Out] TrustData data);
        public static bool IsTrusted(string path)
        {
            if(!File.Exists(path))return false;
            var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            using(var info=new FileInfo(path)){
                var ptr=Marshal.AllocHGlobal(Marshal.SizeOf(info));
                var data=new TrustData{file=ptr};
                try{Marshal.StructureToPtr(info,ptr,false);return WinVerifyTrust(new IntPtr(-1),ref action,data)==0;}
                catch{return false;}
                finally{data.action=2;try{WinVerifyTrust(new IntPtr(-1),ref action,data);}finally{Marshal.FreeHGlobal(ptr);}}
            }
        }
        static string Key(string file)
        {
            using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(file)))
            using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(cert.GetPublicKey()));
        }
        public static void RequireSamePublisher(string candidate,string installed)
        {
            if(!IsTrusted(installed))throw new LicenseFailure("UpdatePublisherUnavailable");
            if(!IsTrusted(candidate))throw new LicenseFailure("InvalidUpdatePublisher");
            try{if(Key(candidate)!=Key(installed))throw new LicenseFailure("InvalidUpdatePublisher");}
            catch(LicenseFailure){throw;}catch{throw new LicenseFailure("InvalidUpdatePublisher");}
        }
        public static string CurrentDescription()
        {
            try{using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(Assembly.GetExecutingAssembly().Location)))return "มีลายเซ็นผู้เผยแพร่ · ระบบจะตรวจความเชื่อถือและผู้ลงนามอีกครั้งก่อนอัปเดต";}
            catch{return "Unsigned Preview · ยังไม่มีลายเซ็นผู้เผยแพร่ที่ตรวจสอบได้ การอัปเดตในแอปจะรอรุ่นที่มีลายเซ็นเชื่อถือได้";}
        }
    }
}
