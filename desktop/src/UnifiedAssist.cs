using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;

namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        CompanionClient companion;
        CompanionClient Companion { get { return companion ?? (companion = new CompanionClient()); } }
        static string CompanionVaultPath { get { return Path.Combine(Product.Storage, "companion.dat"); } }
        DateTime companionAt=DateTime.MinValue;
        TextBlock companionStatus;
        void BuildAssist(Panel page)
        {
            Heading(page,"Prompt Assist · ทดลอง");page.Children.Add(Text("ประเมินความยาวและตัดช่องว่างหัวท้ายในเครื่อง\nไม่ได้ส่ง prompt ให้ผู้ให้บริการ และไม่รับประกัน % เครดิตที่จะใช้",12));
            var input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=160,MaxLength=60000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,12,0,12)};page.Children.Add(input);
            var summary=Text("",12);page.Children.Add(summary);var output=new TextBox{AcceptsReturn=true,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            page.Children.Add(Button("ประเมิน / จัดรูปแบบ","ประเมิน prompt ในเครื่อง",()=>{var original=input.Text;var candidate=original.Trim();output.Text=candidate;summary.Text=original.Length+" ตัวอักษร → "+candidate.Length+" ตัวอักษร\ntoken โดยประมาณจาก bytes/4: "+Math.Ceiling(System.Text.Encoding.UTF8.GetByteCount(original)/4.0)+" · อาจคลาดเคลื่อนมาก\nเครดิตจริงขึ้นกับโมเดล บริบท และคำตอบ ตรวจฉบับจัดรูปแบบก่อนนำไปใช้";}));page.Children.Add(output);page.Children.Add(Button("คัดลอกฉบับจัดรูปแบบ","คัดลอกข้อความ",()=>{if(!String.IsNullOrEmpty(output.Text))Clipboard.SetText(output.Text);}));
        }
        void BuildCompanion(Panel page)
        {
            if(Product.FreeCore){Heading(page,"บริการออนไลน์เสริม");page.Children.Add(Text("Free Core ใช้มอนิเตอร์ในเครื่องได้โดยไม่ต้องมีบัญชี Quorilo\nCloud Sync และบริการบัญชีแยกต่างหากยังไม่เปิดในรุ่นนี้",12));return;}
            Heading(page,"ดูเครดิตบนมือถือ · Preview");page.Children.Add(Text("จับคู่แล้วส่งชื่อบริการ ชื่อบัญชีที่ตั้งเอง เปอร์เซ็นต์ และเวลารีเซ็ต\nคอมต้องเปิดเพื่อซิงค์ · ไม่มีรหัสผ่านหรือ session ของ AI ในข้อมูลที่ส่ง",12));
            page.Children.Add(Button("เปิดหน้าดูเครดิตมือถือ","เปิดหน้าเว็บเมื่อคุณกด",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=AccountTrialClient.Origin+"/companion/",UseShellExecute=true})));var endpoint=new TextBox{Text=CompanionClient.Gateway};page.Children.Add(Text("บนหน้าเว็บ กดสร้างรหัสจับคู่ → คัดลอก → วางด้านล่าง",12));page.Children.Add(Text("รหัสจับคู่จากหน้ามือถือ",12));var code=new PasswordBox{MaxLength=48,Margin=new Thickness(0,5,0,12)};page.Children.Add(code);
            companionStatus=Text(Companion.Status??(Companion.Paired?"จับคู่แล้ว":"บริการออนไลน์ต้องตั้งค่าก่อน · ยังไม่ส่งข้อมูล"),12);page.Children.Add(companionStatus);
            Button pair=null;pair=Button("จับคู่และเปิดซิงค์","เปิดการส่งข้อมูลโดยสมัครใจ",async()=>{if(!Allowed){companionStatus.Text="เปิดสิทธิ์ใช้งานในโปรแกรมก่อนจับคู่มือถือ";return;}pair.IsEnabled=false;try{await Companion.Pair(endpoint.Text,code.Password.Trim());code.Clear();companionAt=DateTime.MinValue;companionStatus.Text=Companion.Status;SyncCompanion();}catch(Exception e){companionStatus.Text=e is InvalidOperationException?e.Message:"จับคู่ไม่ได้ · ตรวจอินเทอร์เน็ตแล้วลองใหม่";}finally{pair.IsEnabled=true;}});page.Children.Add(pair);page.Children.Add(Button("หยุดซิงค์เครื่องนี้","หยุดส่งข้อมูลมือถือ",()=>{Companion.Disconnect();companionStatus.Text=Companion.Status;}));
        }
        // The API accepts safe integer Unix milliseconds. DateTime has sub-ms precision.
        static long Milliseconds(DateTime date){return (date.ToUniversalTime().Ticks-621355968000000000L)/TimeSpan.TicksPerMillisecond;}
        object MobileMetric(HubMetric raw){var metric=raw==null?null:raw.ToMetric();return metric==null||(metric.ResetAt.HasValue&&metric.ResetAt.Value<=DateTime.UtcNow)?null:new{remaining=metric.Remaining,resetAt=metric.ResetAt.HasValue?(long?)Milliseconds(metric.ResetAt.Value):null};}
        async void SyncCompanion()
        {
            if(Product.FreeCore)return;
            if(companion==null&&!File.Exists(CompanionVaultPath))return;
            if(!Companion.Paired||!Allowed||DateTime.UtcNow<companionAt)return;companionAt=DateTime.UtcNow.AddSeconds(30);
            object payload;
            try{payload=MobileSnapshotPacking.Build(preferences.Monitors.Where(x=>AccountAllowed(x.Id)&&snapshots.ContainsKey(x.Id)&&snapshots[x.Id]!=null&&snapshots[x.Id].HeartbeatCurrent).Select(x=>new MobileSnapshotEntry{Choice=x,Data=snapshots[x.Id]}),DateTime.UtcNow);}
            catch{if(companionStatus!=null)companionStatus.Text="ข้อมูลซิงค์เกินขนาดที่รองรับ · ตรวจจำนวนบัญชีและชื่อที่ตั้ง";return;}
            await Companion.Upload(payload);if(companionStatus!=null)companionStatus.Text=Companion.Status;
            if(Allowed&&insights!=null)await Companion.UploadHistory(insights);
        }
    }
}
