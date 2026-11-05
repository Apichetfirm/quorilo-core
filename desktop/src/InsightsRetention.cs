using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;
namespace CreditMonitor.Insights {
 public sealed partial class HistoryStore {
  void InitRetention(){InitQuotaRanges();Run("CREATE INDEX IF NOT EXISTS events_sync ON events(at,k)");Run("CREATE TABLE IF NOT EXISTS retired(k TEXT PRIMARY KEY,i INTEGER,o INTEGER,c INTEGER,w INTEGER,r INTEGER)");if(!Query("PRAGMA table_info(retired)").Any(r=>(string)r["name"]=="payload"))Run("ALTER TABLE retired ADD COLUMN payload TEXT");if(!Query("PRAGMA table_info(retired)").Any(r=>(string)r["name"]=="at"))Run("ALTER TABLE retired ADD COLUMN at TEXT");if(!Query("PRAGMA table_info(retired)").Any(r=>(string)r["name"]=="model"))Run("ALTER TABLE retired ADD COLUMN model TEXT DEFAULT 'unknown'");if(!Query("PRAGMA table_info(retired)").Any(r=>(string)r["name"]=="project"))Run("ALTER TABLE retired ADD COLUMN project TEXT DEFAULT 'unattributed'");Run("CREATE INDEX IF NOT EXISTS retired_sync ON retired(at,k)");Run("CREATE TABLE IF NOT EXISTS buckets(k TEXT PRIMARY KEY,tool TEXT,provider TEXT,account TEXT,device TEXT,model TEXT,project TEXT,session TEXT,at TEXT,i INTEGER,o INTEGER,c INTEGER,w INTEGER,r INTEGER,granularity TEXT,requests INTEGER)");Run("CREATE INDEX IF NOT EXISTS buckets_time ON buckets(at)");}
  // Receipts prevent an old JSONL file from recreating detail already rolled up.
  // Counts remain separate from quota percentages and preserve request weighting.
  public void Maintain(DateTime now){
   foreach(var stage in new[]{new[]{"events","","hour","30"},new[]{"buckets","hour","day","90"},new[]{"buckets","day","month","365"}}){
    // Bounded transactions release the shared connection between batches.
    for(int batch=0;batch<10;batch++){int count;lock(gate){Run("BEGIN IMMEDIATE");try{count=Rollup(stage[0],stage[1]==""?null:stage[1],now.AddDays(-Int32.Parse(stage[3],CultureInfo.InvariantCulture)),stage[2]);Run("COMMIT");}catch{Run("ROLLBACK");throw;}}if(count<100)break;}
   }
   MaintainQuota(now);Run("UPDATE retired SET payload=NULL,model=NULL,project=NULL,at=NULL WHERE at<?",InsightIdentity.Time(now.AddDays(-365)));
  }
  int Rollup(string table,string oldGranularity,DateTime cutoff,string granularity){
   var rows=Query("SELECT * FROM "+table+" WHERE at<?"+(oldGranularity==null?"":" AND granularity=?")+" LIMIT 100",oldGranularity==null?new object[]{InsightIdentity.Time(cutoff)}:new object[]{InsightIdentity.Time(cutoff),oldGranularity});
   foreach(var row in rows){var e=RowEvent(row);DateTime at=e.At;at=granularity=="hour"?new DateTime(at.Year,at.Month,at.Day,at.Hour,0,0,DateTimeKind.Utc):granularity=="day"?at.Date:new DateTime(at.Year,at.Month,1,0,0,0,DateTimeKind.Utc);
    string time=InsightIdentity.Time(at);string key=BucketKey(e,granularity,at);
    Run("INSERT INTO buckets VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(k) DO UPDATE SET i=i+excluded.i,o=o+excluded.o,c=c+excluded.c,w=w+excluded.w,r=r+excluded.r,requests=requests+excluded.requests",key,e.Tool,e.Provider,e.Account,e.Device,e.Model,e.Project,e.Session,time,e.Input,e.Output,e.Cached,e.CacheWrite,e.Reasoning,granularity,e.RequestEvents);
    if(table=="events")Run("INSERT OR IGNORE INTO retired(k,i,o,c,w,r,payload,at,model,project) VALUES(?,?,?,?,?,?,?,?,?,?)",e.Key,e.Input,e.Output,e.Cached,e.CacheWrite,e.Reasoning,Pack(e),InsightIdentity.Time(e.At),e.Model,e.Project);
    Run("DELETE FROM "+table+" WHERE k=?",e.Key);
   }return rows.Count;
  }
  static string BucketKey(UsageEvent e,string granularity,DateTime at){return InsightIdentity.Hash(new JavaScriptSerializer().Serialize(new[]{granularity,e.Tool,e.Provider,e.Account,e.Device,e.Model,e.Project,e.Session,InsightIdentity.Time(at)}));}
  void CorrectRetired(UsageEvent incoming,Dictionary<string,object> receipt){Run("SAVEPOINT correction");try{CorrectRetiredCore(incoming,receipt);Run("RELEASE correction");}catch{Run("ROLLBACK TO correction");Run("RELEASE correction");throw;}}
  void CorrectRetiredCore(UsageEvent incoming,Dictionary<string,object> receipt){
   if(receipt["payload"]==null)throw new FormatException("Correction older than the 365-day sync archive needs an explicit fresh import");
   var old=Unpack((string)receipt["payload"]);long input=Math.Max(old.Input,incoming.Input),output=Math.Max(old.Output,incoming.Output),cache=Math.Max(old.Cached,incoming.Cached),write=Math.Max(old.CacheWrite,incoming.CacheWrite),reason=Math.Max(old.Reasoning,incoming.Reasoning);input=Math.Max(input,cache+write);output=Math.Max(output,reason);
   foreach(string granularity in new[]{"hour","day","month"}){DateTime at=granularity=="hour"?new DateTime(old.At.Year,old.At.Month,old.At.Day,old.At.Hour,0,0,DateTimeKind.Utc):granularity=="day"?old.At.Date:new DateTime(old.At.Year,old.At.Month,1,0,0,0,DateTimeKind.Utc);string bucket=BucketKey(old,granularity,at);if(Query("SELECT k FROM buckets WHERE k=?",bucket).Count==0)continue;
    string model=old.Model=="unknown"?incoming.Model:old.Model,project=old.Project=="unattributed"?incoming.Project:old.Project;bool moved=model!=old.Model||project!=old.Project;
    if(moved){Run("UPDATE buckets SET i=i-?,o=o-?,c=c-?,w=w-?,r=r-?,requests=requests-1 WHERE k=?",old.Input,old.Output,old.Cached,old.CacheWrite,old.Reasoning,bucket);Run("DELETE FROM buckets WHERE k=? AND requests=0",bucket);}else Run("UPDATE buckets SET i=i+?,o=o+?,c=c+?,w=w+?,r=r+? WHERE k=?",input-old.Input,output-old.Output,cache-old.Cached,write-old.CacheWrite,reason-old.Reasoning,bucket);
    old.Input=input;old.Output=output;old.Cached=cache;old.CacheWrite=write;old.Reasoning=reason;old.Model=model;old.Project=project;
    if(moved)Run("INSERT INTO buckets VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(k) DO UPDATE SET i=i+excluded.i,o=o+excluded.o,c=c+excluded.c,w=w+excluded.w,r=r+excluded.r,requests=requests+excluded.requests",BucketKey(old,granularity,at),old.Tool,old.Provider,old.Account,old.Device,old.Model,old.Project,old.Session,InsightIdentity.Time(at),old.Input,old.Output,old.Cached,old.CacheWrite,old.Reasoning,granularity,1);
    Run("UPDATE retired SET i=?,o=?,c=?,w=?,r=?,payload=?,model=?,project=? WHERE k=?",input,output,cache,write,reason,Pack(old),old.Model,old.Project,old.Key);return;
   }throw new FormatException("Archived event bucket is missing");
  }
  static string Pack(UsageEvent e){var json=new JavaScriptSerializer().Serialize(e);using(var output=new MemoryStream()){using(var gzip=new GZipStream(output,CompressionMode.Compress,true)){byte[] bytes=Encoding.UTF8.GetBytes(json);gzip.Write(bytes,0,bytes.Length);}return Convert.ToBase64String(output.ToArray());}}
  static UsageEvent Unpack(string payload){using(var input=new MemoryStream(Convert.FromBase64String(payload)))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var reader=new StreamReader(gzip,Encoding.UTF8)){return new JavaScriptSerializer().Deserialize<UsageEvent>(reader.ReadToEnd());}}
  // Compact canonical metadata kept for 365 days permits sync enabled after rollup.
  // Never upload aggregate rows as independent requests: identical logs dedup by raw key.
  public static string SyncRevision(UsageEvent e,string generation,bool shareProjects=false){return String.Join("|",new[]{"v1",generation,e.Model,shareProjects?e.Project:"unattributed",e.Input.ToString(CultureInfo.InvariantCulture),e.Output.ToString(CultureInfo.InvariantCulture),e.Cached.ToString(CultureInfo.InvariantCulture),e.CacheWrite.ToString(CultureInfo.InvariantCulture),e.Reasoning.ToString(CultureInfo.InvariantCulture)});}
  public IEnumerable<UsageEvent> SyncSource(DateTime from,string generation=null,bool shareProjects=false){
   // A separate read-only WAL connection leaves UI writes free during archive work.
   using(var reader=new HistoryStore(filePath,true))foreach(string table in new[]{"events","retired"}){
    string cursor=InsightIdentity.Time(from),after="";
    for(;;){var rows=reader.Query("SELECT e.* FROM "+table+" e LEFT JOIN settings s ON s.k='synced:'||e.k WHERE e.at>=? AND (e.at>? OR (e.at=? AND e.k>?))"+(table=="retired"?" AND e.payload IS NOT NULL":"")+(generation==null?"":" AND (s.v IS NULL OR s.v<>'v1|'||?||'|'||e.model||'|'||CASE WHEN ?=1 THEN e.project ELSE 'unattributed' END||'|'||e.i||'|'||e.o||'|'||e.c||'|'||e.w||'|'||e.r)")+" ORDER BY e.at,e.k LIMIT 100",generation==null?new object[]{InsightIdentity.Time(from),cursor,cursor,after}:new object[]{InsightIdentity.Time(from),cursor,cursor,after,generation,shareProjects?1:0});
     if(rows.Count==0)break;cursor=(string)rows[rows.Count-1]["at"];after=(string)rows[rows.Count-1]["k"];foreach(var row in rows)yield return table=="events"?RowEvent(row):Unpack((string)row["payload"]);
    }
   }
  }
  public List<UsageEvent> SyncEvents(DateTime from){return SyncSource(from).GroupBy(e=>e.Key).Select(g=>g.OrderByDescending(e=>e.Total).First()).OrderBy(e=>e.At).ToList();}
  void InitQuotaRanges(){Run("CREATE TABLE IF NOT EXISTS quota_ranges(k TEXT PRIMARY KEY,provider TEXT,scope TEXT,window TEXT,reset TEXT,day TEXT,first_at TEXT,last_at TEXT,first_remaining REAL,last_remaining REAL,min_remaining REAL,max_remaining REAL)");}
  void MaintainQuota(DateTime now){for(int batch=0;batch<10;batch++){int count;lock(gate){var rows=Query("SELECT rowid,* FROM quotas WHERE at<? ORDER BY at LIMIT 100",InsightIdentity.Time(now.AddDays(-30)));count=rows.Count;Run("BEGIN IMMEDIATE");try{foreach(var row in rows){string at=(string)row["at"],day=at.Substring(0,10),key=InsightIdentity.Hash(new JavaScriptSerializer().Serialize(new[]{row["provider"],row["scope"],row["window"],row["reset"],day}));double remaining=Convert.ToDouble(row["remaining"]);Run("INSERT INTO quota_ranges VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(k) DO UPDATE SET first_remaining=CASE WHEN excluded.first_at<first_at THEN excluded.first_remaining ELSE first_remaining END,last_remaining=CASE WHEN excluded.last_at>last_at THEN excluded.last_remaining ELSE last_remaining END,first_at=MIN(first_at,excluded.first_at),last_at=MAX(last_at,excluded.last_at),min_remaining=MIN(min_remaining,excluded.min_remaining),max_remaining=MAX(max_remaining,excluded.max_remaining)",key,row["provider"],row["scope"],row["window"],row["reset"],day,at,at,remaining,remaining,remaining,remaining);Run("DELETE FROM quotas WHERE rowid=?",row["rowid"]);}Run("COMMIT");}catch{Run("ROLLBACK");throw;}}if(count<100)break;}Run("DELETE FROM quota_ranges WHERE day<?",now.AddDays(-365).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));}
  public System.Collections.IEnumerable QuotaHistory(){lock(gate){var raw=Quotas(250).Select(q=>new{Provider=q.Provider,Scope=q.Scope,Window=q.Window,AtUtc=InsightIdentity.Time(q.At),Minimum=q.Remaining,Maximum=q.Remaining,First=q.Remaining,Last=q.Remaining,Reset=q.ResetAt.HasValue?InsightIdentity.Time(q.ResetAt.Value):"unknown",Granularity="sample"}).ToList();var older=Query("SELECT * FROM quota_ranges ORDER BY day DESC LIMIT 250").Select(r=>new{Provider=(string)r["provider"],Scope=(string)r["scope"],Window=(string)r["window"],AtUtc=(string)r["day"],Minimum=Convert.ToDouble(r["min_remaining"]),Maximum=Convert.ToDouble(r["max_remaining"]),First=Convert.ToDouble(r["first_remaining"]),Last=Convert.ToDouble(r["last_remaining"]),Reset=(string)r["reset"]??"unknown",Granularity="day/window"});return raw.Concat(older).ToList();}}
  public void ClearHistory(){lock(gate){Run("BEGIN IMMEDIATE");try{Run("DELETE FROM events");Run("DELETE FROM buckets");Run("DELETE FROM retired");Run("DELETE FROM quotas");Run("DELETE FROM quota_ranges");Run("DELETE FROM settings WHERE k LIKE 'scan:%' OR k LIKE 'synced:%' OR k LIKE 'incremental:%' OR k='lastScan'");Run("COMMIT");Run("PRAGMA wal_checkpoint(TRUNCATE)");}catch{Run("ROLLBACK");throw;}}}
 }
}
