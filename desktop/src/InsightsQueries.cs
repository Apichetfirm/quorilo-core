using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
namespace CreditMonitor.Insights {
 public sealed class InsightSummary {
  public long Tokens,Input,Output,Cache;
  public int Requests,Sessions,UnknownPriceRequests;
  public DateTime? First,Last;
  public Dictionary<string,double> Costs=new Dictionary<string,double>();
 }
 public sealed class InsightGroup {public string Label;public long Tokens;public int Requests;public double? Cost;}
 public sealed partial class InsightQueries : IDisposable {
  readonly HistoryStore store;readonly List<PriceRule> prices;
  public InsightQueries(HistoryStore history,List<PriceRule> rules){store=history;prices=rules;}
  bool ownsSnapshot;
  public InsightQueries Snapshot(HistoryFilter filter=null){return new InsightQueries(store.OpenSnapshot(),prices){ownsSnapshot=true};}
  public void Dispose(){if(ownsSnapshot){store.Dispose();ownsSnapshot=false;}}
  List<UsageEvent> Events(HistoryFilter filter){return store.SnapshotEvents(filter);}
  PriceRule Price(UsageEvent e){return prices.Where(p=>p.Model==e.Model&&p.EffectiveAt<=e.At).OrderByDescending(p=>p.EffectiveAt).FirstOrDefault();}
  public InsightSummary Summarize(HistoryFilter filter){return SqlSummary(filter);}
  public List<InsightGroup> Group(HistoryFilter filter,string dimension,bool utc){return SqlGroup(filter,dimension,utc);}
  public string Export(HistoryFilter filter,bool privacy){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024}.Serialize(new{schemaVersion=1,exportedAt=InsightIdentity.Time(DateTime.UtcNow),privacy=privacy,costBasis="Standard API estimate; excludes tools, fast mode and regional adjustments",events=Events(filter).Select(e=>new{key=privacy?null:e.Key,tool=e.Tool,provider=e.Provider,account=privacy?null:e.Account,device=privacy?null:e.Device,model=e.Model,project=privacy?null:e.Project,session=privacy?null:e.Session,at=InsightIdentity.Time(e.At),granularity=e.Granularity,requestEvents=e.RequestEvents,input=e.Input,output=e.Output,cachedInput=e.Cached,cacheWrite=e.CacheWrite,reasoning=e.Reasoning,total=e.Total,estimatedCost=privacy||Price(e)==null?(double?)null:Price(e).Estimate(e),currency=privacy||Price(e)==null?null:Price(e).Currency,priceVersion=privacy||Price(e)==null?null:Price(e).Version})});}
  public System.Collections.IEnumerable Detail(HistoryFilter filter,bool privacy){return store.DetailEvents(filter,250).Select(e=>new{TimeUtc=InsightIdentity.Time(e.At),Tool=e.Tool,Model=e.Model,Granularity=e.Granularity,Requests=e.RequestEvents,Tokens=e.Total,Cached=e.Cached,Project=privacy?"hidden":e.Project,Device=privacy?"hidden":e.Device,Session=privacy?"hidden":e.Session,ApiUSD=privacy||Price(e)==null||Price(e).Currency!="USD"?(double?)null:Price(e).Estimate(e)}).ToList();}
  static string Cell(object value){string text=Convert.ToString(value,CultureInfo.InvariantCulture);if(value is string&&text.Length>0&&"=+@-\t\r".IndexOf(text[0])>=0)text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
  public string Csv(HistoryFilter filter,bool privacy){var lines=new List<string>{"tool,model,atUtc,granularity,requestEvents,input,output,cachedInput,cacheWrite,total,project,device,session,estimatedCost,currency"};foreach(var e in Events(filter)){var p=Price(e);lines.Add(String.Join(",",new object[]{e.Tool,e.Model,InsightIdentity.Time(e.At),e.Granularity,e.RequestEvents,e.Input,e.Output,e.Cached,e.CacheWrite,e.Total,privacy?null:e.Project,privacy?null:e.Device,privacy?null:e.Session,privacy||p==null?null:(object)p.Estimate(e),privacy||p==null?null:p.Currency}.Select(Cell)));}return String.Join("\r\n",lines);}
  public string Weekly(DateTime until){return SqlWeekly(until);}

 }
 public static class InsightBudget {
  public static string Check(double actual,double limit,string currency,double estimate,string estimateCurrency){if(currency!=estimateCurrency&&estimate!=0)return "different-currency";if(limit<=0||Double.IsNaN(limit)||Double.IsInfinity(limit))return "unset";return actual>=limit?"over":actual>=limit*0.8?"warning":"within";}
 }
 public static class InsightPrices {
  // Reference snapshot, standard global API-equivalent rates; not provider invoices.
  // Explicit exact names only. Unknown variants stay unpriced. No remote auto-download.
  public static List<PriceRule> Defaults(){var rules=new List<PriceRule>();Add(rules,"claude-opus-4-6",5,25,0.5,6.25);Add(rules,"claude-sonnet-4-6",3,15,0.3,3.75);Add(rules,"claude-haiku-4-5",1,5,0.1,1.25);rules.Add(new PriceRule{Model="gpt-5.3-codex",Input=1.75,Output=14,CachedInput=0.175,CacheWrite=1.75,Currency="USD",Version="reference-2026-10-18-standard",Source="https://developers.openai.com/api/docs/pricing",EffectiveAt=new DateTime(2020,1,1)});return rules;}
  static void Add(List<PriceRule> rules,string model,double input,double output,double cache,double write){rules.Add(new PriceRule{Model=model,Input=input,Output=output,CachedInput=cache,CacheWrite=write,Version="reference-2026-10-18-standard-5m-cache",Source="https://platform.claude.com/docs/en/about-claude/pricing",EffectiveAt=new DateTime(2020,1,1),Currency="USD"});}
 }
}
