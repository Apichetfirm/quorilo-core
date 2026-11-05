#if FREE_CORE
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CreditMonitor
{
    // Uses only the provider CLI's existing session. No client identity, OAuth
    // acquisition, refresh request, credential write or browser launch belongs here.
    internal static class GeminiExistingSession
    {
        static string Text(IDictionary<string,object> value,string key){object item;return value!=null&&value.TryGetValue(key,out item)?item as string:null;}
        internal static bool TryRead(IDictionary<string,object> credentials,DateTime nowUtc,out string access,out DateTime untilUtc,out string source)
        {
            access=null;untilUtc=DateTime.MinValue;source=null;
            if(credentials==null||nowUtc.Kind!=DateTimeKind.Utc||nowUtc==DateTime.MinValue||nowUtc> DateTime.MaxValue.AddDays(-2))return false;
            string candidate=Text(credentials,"access_token");
            if(String.IsNullOrWhiteSpace(candidate)||candidate.Length>8192)return false;
            foreach(char value in candidate)if(value<'!'||value>'~')return false;
            object raw;if(!credentials.TryGetValue("expiry_date",out raw)||!(raw is int||raw is long||raw is double||raw is decimal))return false;
            double milliseconds;try{milliseconds=Convert.ToDouble(raw);}catch{return false;}
            if(Double.IsNaN(milliseconds)||Double.IsInfinity(milliseconds)||milliseconds!=Math.Truncate(milliseconds)||milliseconds<0||milliseconds>253402214400000d)return false;
            DateTime expiry;try{expiry=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(milliseconds);}catch{return false;}
            if(expiry<=nowUtc.AddMinutes(2)||expiry>nowUtc.AddDays(1))return false;
            string marker=Text(credentials,"refresh_token");
            if(String.IsNullOrWhiteSpace(marker))marker=candidate;
            if(marker.Length>8192)return false;
            using(var hash=SHA256.Create())source=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(marker))).Replace("-","").ToLowerInvariant().Substring(0,24);
            access=candidate;untilUtc=expiry;return true;
        }
    }
}
#endif
