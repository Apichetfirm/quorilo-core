#if GEMINI
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    internal static class GeminiUsageText
    {
        // The browser only projects text from the Usage component. Interpretation,
        // percentage validation and reset calendar logic are compiled application code.
        internal const string DomProjection="(()=>{if(location.hostname!=='gemini.google.com'||!/^\\/(?:u\\/\\d+\\/)?usage\\/?$/.test(location.pathname))return null;const legacy=document.querySelector('usage-metrics-window');if(legacy){const read=id=>{const e=legacy.querySelector('[data-test-id=\"'+id+'\"]');if(!e)return null;return {paragraphs:Array.from(e.querySelectorAll('p'),p=>p.textContent),width:e.querySelector('.progress-indicator')?.style.width||''};};const current=read('gxu-currently'),weekly=read('gxu-weekly');if(current||weekly)return JSON.stringify({current,weekly});}const root=document.querySelector('declared-route-island-page');if(!root)return null;const read=id=>{const e=root.querySelector('[data-testid=\"'+id+'\"]');if(!e)return null;const text=e.innerText||'';if(!text||text.length>4096)return null;return {paragraphs:[text],width:''};};const current=read('current-usage'),weekly=read('weekly-usage');return current||weekly?JSON.stringify({current,weekly}):null;})()";
        static readonly string[] Months={@"ม\.ค\.|มกราคม|jan(?:uary)?",@"ก\.พ\.|กุมภาพันธ์|feb(?:ruary)?",@"มี\.ค\.|มีนาคม|mar(?:ch)?",@"เม\.ย\.|เมษายน|apr(?:il)?",@"พ\.ค\.|พฤษภาคม|may",@"มิ\.ย\.|มิถุนายน|jun(?:e)?",@"ก\.ค\.|กรกฎาคม|jul(?:y)?",@"ส\.ค\.|สิงหาคม|aug(?:ust)?",@"ก\.ย\.|กันยายน|sep(?:t(?:ember)?)?",@"ต\.ค\.|ตุลาคม|oct(?:ober)?",@"พ\.ย\.|พฤศจิกายน|nov(?:ember)?",@"ธ\.ค\.|ธันวาคม|dec(?:ember)?"};
        static object Field(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)?result:null;}
        static string Normalize(string value){return Regex.Replace((value??"").Replace('\u00a0',' '),"[๐-๙]",m=>((int)m.Value[0]-0x0E50).ToString(CultureInfo.InvariantCulture)).Trim();}
        static double? Used(string text)
        {
            text=Normalize(text);var matches=Regex.Matches(text,@"(?:ใช้ไป(?:แล้ว)?|used)\s*(?<value>\d+(?:[.,]\d+)?)\s*%|(?<value>\d+(?:[.,]\d+)?)\s*%\s*(?:used|ใช้ไป(?:แล้ว)?)",RegexOptions.IgnoreCase);
            if(matches.Count!=1)return null;double value;return Double.TryParse(matches[0].Groups["value"].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)&&value>=0&&value<=100?(double?)value:null;
        }

        static string Reset(string text,bool weekly,DateTime now)
        {
            text=Normalize(text);if(!Regex.IsMatch(text,"รีเซ็ต|reset",RegexOptions.IgnoreCase))return null;
            var clock=Regex.Match(text,@"(?:เวลา|at)\s*(\d{1,2}):(\d{2})\s*([ap]\.?m\.?)?",RegexOptions.IgnoreCase);
            if(!clock.Success)clock=Regex.Match(text,@"(\d{1,2}):(\d{2})\s*([ap]\.?m\.?)?",RegexOptions.IgnoreCase);
            if(!clock.Success)return null;int hour=Int32.Parse(clock.Groups[1].Value),minute=Int32.Parse(clock.Groups[2].Value);
            if(minute>59||hour>23)return null;if(clock.Groups[3].Success){if(hour<1||hour>12)return null;hour=hour%12+(clock.Groups[3].Value.StartsWith("p",StringComparison.OrdinalIgnoreCase)?12:0);}
            DateTime candidate=new DateTime(now.Year,now.Month,now.Day,hour,minute,0,DateTimeKind.Local);bool dated=false;
            for(int month=1;month<=12;month++){
                var match=Regex.Match(text,@"(\d{1,2})\s+(?:"+Months[month-1]+@")(?:\s+(\d{4}))?",RegexOptions.IgnoreCase);
                if(!match.Success)match=Regex.Match(text,@"(?:"+Months[month-1]+@")\s+(\d{1,2})(?:,?\s+(\d{4}))?",RegexOptions.IgnoreCase);
                if(!match.Success)continue;int day=Int32.Parse(match.Groups[1].Value),year=match.Groups[2].Success?Int32.Parse(match.Groups[2].Value):now.Year;if(year>2400)year-=543;
                try{candidate=new DateTime(year,month,day,hour,minute,0,DateTimeKind.Local);}catch(ArgumentOutOfRangeException){return null;}
                if(!match.Groups[2].Success&&candidate<=now&&month==1&&now.Month==12)candidate=candidate.AddYears(1);dated=true;break;
            }
            if(!dated&&Regex.IsMatch(text,"พรุ่งนี้|tomorrow",RegexOptions.IgnoreCase)){candidate=candidate.AddDays(1);dated=true;}
            if(weekly&&!dated&&!Regex.IsMatch(text,"วันนี้|today",RegexOptions.IgnoreCase))return null;
            if(!weekly&&!dated&&candidate<=now)candidate=candidate.AddDays(1);
            double seconds=(candidate.ToUniversalTime()-now.ToUniversalTime()).TotalSeconds;
            return seconds>0&&seconds<=(weekly?8*86400:86400)?candidate.ToUniversalTime().ToString("o"):null;
        }
        static object ReadWindow(object value,bool weekly,DateTime now)
        {
            var window=value as Dictionary<string,object>;if(window==null)return null;var paragraphs=Field(window,"paragraphs") as object[];if(paragraphs==null)return null;
            var texts=paragraphs.Select(x=>Normalize(x as string)).ToArray();var percents=texts.Select(Used).Where(x=>x.HasValue).ToArray();if(percents.Length!=1)return null;
            string width=Convert.ToString(Field(window,"width"));double indicator;
            if(!weekly&&Regex.IsMatch(width,@"^\d+(?:\.\d+)?%$")&&Double.TryParse(width.TrimEnd('%'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out indicator)&&Math.Abs(indicator-percents[0].Value)>1)return null;
            return new{used_percentage=percents[0].Value,resets_at=Reset(texts.FirstOrDefault(x=>Regex.IsMatch(x,"รีเซ็ต|reset",RegexOptions.IgnoreCase)),weekly,now)};
        }
        internal static string Snapshot(string json,string source,DateTime captured,DateTime localNow)
        {
            if(json==null||json.Length>32768)throw new FormatException("Usage text too large");
            var root=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;if(root==null)throw new FormatException("Invalid usage text");
            return new JavaScriptSerializer().Serialize(new{schema=1,provider="gemini-apps",source_key=source,captured_at=captured.ToUniversalTime().ToString("o"),current=ReadWindow(Field(root,"current"),false,localNow),weekly=ReadWindow(Field(root,"weekly"),true,localNow)});
        }
    }
}
#endif
