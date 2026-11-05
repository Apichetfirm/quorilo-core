using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace CreditMonitor.Insights {
 public sealed class UsageEvent {
  public string Key,Tool,Provider,Account,Device,Model,Project,Session;
  public DateTime At;
  public bool Aggregate;public string Granularity="event";public int RequestEvents=1;
  public long Input,Output,Cached,CacheWrite,Reasoning;
  public long Total {get{return Input+Output;}}
  public void Validate(){if(String.IsNullOrEmpty(Key)||Key.Length>512||String.IsNullOrEmpty(Tool)||At.Kind!=DateTimeKind.Utc||At.Year<2020||At>DateTime.UtcNow.AddMinutes(1))throw new FormatException("Invalid usage identity/time");foreach(long n in new[]{Input,Output,Cached,CacheWrite,Reasoning})if(n<0||n>1000000000000L)throw new FormatException("Invalid usage counter");if(Cached+CacheWrite>Input||Reasoning>Output)throw new FormatException("Invalid counter subsets");}
 }
 public sealed class QuotaSample {public string Provider,Scope,Window;public DateTime At;public DateTime? ResetAt;public double Remaining;public double? Minutes;}
 public sealed class PriceRule {
  public string Model,Currency="USD",Version,Source;
  public DateTime EffectiveAt;
  public double Input,Output,CachedInput,CacheWrite;
  public double Estimate(UsageEvent e){return ((e.Input-e.Cached-e.CacheWrite)*Input+e.Cached*CachedInput+e.CacheWrite*CacheWrite+e.Output*Output)/1000000.0;}
 }
 public sealed class PaceResult {public string Status;public double? Difference;}
 public static class Pace {
  public static PaceResult Assess(double used,double? elapsed){if(Double.IsNaN(used)||used<0||used>100||!elapsed.HasValue||Double.IsNaN(elapsed.Value)||elapsed<0||elapsed>100)return new PaceResult{Status="unknown"};double diff=used-elapsed.Value;return new PaceResult{Status=diff>15?"ahead":"steady",Difference=diff};}
 }
 public sealed class HistoryFilter {
  public DateTime From=DateTime.UtcNow.AddDays(-30),To=DateTime.UtcNow.AddMinutes(1);
  public string Tool,Model,Project,Device,Account;
  public bool Matches(UsageEvent e){return e.At>=From&&e.At<To&&(String.IsNullOrEmpty(Tool)||e.Tool==Tool)&&(String.IsNullOrEmpty(Model)||e.Model==Model)&&(String.IsNullOrEmpty(Project)||e.Project==Project)&&(String.IsNullOrEmpty(Device)||e.Device==Device)&&(String.IsNullOrEmpty(Account)||e.Account==Account);}
 }
 public static class InsightCalendar {
  public static HistoryFilter Range(int days,DateTime now,bool utc,DateTime? selected=null,TimeZoneInfo zone=null){zone=zone??TimeZoneInfo.Local;now=now.ToUniversalTime();DateTime date=utc?now.Date:TimeZoneInfo.ConvertTimeFromUtc(now,zone).Date;var result=new HistoryFilter{From=utc?date.AddDays(1-days):Boundary(date.AddDays(1-days),zone),To=now.AddMinutes(1)};if(selected.HasValue){date=utc?selected.Value.ToUniversalTime().Date:TimeZoneInfo.ConvertTimeFromUtc(selected.Value.ToUniversalTime(),zone).Date;result.From=utc?DateTime.SpecifyKind(date,DateTimeKind.Utc):Boundary(date,zone);result.To=utc?DateTime.SpecifyKind(date.AddDays(1),DateTimeKind.Utc):Boundary(date.AddDays(1),zone);}return result;}
  static DateTime Boundary(DateTime date,TimeZoneInfo zone){date=DateTime.SpecifyKind(date,DateTimeKind.Unspecified);while(zone.IsInvalidTime(date))date=date.AddMinutes(1);if(zone.IsAmbiguousTime(date))return new DateTime(date.Ticks-zone.GetAmbiguousTimeOffsets(date).Max().Ticks,DateTimeKind.Utc);return TimeZoneInfo.ConvertTimeToUtc(date,zone);}
 }
 public static class InsightIdentity {
  public static string Hash(string value){using(var hash=SHA256.Create())return String.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value??"")).Select(x=>x.ToString("x2")));}
  public static string Project(string path){return String.IsNullOrWhiteSpace(path)?"unattributed":"project-"+Hash(path.Replace('\\','/').TrimEnd('/')).Substring(0,12);}
  public static string Time(DateTime at){return at.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture);}
 }
}
