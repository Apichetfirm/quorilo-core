using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
namespace CreditMonitor.Insights {
 public sealed class MonthlyBudgetItem {
  public string Id,Month,Name,Currency;
  public double Limit,Paid;
 }
 public sealed class MonthlyBudgetTotal {
  public string Currency;public double Limit,Paid;
 }
 // One versioned setting keeps each edit atomic without changing the history schema.
 public sealed class MonthlyBudgetStore {
  readonly HistoryStore store;readonly object gate=new object();
  public MonthlyBudgetStore(HistoryStore history){store=history;}
  public static string CurrentMonth {get{return DateTime.Now.ToString("yyyy-MM",CultureInfo.InvariantCulture);}}
  public static void ValidateMonth(string month){DateTime value;if(String.IsNullOrEmpty(month)||month.Length!=7||!DateTime.TryParseExact(month,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out value))throw new FormatException("ระบุเดือนเป็น ค.ศ. YYYY-MM เช่น 2026-10");}
  static void Validate(MonthlyBudgetItem item){if(item==null)throw new FormatException("ไม่พบรายการงบ");ValidateMonth(item.Month);if(String.IsNullOrWhiteSpace(item.Name)||item.Name.Length>80||item.Name.Any(Char.IsControl))throw new FormatException("ชื่อรายการต้องยาว 1–80 ตัวอักษร");if(!new[]{"THB","USD","EUR","GBP","JPY"}.Contains(item.Currency))throw new FormatException("เลือกสกุลเงินที่รองรับ");if(Double.IsNaN(item.Limit)||Double.IsInfinity(item.Limit)||item.Limit<0||item.Limit>1000000000||Double.IsNaN(item.Paid)||Double.IsInfinity(item.Paid)||item.Paid<0||item.Paid>1000000000)throw new FormatException("งบและยอดจ่ายต้องอยู่ระหว่าง 0 ถึง 1,000,000,000");if(item.Id!=null&&(item.Id.Length>100||item.Id.Any(Char.IsControl)))throw new FormatException("รหัสรายการไม่ถูกต้อง");}
  List<MonthlyBudgetItem> Read(){string json=store.Setting("budgets.v2",null);if(json!=null){try{var items=new JavaScriptSerializer().Deserialize<List<MonthlyBudgetItem>>(json);if(items==null||items.Count>5000)throw new FormatException();foreach(var item in items){Validate(item);if(String.IsNullOrEmpty(item.Id))throw new FormatException();}if(items.Select(x=>x.Id).Distinct().Count()!=items.Count)throw new FormatException();return items;}catch{throw new FormatException("อ่านรายการงบไม่ได้ · ข้อมูลเดิมยังอยู่ กรุณาตรวจสำเนาข้อมูลก่อนแก้ไข");}}
   var legacy=new List<MonthlyBudgetItem>();string month=store.Setting("budgetMonth","");if(month!=""){ValidateMonth(month);int year=Int32.Parse(month.Substring(0,4),CultureInfo.InvariantCulture);if(year>=2400&&year-543>=1900&&year-543<=DateTime.Now.Year+1)month=(year-543).ToString("0000",CultureInfo.InvariantCulture)+month.Substring(4);double limit,paid;if(!Double.TryParse(store.Setting("budget","0"),NumberStyles.Float,CultureInfo.InvariantCulture,out limit)||!Double.TryParse(store.Setting("subscriptionPaid","0"),NumberStyles.Float,CultureInfo.InvariantCulture,out paid))throw new FormatException("อ่านงบเดิมไม่ได้ · ข้อมูลเดิมยังอยู่");var item=new MonthlyBudgetItem{Id="legacy-"+month,Month=month,Name="ยอดรวมเดิมที่เคยกรอก",Currency=store.Setting("currency","THB"),Limit=limit,Paid=paid};Validate(item);legacy.Add(item);}Write(legacy);return legacy;
  }
  void Write(List<MonthlyBudgetItem> items){store.Set("budgets.v2",new JavaScriptSerializer().Serialize(items));}
  public List<MonthlyBudgetItem> Items(string month){lock(gate){if(month!=null)ValidateMonth(month);return Read().Where(x=>month==null||x.Month==month).OrderBy(x=>x.Currency).ThenBy(x=>x.Name).ToList();}}
  public List<MonthlyBudgetTotal> Totals(string month){return Items(month).GroupBy(x=>x.Currency).Select(g=>new MonthlyBudgetTotal{Currency=g.Key,Limit=g.Sum(x=>x.Limit),Paid=g.Sum(x=>x.Paid)}).ToList();}
  public void Save(MonthlyBudgetItem item){Validate(item);lock(gate){var items=Read();string id=String.IsNullOrEmpty(item.Id)?Guid.NewGuid().ToString("N"):item.Id;items.RemoveAll(x=>x.Id==id);if(items.Count>=5000)throw new FormatException("รายการงบถึงขีดจำกัด 5,000 รายการ");items.Add(new MonthlyBudgetItem{Id=id,Month=item.Month,Name=item.Name.Trim(),Currency=item.Currency,Limit=item.Limit,Paid=item.Paid});Write(items);}}
  public void Remove(string id){lock(gate){var items=Read();items.RemoveAll(x=>x.Id==id);Write(items);}}
 }
 public static class InsightExportPreview {
  public static string Describe(HistoryFilter filter,bool privacy,bool csv){string fields=csv?"tool, model, atUtc, granularity, requestEvents, input, output, cachedInput, cacheWrite, total, project, device, session, estimatedCost, currency":"key, tool, provider, account, device, model, project, session, at, granularity, requestEvents, input, output, cachedInput, cacheWrite, reasoning, total, estimatedCost, currency, priceVersion";string redacted=csv?"project, device, session, estimatedCost, currency":"key, account, device, project, session, estimatedCost, currency, priceVersion";return (csv?"CSV":"JSON")+" · ประวัติที่เก็บอยู่และตรงตัวกรองทั้งหมด (ไม่จำกัด 250 แถว)\n\nช่วงเวลา UTC: "+filter.From.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+" ถึงก่อน "+filter.To.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"\nตัวกรอง: tool="+(filter.Tool??"ทั้งหมด")+"; model="+(filter.Model??"ทั้งหมด")+"; project="+(filter.Project==null?"ทั้งหมด":"ระบุไว้")+"; device="+(filter.Device==null?"ทั้งหมด":"ระบุไว้")+"; account="+(filter.Account==null?"ทั้งหมด":"ระบุไว้")+"\n\nฟิลด์ในไฟล์: "+fields+(!csv?"\nส่วนหัว JSON: schemaVersion, exportedAt, privacy, costBasis":"")+"\n\n"+(privacy?"ซ่อนโดยส่งเป็น "+(csv?"ช่องว่าง":"null")+": "+redacted+"\nยังมีเครื่องมือ โมเดล เวลา และจำนวน token":"ไม่ได้ซ่อนรหัสโปรเจกต์ เครื่อง session และค่า API estimate"+(csv?"":" รวมถึง event key และขอบเขตบัญชีจาก local log"))+"\n\nไม่มีข้อความสนทนา credential หรืองบที่กรอกเอง · ค่า API เป็น estimate ไม่ใช่ยอดเรียกเก็บ\nข้อมูลเก่าบางส่วนเป็นยอดรวมตาม retention และไม่รวมข้อมูลนอก logs ที่อ่านได้";}
 }
}
