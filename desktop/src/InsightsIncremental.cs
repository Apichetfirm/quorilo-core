using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
namespace CreditMonitor.Insights {
 public sealed class UsageLogState {public string Session,Project,Model;public long Input,Output,Cache,Reasoning;public DateTime LastAt=DateTime.MinValue;public bool HasTotal;}
 public sealed class UsageFileCheckpoint {public long Length,Ticks,Offset;public int PrefixBytes;public string Prefix;public UsageLogState State=new UsageLogState();}
 public static class UsageImportCoordinator {
  static string Prefix(FileStream input,int length){long position=input.Position;try{input.Position=0;byte[] bytes=new byte[length];int read=0,n;while(read<length&&(n=input.Read(bytes,read,length-read))>0)read+=n;using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(bytes,0,read));}finally{input.Position=position;}}
  public static int ImportFile(HistoryStore store,string tool,string path,string device,string account,Func<bool> active){
   string key="incremental:"+InsightIdentity.Hash(path);var json=new JavaScriptSerializer();UsageFileCheckpoint checkpoint=null;try{checkpoint=json.Deserialize<UsageFileCheckpoint>(store.Setting(key,"null"));}catch{}
   var info=new FileInfo(path);if(checkpoint!=null&&info.Length==checkpoint.Length&&info.LastWriteTimeUtc.Ticks==checkpoint.Ticks)return 0;
   using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
    bool reuse=checkpoint!=null&&checkpoint.State!=null&&checkpoint.Offset>=0&&checkpoint.Offset<=input.Length&&input.Length>=checkpoint.Length&&checkpoint.PrefixBytes>=0&&checkpoint.PrefixBytes<=4096&&Prefix(input,checkpoint.PrefixBytes)==checkpoint.Prefix&&!(info.Length==checkpoint.Length&&info.LastWriteTimeUtc.Ticks!=checkpoint.Ticks);
    if(!reuse)checkpoint=new UsageFileCheckpoint();input.Position=checkpoint.Offset;int count=0;
    var events=UsageLogReader.Iterate(tool,Lines(input,checkpoint,active),device,account,InsightIdentity.Hash(path),checkpoint.State).Select(e=>{count++;return e;});
    store.Import(events);if(!active())throw new OperationCanceledException();info.Refresh();checkpoint.Length=input.Position;checkpoint.Ticks=info.LastWriteTimeUtc.Ticks;checkpoint.PrefixBytes=(int)Math.Min(4096,checkpoint.Length);checkpoint.Prefix=Prefix(input,checkpoint.PrefixBytes);store.Set(key,json.Serialize(checkpoint));return count;
   }
  }
  static IEnumerable<string> Lines(FileStream input,UsageFileCheckpoint checkpoint,Func<bool> active){byte[] buffer=new byte[65536];long position=input.Position;bool oversized=false;using(var line=new MemoryStream()){int read;while((read=input.Read(buffer,0,buffer.Length))>0){if(!active())throw new OperationCanceledException();for(int i=0;i<read;i++){byte b=buffer[i];position++;if(b==10){checkpoint.Offset=position;if(!oversized){string text=Encoding.UTF8.GetString(line.ToArray());if(position-line.Length-1==0)text=text.TrimStart('\uFEFF');yield return text;}line.SetLength(0);oversized=false;}else if(!oversized){if(line.Length>=2097152){oversized=true;line.SetLength(0);}else line.WriteByte(b);}}}if(!oversized&&line.Length>0)yield return Encoding.UTF8.GetString(line.ToArray()).TrimStart('\uFEFF');}}
 }
}
