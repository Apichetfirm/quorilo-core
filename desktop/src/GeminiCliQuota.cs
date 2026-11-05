using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    public static class GeminiCliQuota
    {
        static object Field(Dictionary<string,object> value,string key) { object raw;return value != null && value.TryGetValue(key,out raw) ? raw : null; }
        public static UsageData Parse(string text) { return Parse(text,DateTime.UtcNow); }
        public static UsageData Parse(string text,DateTime now)
        {
            if(text == null || text.Length > 32768)throw new FormatException();
            var root=new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;
            DateTimeOffset observed;
            string at=Field(root,"captured_at") as string,source=Field(root,"source_key") as string;
            if(Convert.ToString(Field(root,"provider")) != "gemini-cli" || Convert.ToString(Field(root,"schema")) != "1" || !Regex.IsMatch(source ?? "","^[a-f0-9]{24}$")
                || at == null || at.Length > 40 || !Regex.IsMatch(at,@"T.*(?:Z|[+-]\d\d:\d\d)$") || !DateTimeOffset.TryParse(at,CultureInfo.InvariantCulture,DateTimeStyles.None,out observed) || observed.UtcDateTime > now.AddMinutes(2))throw new FormatException();
            var data=new UsageData { Source="Gemini CLI · Code Assist",SourceKey=source,ReadAt=observed.LocalDateTime,Capability="NoQuota",CurrentLabel="Pro",WeeklyLabel="Flash" };
            var buckets=Field(root,"buckets") as object[];
            if(buckets == null)return data;if(buckets.Length > 128)throw new FormatException();
            foreach(var item in buckets) {
                var bucket=item as Dictionary<string,object>;string model=Field(bucket,"modelId") as string;
                object raw=Field(bucket,"remainingFraction");
                if(!Regex.IsMatch(model ?? "","^gemini-[a-z0-9.-]{1,64}$") || !(raw is int || raw is long || raw is double || raw is decimal))continue;
                double fraction=Convert.ToDouble(raw,CultureInfo.InvariantCulture);if(Double.IsNaN(fraction) || Double.IsInfinity(fraction) || fraction < 0 || fraction > 1)continue;
                bool pro=model.Contains("-pro"),flash=model.Contains("-flash");if(!pro && !flash)continue;
                DateTime? reset=null;object resetRaw=Field(bucket,"resetTime");
                if(resetRaw != null) {
                    string stamp=resetRaw as string;DateTimeOffset parsed;
                    if(stamp == null || stamp.Length > 40 || !Regex.IsMatch(stamp,@"T.*(?:Z|[+-]\d\d:\d\d)$") || !DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))continue;
                    reset=parsed.UtcDateTime;if(reset > now.AddDays(8))continue;if(reset <= now){data.ResetPending=true;continue;}
                }
                var metric=new Metric { Remaining=fraction*100,ResetAt=reset,Model=model };
                // The tightest bucket in each family determines the displayed limit.
                Metric previous=pro ? data.Current : data.Weekly;
                if(previous == null || metric.Remaining < previous.Remaining) {if(pro)data.Current=metric;else data.Weekly=metric;}
            }
            if(data.HasQuota)data.Capability="Quota";return data;
        }
    }
}
