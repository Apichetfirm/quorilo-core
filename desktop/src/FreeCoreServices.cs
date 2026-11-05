#if FREE_CORE
// Offline service boundary for the Free Core source build. These contracts never
// load an Account/Owner vault, create an identity, contact a server or grant rights.
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CreditMonitor
{
    public sealed class CommerceConfiguration
    {
        public string LicenseServer="",PublicKeyXml="",CheckoutUrl="",SupportUrl="",Transport="",FeedbackUrl="";
        public bool Beta;
        public bool Ready {get{return false;}}
        public static CommerceConfiguration Load(){return new CommerceConfiguration();}
    }
    public sealed class LicenseEnvelope {public string payload,signature;}
    public sealed class LicenseGrant
    {
        public int schema,devices,trialRevision,betaSlot;
        public string product,kind,device,instance,issuedAt,expiresAt,updatesUntil,startedAt,trialEndsAt,testerId,requestNonce,subjectHash,entitlementEndsAt,billingMode,originalTrialStartedAt,originalTrialEndsAt,monitorPlan;
        public int? accountsPerProvider;
        public bool perpetual;
    }
    public sealed class LicenseDevice {public string instance,name,activatedAt;public bool current;}
    public sealed class LicenseFailure:Exception
    {
        public readonly string Code;
        public LicenseFailure(string code):base(code){Code=code;}
    }
    public sealed class LicenseClient
    {
        static LicenseFailure Unavailable(){return new LicenseFailure("AccountServiceUnavailable");}
        public LicenseClient(CommerceConfiguration config,string vaultPath,string deviceId,Func<DateTime> clock=null,Func<string,object,Task<string>> send=null){throw Unavailable();}
        public bool Configured {get{return false;}}
        public bool Allowed {get{return false;}}
        public bool Full {get{return false;}}
        public bool MultipleAccounts {get{return false;}}
        public bool HasKey {get{return false;}}
        public string LastError {get{return "AccountServiceUnavailable";}}
        public string KeyHint {get{return "";}}
        public LicenseGrant Grant {get{return null;}}
        public DateTime? EntitlementEndsAt {get{return null;}}
        public DateTime? ValidUntil {get{return null;}}
        public DateTime? StartedAt {get{return null;}}
        public DateTime? TrialEndsAt {get{return null;}}
        public static string Storage {get{throw Unavailable();}}
        public static string DeviceIdentity(){throw Unavailable();}
        public Task SignInGoogleAsync(Action<string> status=null,Action<string> open=null){throw Unavailable();}
        public Task PrepareAsync(string key){throw Unavailable();}
        public Task ActivateAsync(string key,bool quotaReady=false){throw Unavailable();}
        public Task ReactivateAsync(bool quotaReady=false){throw Unavailable();}
        public Task RefreshAsync(bool connected,bool force=false){throw Unavailable();}
        public Task<LicenseDevice[]> DevicesAsync(){throw Unavailable();}
        public Task DeactivateAsync(string instance){throw Unavailable();}
        public Task<UpdateManifest> CheckUpdateAsync(){throw Unavailable();}
        public void SignOut(){throw Unavailable();}
        public void SuspendTrial(){throw Unavailable();}
        public static string Message(string code){return "บริการบัญชีแยกต่างหากยังไม่เปิดใน Free Core · ฟีเจอร์ในเครื่องใช้ได้โดยไม่ต้องมีบัญชี";}
    }
    public static class AccountTrialClient
    {
        public const string Origin="https://quorilo.app";
        public static DateTime Date(string value){return DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);}
    }
#if UNIFIED
    sealed class CompanionClient
    {
        internal const string Gateway="";
        public string Status {get{return "บริการออนไลน์เสริมยังไม่เปิดใน Free Core";}}
        public bool Paired {get{return false;}}
        static InvalidOperationException Unavailable(){return new InvalidOperationException("Free Core does not authorize online services");}
        public Task Pair(string endpoint,string code){throw Unavailable();}
        public Task Upload(object snapshot){throw Unavailable();}
        public Task UploadHistory(CreditMonitor.Insights.InsightsRuntime history){throw Unavailable();}
        public void RetryHistory(){throw Unavailable();}
        public void Disconnect(){throw Unavailable();}
    }
#endif
}
#endif
