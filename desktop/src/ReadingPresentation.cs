using System;
using System.Globalization;

namespace CreditMonitor
{
    internal sealed class ReadingState
    {
        public string Summary,Detail,Action;
        public bool Verified;
    }
    // Presentation only. Never authorizes a license or changes a provider reading.
    internal static class ReadingPresentation
    {
        static bool Recent(string timestamp,DateTime now,double seconds)
        {
            DateTime time;
            return DateTime.TryParse(timestamp,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out time)
                &&(now-time).TotalSeconds>=-5&&(now-time).TotalSeconds<=seconds;
        }
        static bool Usable(HubMetric raw,DateTime now)
        {
            var metric=raw==null?null:raw.ToMetric();
            return metric!=null&&(!metric.ResetAt.HasValue||metric.ResetAt.Value>now);
        }
        public static ReadingState Describe(string id,HubSnapshot data,bool paused,bool allowed,bool refreshing,DateTime now)
        {
            if(!allowed)return State("ตรวจสิทธิ์ก่อนอ่านเครดิต","เปิดหน้าสิทธิ์ใช้งานเพื่อลงชื่อเข้าใช้หรือตรวจสิทธิ์เดิม","rights");
            if(paused||(data!=null&&data.paused))return State("พักการอ่าน · แสดงค่าครั้งล่าสุด","กดทำงานต่อเพื่อรับค่าใหม่ ค่าที่ค้างอยู่ยังไม่ยืนยันสถานะล่าสุด","resume");
            if(data==null)return State("กำลังรอข้อมูลจากตัวอ่าน","ยังยืนยันการเชื่อมต่อไม่ได้ เปิดวิธีเชื่อมเพื่อตรวจทีละขั้น","connect");
            if(data.state=="LicenseRequired")return State("ต้องตรวจสิทธิ์ใช้งาน","ตรวจสิทธิ์ Quorilo Monitor ก่อนอ่านเครดิต","rights");
            if(data.state=="LoginRequired")return State("ต้องเชื่อมบัญชีใหม่","เปิดหน้าต่างเชื่อมบัญชีของบริการนี้ แล้วกลับมาตรวจว่ารับเครดิตได้","connect");
            if(data.state=="MissingCli")return State("ยังไม่พบ Codex ในเครื่อง","ติดตั้ง Codex และลงชื่อเข้าใช้บัญชีที่ต้องการ ก่อนกดตรวจอีกครั้ง","connect");
            if(data.state=="Waiting"&&id=="Claude")return State("รอข้อมูลจาก Claude Code","เชื่อม Claude Code แล้วเปิด session ใหม่ให้ส่งข้อมูล statusLine จากนั้นกดตรวจอีกครั้ง","connect");
            if((data.capability=="UsageReport"||data.capability=="SpendBudget")&&data.state=="Live"&&data.Received.HasValue&&data.Received.Value<=now.AddSeconds(5)&&Recent(data.updatedAt,now,25)&&!String.IsNullOrWhiteSpace(data.reportSummary)){
                bool freshReport=Recent(data.lastReceived,now,90);var report=State((freshReport?"อ่านรายงานแล้ว · ":"รายงานที่บันทึกไว้ · ")+Received(data,now),data.reportSummary+"\n"+(data.capability=="SpendBudget"?"งบ on-demand แยกจาก included quota · ไม่คาดเดาเวลารีเซ็ต":"จำนวนใช้จริงจาก API · ไม่มี allowance สำหรับคำนวณ % เครดิตคงเหลือ")+"\n"+(id=="Cursor"?"ตรวจรายงานตามรอบทุก 1 ชั่วโมง":"ตรวจรายงานตามรอบทุก 5 นาที"),"refresh");report.Verified=freshReport;return report;
            }
            bool one=Usable(data.primary,now),two=Usable(data.secondary,now);
            bool fresh=(data.state=="Live"||data.state=="Partial")&&Recent(data.lastReceived,now,90)&&Recent(data.updatedAt,now,25)&&(one||two);
            if(fresh){
                var result=State((one&&two&&data.state!="Partial"?"รับเครดิตแล้ว":"รับเครดิตแล้วบางส่วน")+" · "+Received(data,now)+(refreshing?" · กำลังตรวจใหม่":""),
                    (one&&two?"อ่านโควตาทั้งสองรอบได้":"บางรอบยังไม่มีข้อมูล จะแสดง — โดยไม่สร้างเปอร์เซ็นต์ขึ้นเอง")+"\nรับล่าสุด "+Received(data,now),"refresh");
                result.Verified=true;return result;
            }
            if(data.state=="Error")return State("อ่านค่าใหม่ไม่สำเร็จ","ตรวจอินเทอร์เน็ตแล้วกดตรวจอีกครั้ง หากยังไม่สำเร็จให้เชื่อมบัญชีใหม่\nค่าที่แสดงอาจเป็นค่าครั้งล่าสุด · รับล่าสุด "+Received(data,now),"refresh");
            if(one||two)return State("ข้อมูลเก่า · "+Received(data,now),"ยังยืนยันค่าปัจจุบันไม่ได้ กดตรวจอีกครั้ง หรือเปิดวิธีเชื่อมหากยังรอข้อมูล","refresh");
            return State("ยังไม่มีเครดิตที่ยืนยันได้","ยังไม่มีค่าที่ถูกต้องหรือรอบเดิมสิ้นสุดแล้ว เปิดวิธีเชื่อมและตรวจอีกครั้ง","connect");
        }
        static ReadingState State(string summary,string detail,string action){return new ReadingState{Summary=summary,Detail=detail,Action=action};}
        public static string Received(HubSnapshot data,DateTime now)
        {
            if(data==null||!data.Received.HasValue)return "ยังไม่มีเวลาอ่านค่า";
            double seconds=(now-data.Received.Value).TotalSeconds;
            if(seconds < -5)return "เวลาอ่านค่าอยู่ในอนาคต · ตรวจนาฬิกาเครื่อง";
            seconds=Math.Max(0,seconds);
            return seconds<60?Math.Floor(seconds)+" วินาทีก่อน":seconds<3600?Math.Floor(seconds/60)+" นาทีก่อน":Math.Floor(seconds/3600)+" ชั่วโมงก่อน";
        }
        public static string MetricHelp(Metric metric,TimeSpan duration,DateTime now)
        {
            string result=metric==null?"ยังไม่มีเครดิตที่ยืนยันได้":"แถบสี = เครดิตเหลือ "+metric.Display;
            var ratio=QuotaFormatting.ElapsedRatio(metric,duration,now);
            if(!ratio.HasValue)return result+"\nขีดเวลาซ่อนเมื่อไม่มีเวลารีเซ็ตที่อยู่ในรอบมาตรฐาน";
            var remaining=metric.ResetAt.Value.ToUniversalTime()-now.ToUniversalTime();
            string time=remaining.TotalDays>=1?(int)remaining.TotalDays+" วัน "+remaining.Hours+" ชม.":remaining.TotalHours>=1?(int)remaining.TotalHours+" ชม. "+remaining.Minutes+" นาที":Math.Max(1,(int)Math.Ceiling(remaining.TotalMinutes))+" นาที";
            return result+"\nขีดสีอ่อน = เวลาผ่านไป "+(ratio.Value*100).ToString("0",CultureInfo.InvariantCulture)+"% ของรอบ\nเวลาเหลือ "+((1-ratio.Value)*100).ToString("0",CultureInfo.InvariantCulture)+"% · รีเซ็ตใน "+time;
        }
        public static string Instructions(string id,bool cli)
        {
            if(id=="ChatGPT")return "1. ติดตั้ง Codex และลงชื่อเข้าใช้บัญชีที่ต้องการในเครื่องนี้\n2. กดเปิดการเชื่อมต่อ แล้วทำตามหน้าต่าง Codex\n3. กลับมากดตรวจอีกครั้ง รอจนขึ้นรับเครดิตแล้ว\n\nเครดิตนี้เป็นของ Codex บัญชี ChatGPT บนเว็บอย่างเดียวอาจยังอ่านไม่ได้";
            if(id=="Claude")return "1. ติดตั้งและลงชื่อเข้าใช้ Claude Code\n2. กดเปิดการเชื่อมต่อเพื่อตั้งค่า statusLine ตามหน้าต่างที่แสดง\n3. เปิด session ใหม่ใน Claude Code เพื่อให้ส่งข้อมูลเครดิต แล้วกดตรวจอีกครั้ง\n\nหากยังรอข้อมูล ให้ทำงานใน session ใหม่ก่อน การเปิดหน้าต่างเชื่อมอย่างเดียวยังไม่ยืนยันว่าอ่านเครดิตได้";
            return cli?"1. ลงชื่อเข้าใช้ Gemini CLI ในเครื่องนี้\n2. เปิดการเชื่อมต่อและเลือกแหล่ง Gemini CLI\n3. กลับมาตรวจอีกครั้ง\n\nโควตา CLI แยกจาก Gemini Apps":"1. กดเปิดการเชื่อมต่อเพื่อเปิดหน้าต่าง Google ของแอป\n2. ลงชื่อเข้าใช้ด้วยตัวเอง แล้วรอหน้า Usage\n3. กลับมาตรวจอีกครั้งจนขึ้นรับเครดิตแล้ว\n\nหาก Google ขอ passkey ให้เลือกด้วยตัวเอง โควตา Gemini Apps แยกจาก Gemini CLI";
        }
    }
}
