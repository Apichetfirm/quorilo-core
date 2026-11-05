using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace CreditMonitor.Insights {
 public static class UsageLogReader {
  static IDictionary<string,object> Obj(object x){return x as IDictionary<string,object>??new Dictionary<string,object>();}
  static object Get(IDictionary<string,object> x,string k){object v;return x.TryGetValue(k,out v)?v:null;}
  static string Str(IDictionary<string,object> x,string k,string fallback){return Convert.ToString(Get(x,k)) is string&&!String.IsNullOrWhiteSpace(Convert.ToString(Get(x,k)))?Convert.ToString(Get(x,k)):fallback;}
  static long Num(IDictionary<string,object> x,string k){long n;return Int64.TryParse(Convert.ToString(Get(x,k)),out n)&&n>=0&&n<=1000000000000L?n:0;}
  static bool Counter(IDictionary<string,object> x,string key,bool required,out long n){n=0;object raw=Get(x,key);if(raw==null)return !required;if(!(raw is int)&&!(raw is long))return false;n=Convert.ToInt64(raw);return n>=0&&n<=1000000000000L;}
  public static IEnumerable<UsageEvent> ReadFile(string tool,string path,string device,string account){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(f)){foreach(var e in Iterate(tool,Lines(r),device,account,InsightIdentity.Hash(path)))yield return e;}}
  static IEnumerable<string> Lines(StreamReader r){string line;while((line=r.ReadLine())!=null)yield return line;}
  public static List<UsageEvent> ParseLines(string tool,IEnumerable<string> lines,string device,string account){return Iterate(tool,lines,device,account,"fixture").GroupBy(e=>e.Key).Select(g=>g.OrderByDescending(e=>e.Total).First()).ToList();}
  internal static IEnumerable<UsageEvent> Iterate(string tool,IEnumerable<string> lines,string device,string account,string source,UsageLogState state=null){
   state=state??new UsageLogState();string session=state.Session??source,project=state.Project??"unattributed",model=state.Model??"unknown";long lastI=state.Input,lastO=state.Output,lastC=state.Cache,lastR=state.Reasoning;bool hasTotal=state.HasTotal;DateTime lastAt=state.LastAt;
   try{foreach(string line in lines){if(line.Length>2097152)continue;UsageEvent result=null;try{
    var root=Obj(new JavaScriptSerializer{MaxJsonLength=2097152}.DeserializeObject(line));string type=Str(root,"type","");var payload=Obj(Get(root,"payload"));
    if(tool=="Codex"){
     if(type=="session_meta"){string next=Str(payload,"id",session);if(next!=session){lastI=lastO=lastC=lastR=0;hasTotal=false;lastAt=DateTime.MinValue;}session=next;project=InsightIdentity.Project(Str(payload,"cwd",""));}
     if(type=="turn_context"){model=Str(payload,"model",model);if(Get(payload,"cwd")!=null)project=InsightIdentity.Project(Str(payload,"cwd",""));}
     if(type!="event_msg"||Str(payload,"type","")!="token_count")continue;
     var info=Obj(Get(payload,"info"));var total=Obj(Get(info,"total_token_usage"));if(total.Count==0)continue;
     long i,o,c,r;if(!Counter(total,"input_tokens",true,out i)||!Counter(total,"output_tokens",true,out o)||!Counter(total,"cached_input_tokens",false,out c)||!Counter(total,"reasoning_output_tokens",false,out r)||c>i||r>o)continue;
     DateTime at;if(!DateTime.TryParse(Str(root,"timestamp",""),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out at)||at<lastAt||at>DateTime.UtcNow.AddMinutes(1)||at.Year<2020)continue;
     bool reset=hasTotal&&(i<lastI||o<lastO||c<lastC||r<lastR);long di=i-lastI,do_=o-lastO,dc=c-lastC,dr=r-lastR;
     if(reset){lastI=i;lastO=o;lastC=c;lastR=r;lastAt=at;continue;}if(di+do_==0)continue;
     result=new UsageEvent{Tool=tool,Provider="OpenAI",Account=account,Device=device,Session=session,Project=project,Model=model,At=at,Input=di,Output=do_,Cached=dc,Reasoning=dr,Key=InsightIdentity.Hash(tool+"|"+session+"|"+i+"|"+o+"|"+Str(root,"timestamp",""))};
     result.Validate();lastI=i;lastO=o;lastC=c;lastR=r;hasTotal=true;lastAt=at;
    }else if(tool=="Claude"){
     if(type!="assistant")continue;var message=Obj(Get(root,"message"));var usage=Obj(Get(message,"usage"));if(usage.Count==0)continue;string messageId=Str(message,"id","");if(messageId=="")continue;
     DateTime at;if(!DateTime.TryParse(Str(root,"timestamp",""),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out at))continue;
     session=Str(root,"sessionId",session);long cached,write,input,output;if(!Counter(usage,"input_tokens",true,out input)||!Counter(usage,"output_tokens",true,out output)||!Counter(usage,"cache_read_input_tokens",false,out cached)||!Counter(usage,"cache_creation_input_tokens",false,out write))continue;
     result=new UsageEvent{Tool=tool,Provider="Anthropic",Account=account,Device=device,Session=session,Project=InsightIdentity.Project(Str(root,"cwd","")),Model=Str(message,"model","unknown"),At=at,Input=input+cached+write,Output=output,Cached=cached,CacheWrite=write,Key=InsightIdentity.Hash(tool+"|"+session+"|"+Str(root,"requestId",messageId)+"|"+messageId)};
    }
    if(result!=null)result.Validate();
   }catch{result=null;}if(result!=null)yield return result;}}finally{state.Session=session;state.Project=project;state.Model=model;state.Input=lastI;state.Output=lastO;state.Cache=lastC;state.Reasoning=lastR;state.HasTotal=hasTotal;state.LastAt=lastAt;}
  }
 }
}
