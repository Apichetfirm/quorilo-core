using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    internal sealed class AdditionalProviderCredential { public string Account,Token; }
    internal sealed class AdditionalProviderVault
    {
        readonly string directory;
        public AdditionalProviderVault(string root){directory=Path.Combine(Path.GetFullPath(root),"provider-credentials");}
        public string PathFor(string id){if(!HubNames.Valid(id)||!HubNames.External(id))throw new ArgumentException("Invalid provider identity");return Path.Combine(directory,id+".dat");}
        static byte[] Entropy(string id){return Encoding.UTF8.GetBytes("Quorilo.additional-provider.v1|"+id);}
        void Safe(){for(var current=new DirectoryInfo(directory);current!=null;current=current.Parent)if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe credential storage");}
        public void Save(string id,AdditionalProviderCredential credential)
        {
            AdditionalProviderClient.Validate(HubNames.Provider(id),credential);Safe();Directory.CreateDirectory(directory);
            string path=PathFor(id),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe credential file");
            var bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(credential)),Entropy(id),DataProtectionScope.CurrentUser);
            try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public AdditionalProviderCredential Load(string id)
        {
            try{Safe();string path=PathFor(id);var info=new FileInfo(path);if(!info.Exists||info.Length>16384||(info.Attributes&FileAttributes.ReparsePoint)!=0)return null;
                var value=new JavaScriptSerializer().Deserialize<AdditionalProviderCredential>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy(id),DataProtectionScope.CurrentUser)));AdditionalProviderClient.Validate(HubNames.Provider(id),value);return value;
            }catch{return null;}
        }
        public void Forget(string id){Safe();string path=PathFor(id);if(File.Exists(path))File.Delete(path);}
    }
    internal sealed class AdditionalProviderClient:IDisposable
    {
        readonly HttpClient client;
        public AdditionalProviderClient(HttpMessageHandler handler=null){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;client=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};}
        public static void Validate(string provider,AdditionalProviderCredential credential)
        {
            if(credential==null||String.IsNullOrWhiteSpace(credential.Token)||credential.Token.Length>4096||credential.Token.Any(c=>c<'!'||c>'~')||String.IsNullOrWhiteSpace(credential.Account)||credential.Account.Length>254||credential.Account.Any(Char.IsControl))throw new InvalidDataException("Invalid provider settings");
            if(provider=="GitHubCopilot"&&!System.Text.RegularExpressions.Regex.IsMatch(credential.Account,"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,37}[a-zA-Z0-9])?\\z"))throw new InvalidDataException("Invalid GitHub username");
            if(provider!="Cursor"&&provider!="GitHubCopilot")throw new InvalidDataException("Unsupported provider");
        }
        async Task<string> Send(string provider,string path,AdditionalProviderCredential credential,CancellationToken cancellation,string body=null)
        {
            var origin=provider=="Cursor"?"https://api.cursor.com":"https://api.github.com";
            using(var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,origin+path)){
                request.Headers.UserAgent.ParseAdd("Quorilo-Monitor/1.0");request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Authorization=provider=="Cursor"?new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Token+":"))):new AuthenticationHeaderValue("Bearer",credential.Token);
                if(provider=="GitHubCopilot"){request.Headers.Remove("Accept");request.Headers.Accept.ParseAdd("application/vnd.github+json");request.Headers.Add("X-GitHub-Api-Version","2026-03-10");}
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false)){
                    if(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden)throw new InvalidOperationException("ProviderPermissionRequired");
                    if((int)response.StatusCode==429)throw new InvalidOperationException("ProviderRateLimited");
                    if(!response.IsSuccessStatusCode||(int)response.StatusCode>=300)throw new InvalidOperationException("ProviderUnavailable");
                    if(response.Content.Headers.ContentLength>262144)throw new InvalidDataException("Provider report too large");
                    using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var buffer=new MemoryStream()){
                        var bytes=new byte[8192];int read;while((read=await input.ReadAsync(bytes,0,bytes.Length,cancellation).ConfigureAwait(false))>0){if(buffer.Length+read>262144)throw new InvalidDataException("Provider report too large");buffer.Write(bytes,0,read);}return Encoding.UTF8.GetString(buffer.ToArray());
                    }
                }
            }
        }
        public async Task<HubSnapshot> ReadAsync(string id,AdditionalProviderCredential credential,DateTime now,CancellationToken cancellation)
        {
            string provider=HubNames.Provider(id);Validate(provider,credential);cancellation.ThrowIfCancellationRequested();
            if(provider=="Cursor"){
                string body=new JavaScriptSerializer().Serialize(new{searchTerm=credential.Account,page=1,pageSize=25});
                return ParseCursor(await Send(provider,"/teams/spend",credential,cancellation,body).ConfigureAwait(false),credential.Account,now);
            }
            var user=Object(await Send(provider,"/user",credential,cancellation).ConfigureAwait(false));
            if(!String.Equals(Text(user,"login"),credential.Account,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Provider account mismatch");
            string path="/users/"+Uri.EscapeDataString(credential.Account)+"/settings/billing/premium_request/usage?year="+now.Year+"&month="+now.Month;
            return ParseCopilot(await Send(provider,path,credential,cancellation).ConfigureAwait(false),credential.Account,now);
        }
        static Dictionary<string,object> Object(string json){if(json==null||json.Length>262144)throw new InvalidDataException("Invalid provider report");var value=new JavaScriptSerializer{MaxJsonLength=262144,RecursionLimit=32}.Deserialize<Dictionary<string,object>>(json);if(value==null)throw new InvalidDataException("Invalid provider report");return value;}
        static string Text(Dictionary<string,object> value,string key){object item;return value.TryGetValue(key,out item)?item as string:null;}
        static Dictionary<string,object> Map(Dictionary<string,object> value,string key){object item;if(!value.TryGetValue(key,out item)||!(item is Dictionary<string,object>))throw new InvalidDataException("Missing report object");return (Dictionary<string,object>)item;}
        static IEnumerable<Dictionary<string,object>> Items(Dictionary<string,object> value,string key){object raw;if(!value.TryGetValue(key,out raw)||!(raw is System.Collections.IEnumerable)||raw is string)throw new InvalidDataException("Missing report items");foreach(object item in (System.Collections.IEnumerable)raw){var map=item as Dictionary<string,object>;if(map==null)throw new InvalidDataException("Invalid report item");yield return map;}}
        static double Number(Dictionary<string,object> map,string key){object raw;if(!map.TryGetValue(key,out raw)||raw==null||raw is string||raw is bool)throw new InvalidDataException("Invalid report number");double number=Convert.ToDouble(raw,CultureInfo.InvariantCulture);if(Double.IsNaN(number)||Double.IsInfinity(number)||number<0||number>1e15)throw new InvalidDataException("Invalid report number");return number;}
        static string Identity(string provider,string account){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(provider+"|"+account.ToLowerInvariant()))).Replace("-","");}
        static HubSnapshot Report(string provider,string account,DateTime now)
        {
            var data=new HubSnapshot{schema=2,flavor=provider,hubWorker=true,commercial=Product.Commercial,processId=System.Diagnostics.Process.GetCurrentProcess().Id,state="Live",capability="UsageReport",source=provider+" official API",lastReceived=now.ToString("o"),updatedAt=now.ToString("o"),primaryLabel="Usage",secondaryLabel="Report",refreshSeconds=provider=="Cursor"?3600:300,reportPeriod=now.ToString("yyyy-MM",CultureInfo.InvariantCulture)};
#if !TRIAL
            data.sourceKey=Identity(provider,account);
#endif
            return data;
        }
        public static HubSnapshot ParseCursor(string json,string account,DateTime now)
        {
            var value=Object(json);var members=Items(value,"teamMemberSpend").Where(candidate=>String.Equals(Text(candidate,"email"),account,StringComparison.OrdinalIgnoreCase)||String.Equals(Text(candidate,"userId"),account,StringComparison.Ordinal)).ToArray();
            if(members.Length!=1)throw new InvalidDataException("Selected Cursor member not uniquely present");
            var row=members[0];double spent=Number(row,"spendCents")/100;var report=Report("Cursor",account,now);report.reportSpendUsd=spent;report.reportSummary="On-demand ใช้ไป $"+spent.ToString("0.##",CultureInfo.InvariantCulture)+" · ข้อมูลรอบบิลจาก Cursor";
            object limit;
            if(row.TryGetValue("effectivePerUserLimitDollars",out limit)&&limit!=null){double budget=Number(row,"effectivePerUserLimitDollars");if(budget>0){report.primary=new HubMetric{remaining=Math.Max(0,(budget-spent)/budget*100)};report.primaryLabel="Budget";report.capability="SpendBudget";report.reportBudgetUsd=budget;report.reportSummary+=" · งบ $"+budget.ToString("0.##",CultureInfo.InvariantCulture);}}
            report.detail="งบ on-demand แยกจาก included quota · API ไม่ส่งเวลารีเซ็ตที่ยืนยันได้ · ตรวจรายชั่วโมง";return report;
        }
        public static HubSnapshot ParseCopilot(string json,string account,DateTime now)
        {
            var value=Object(json);if(!String.Equals(Text(value,"user"),account,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Provider account mismatch");
            var period=Map(value,"timePeriod");if(Number(period,"year")!=now.Year||Number(period,"month")!=now.Month)throw new InvalidDataException("Unexpected report period");
            var totals=new Dictionary<string,double>();foreach(var item in Items(value,"usageItems")){
                if(!String.Equals(Text(item,"product"),"Copilot",StringComparison.OrdinalIgnoreCase))continue;
                string unit=Text(item,"unitType");if(String.IsNullOrEmpty(unit)||unit.Length>32||unit.Any(c=>!Char.IsLetterOrDigit(c)&&c!='_'&&c!=' '&&c!='-'))throw new InvalidDataException("Invalid report unit");
                double quantity=Number(item,"grossQuantity"),current;totals.TryGetValue(unit,out current);totals[unit]=current+quantity;if(totals.Count>10||totals[unit]>1e15)throw new InvalidDataException("Invalid report total");
            }
            var report=Report("GitHubCopilot",account,now);report.reportSummary="เดือน "+now.ToString("yyyy-MM",CultureInfo.InvariantCulture)+" · "+(totals.Count==0?"API ยังไม่มีรายการใช้งาน":String.Join(" · ",totals.Select(pair=>pair.Value.ToString("0.##",CultureInfo.InvariantCulture)+" "+pair.Key)));
            if(totals.Count==1){report.reportUsed=totals.First().Value;report.reportUnit=totals.First().Key;}else if(totals.Count==0){report.reportUsed=0;report.reportUnit="records";}else{report.reportUnit="multiple_units";}
            report.detail="รายงาน Copilot ที่เรียกเก็บจากบัญชีส่วนตัว · ไม่รวมสิทธิ์ที่องค์กรจ่าย · ไม่มี allowance เพื่อคำนวณ % คงเหลือ";return report;
        }
        public void Dispose(){client.Dispose();}
    }
}
