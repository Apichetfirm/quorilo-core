#if !TRIAL
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace CreditMonitor
{
    // Local, recent observations only. No quota prediction or telemetry.
    public sealed class BurnRate
    {
        public sealed class Sample { public DateTime At; public double Remaining; }
        public sealed class SavedSamples { public string Identity,ResetAt; public List<Sample> Samples; }
        readonly List<Sample> samples=new List<Sample>();
        readonly string path;
        string scope; DateTime? reset; double? rate; bool restored;
        public BurnRate(string cachePath=null){path=cachePath;}
        public void Clear(){samples.Clear();scope=null;reset=null;rate=null;restored=false;}
        public double? Observe(double remaining,DateTime observed,DateTime now,DateTime? resetAt,string identity,bool fresh)
        {
            if(!fresh)return null;
            observed=observed.ToUniversalTime();now=now.ToUniversalTime();
            if(String.IsNullOrEmpty(identity)||Double.IsNaN(remaining)||Double.IsInfinity(remaining)||remaining<0||remaining>100||observed>now.AddSeconds(5)||(resetAt.HasValue&&resetAt.Value<=now)){Clear();return null;}
            if(scope!=identity||reset!=resetAt){bool first=scope==null;Clear();scope=identity;reset=resetAt;if(first)Restore(now);}
            if(samples.Count>0){var last=samples[samples.Count-1];
                if(observed<last.At||observed-last.At>TimeSpan.FromSeconds(restored?300:90)||remaining>last.Remaining){samples.Clear();rate=null;restored=false;}
                else if(observed==last.At){if(remaining!=last.Remaining){samples.Clear();rate=null;DeleteSaved();}return rate;}
            }
            samples.Add(new Sample{At=observed.ToUniversalTime(),Remaining=remaining});
            // Provider quota is often rounded and arrives in steps. Average across
            // five minutes of fresh observations instead of expiring a step in one.
            while(samples.Count>1&&observed-samples[0].At>TimeSpan.FromSeconds(300))samples.RemoveAt(0);
            if(samples.Count>32)samples.RemoveAt(0);
            var firstSample=samples[0];double minutes=(observed-firstSample.At).TotalMinutes;
            rate=minutes>=0.25?(double?)Math.Max(0,(firstSample.Remaining-remaining)/minutes):null;
            restored=false;Save();
            return rate;
        }
        void Restore(DateTime now)
        {
            if(String.IsNullOrEmpty(path)||!File.Exists(path))return;
            try{
                if(new FileInfo(path).Length>8192)throw new FormatException();
                var saved=new JavaScriptSerializer().Deserialize<SavedSamples>(File.ReadAllText(path));
                if(saved==null||saved.Identity!=scope||saved.ResetAt!=ResetKey(reset)||saved.Samples==null||saved.Samples.Count>32)throw new FormatException();
                foreach(var sample in saved.Samples){
                    if(sample==null)continue;
                    sample.At=sample.At.ToUniversalTime();
                    if(Double.IsNaN(sample.Remaining)||Double.IsInfinity(sample.Remaining)||sample.Remaining<0||sample.Remaining>100||sample.At>now.AddSeconds(5)||now-sample.At>TimeSpan.FromSeconds(300))continue;
                    if(samples.Count>0&&sample.At<=samples[samples.Count-1].At)continue;
                    samples.Add(sample);
                }
                if(samples.Count>0)restored=true;
            }catch{samples.Clear();}
        }
        void Save()
        {
            if(String.IsNullOrEmpty(path)||String.IsNullOrEmpty(scope)||samples.Count==0)return;
            string temporary=path+".tmp";
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json=new JavaScriptSerializer().Serialize(new SavedSamples{Identity=scope,ResetAt=ResetKey(reset),Samples=samples});
                File.WriteAllText(temporary,json,new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }catch{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
        }
        void DeleteSaved(){try{if(!String.IsNullOrEmpty(path)&&File.Exists(path))File.Delete(path);}catch{}}
        static string ResetKey(DateTime? value){return value.HasValue?value.Value.ToUniversalTime().ToString("o",System.Globalization.CultureInfo.InvariantCulture):null;}
        public static string Format(double rate){return rate>0&&rate<0.1?"<0.1":rate.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);}
    }
}
#endif
