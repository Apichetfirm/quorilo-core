using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        Window welcomeWindow;
        int welcomeStep;
        bool welcomeTutorialOnly, welcomeBusy;
        StackPanel welcomeBody;
        TextBlock welcomeStatus;
        DispatcherTimer welcomeTimer;
        void OpenWelcome(bool tutorialOnly=false)
        {
            if(disposed||!windowLifecycle.CanShow)return;
            if(welcomeWindow!=null){welcomeWindow.Activate();return;}
            edgeDelay.Stop();Reveal();welcomeTutorialOnly=tutorialOnly;welcomeStep=tutorialOnly?3:Product.Commercial&&!(license!=null&&license.Allowed)?0:1;
            welcomeWindow=new Window{Title="เริ่มใช้ Quorilo Monitor",Width=610,Height=560,MinWidth=500,MinHeight=480,Owner=Window,ShowInTaskbar=false,FontFamily=Window.FontFamily,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            foreach(var dictionary in Window.Resources.MergedDictionaries)welcomeWindow.Resources.MergedDictionaries.Add(dictionary);
            ApplyPalette(welcomeWindow);welcomeWindow.SetResourceReference(Window.BackgroundProperty,"SettingsBackgroundBrush");
            var layout=new DockPanel{Margin=new Thickness(24)};
            var heading=new StackPanel();heading.Children.Add(Text("Quorilo Monitor",24,true));heading.Children.Add(Text("ตั้งค่าทีละขั้น · เลือกเฉพาะบริการที่คุณใช้",12));DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
            var navigation=new WrapPanel{Margin=new Thickness(0,16,0,0)};navigation.Children.Add(Button("ย้อนกลับ","ขั้นก่อนหน้า",()=>{if(!welcomeBusy&&welcomeStep>(welcomeTutorialOnly?3:Product.Commercial?0:1)){welcomeStep--;DrawWelcome();}}));
            navigation.Children.Add(Button("ถัดไป","ขั้นถัดไป",async()=>await WelcomeNext()));
            navigation.Children.Add(Button("ทำภายหลัง","ปิดหน้าตั้งค่าขั้นต้น",()=>welcomeWindow.Close()));DockPanel.SetDock(navigation,Dock.Bottom);layout.Children.Add(navigation);
            welcomeStatus=Text("",12);DockPanel.SetDock(welcomeStatus,Dock.Bottom);layout.Children.Add(welcomeStatus);
            welcomeBody=new StackPanel{Margin=new Thickness(0,20,0,0)};layout.Children.Add(new ScrollViewer{Content=welcomeBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});welcomeWindow.Content=layout;
            welcomeTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};welcomeTimer.Tick+=(s,e)=>{
                if(welcomeStep==2)UpdateWelcomeReadings();
            };welcomeTimer.Start();
            var closingTimer=welcomeTimer;var openedWindow=welcomeWindow;
            welcomeWindow.Closed+=(s,e)=>{closingTimer.Stop();if(Object.ReferenceEquals(welcomeWindow,openedWindow))welcomeWindow=null;if(!disposed&&windowLifecycle.CanShow){ShowMainWindow();ScheduleEdge();}};DrawWelcome();Window.Hide();welcomeWindow.Show();
        }
        void DrawWelcome()
        {
            welcomeBody.Children.Clear();welcomeReadings.Clear();welcomeStatus.Text="";
            string[] titles={"1 · ตรวจรหัสทดลอง","2 · เลือกบริการ","3 · เชื่อมบัญชี","4 · วิดเจ็ตอยู่ข้างงาน","5 · ซ่อนและจัดเรียง","6 · อ่านเครดิตและรีเซ็ต"};
            Heading(welcomeBody,Product.AccountLicensed&&welcomeStep==0?"1 · ลงชื่อเข้าใช้ Quorilo Monitor":titles[welcomeStep]);
            if(welcomeStep==0){
                if(Product.AccountLicensed){welcomeBody.Children.Add(Text("เปิดเว็บและลงชื่อเข้าใช้ด้วยช่องทางที่เปิดอยู่\nGoogle / GitHub / ChatGPT ไม่ต้องรับหรือพิมพ์คีย์\nกดเริ่ม Trial บนเว็บเพื่อเริ่ม 14 วัน หรือสมัคร Full\nชำระสำเร็จแล้วแอปเดิมเปลี่ยนเป็น Full อัตโนมัติ",13));welcomeBody.Children.Add(Button("ลงชื่อเข้าใช้ Quorilo Monitor","เปิดเว็บเพื่อยืนยันบัญชีและเครื่อง",async()=>{if(welcomeBusy||license==null)return;welcomeBusy=true;try{await license.SignInGoogleAsync(text=>{if(welcomeWindow!=null)welcomeStatus.Text=text;});if(welcomeWindow!=null&&Allowed){welcomeStep=1;DrawWelcome();}}catch(LicenseFailure e){if(welcomeWindow!=null)welcomeStatus.Text=LicenseClient.Message(e.Code);}finally{welcomeBusy=false;}}));welcomeBody.Children.Add(Text("ถ้าเคยยืนยันเครื่องแล้ว แอปจะตรวจสิทธิ์เดิมให้อัตโนมัติทุกนาที\nปิดหน้าต่างเตือนได้ แต่ยังต้องมีสิทธิ์ออนไลน์เพื่ออ่านเครดิต",12));Check(welcomeBody,"ไม่ต้องแสดงหน้าต่างนี้อัตโนมัติอีก",preferences.SuppressAccountLoginPrompt,value=>{preferences.SuppressAccountLoginPrompt=value;Save();});welcomeBody.Children.Add(Button("ตรวจสิทธิ์เดิมอีกครั้ง","ตรวจบัญชีที่บันทึกไว้",async()=>{if(welcomeBusy)return;welcomeBusy=true;try{await CheckLicense(true);if(Allowed){welcomeStep=1;DrawWelcome();}else if(welcomeWindow!=null)welcomeStatus.Text=LicenseClient.Message(license.LastError);}finally{welcomeBusy=false;}}));return;}
                welcomeBody.Children.Add(Text("ใส่รหัสเชิญที่ได้รับจากผู้จัดทดลอง\nเริ่มนับ 14 วันหลังได้รับเครดิตและเปิดใช้สำเร็จ",13));
                var key=new PasswordBox{MaxLength=160,Padding=new Thickness(12),Margin=new Thickness(0,16,0,12)};welcomeBody.Children.Add(key);
                welcomeBody.Children.Add(Button("ตรวจรหัสและไปต่อ","ตรวจรหัสกับระบบ",async()=>{
                    if(welcomeBusy||license==null)return;welcomeBusy=true;key.IsEnabled=false;welcomeStatus.Text="กำลังตรวจรหัส…";
                    try{await license.PrepareAsync(key.Password);key.Clear();welcomeStep=1;DrawWelcome();}
                    catch(LicenseFailure e){welcomeStatus.Text=LicenseClient.Message(e.Code);}catch{welcomeStatus.Text="ติดต่อระบบไม่ได้ · ลองใหม่";}
                    finally{welcomeBusy=false;key.IsEnabled=true;}
                }));return;
            }
            if(welcomeStep==1){
                welcomeBody.Children.Add(Text("ไม่ต้องมีครบสามบัญชี · เพิ่มบริการภายหลังได้ในตั้งค่า",13));
                foreach(var choice in preferences.Monitors){var selected=choice;Check(welcomeBody,HubNames.Label(choice.Id),choice.Visible,value=>{selected.Visible=value;selected.Paused=!value;Save();LayoutCards();});}
                welcomeBody.Children.Add(Text("ChatGPT บนเว็บกับ Codex มีแหล่งเครดิตต่างกัน\nPlus อย่างเดียวไม่ได้ทำให้ตัวอ่าน Codex รับค่าจากเว็บ",12));return;
            }
            if(welcomeStep==2){
                foreach(var choice in preferences.Monitors.Where(x=>x.Visible)){
                    Heading(welcomeBody,HubNames.Label(choice.Id));var id=choice.Id;
                    welcomeBody.Children.Add(Text(id=="ChatGPT"?"อ่านเครดิตจาก Codex ที่ลงชื่อเข้าใช้ในเครื่อง · Plus บนเว็บอย่างเดียวยังอ่านไม่ได้":id=="Claude"?"เชื่อม Claude Code แล้วเปิด session ใหม่เพื่อส่งข้อมูลเครดิต":"ลงชื่อเข้าใช้ Google ในหน้าต่างเชื่อมบัญชี · สำเร็จแล้วอ่านเบื้องหลัง",12));
                    welcomeBody.Children.Add(Button("เชื่อม / ดูสถานะ "+HubNames.Label(id),"เชื่อมบริการ",()=>OpenConnectionGuide(id)));var live=Text("",12);welcomeReadings[id]=live;welcomeBody.Children.Add(live);
                }
                UpdateWelcomeReadings();welcomeBody.Children.Add(Text(Product.AccountLicensed?"เชื่อมไม่สำเร็จบางบริการสามารถข้ามได้\nTrial เริ่มเมื่อคุณกดเริ่มบนเว็บ · การเชื่อมบริการไม่เริ่มเวลาใหม่":"เชื่อมไม่สำเร็จบางบริการสามารถข้ามได้\nสิทธิ์ทดลองจะเริ่มเมื่อมีเครดิตใหม่จากบริการที่รองรับ",12));return;
            }
            var illustration=new Border{Background=Brush(Theme.MetricSurface),CornerRadius=new CornerRadius(6),Padding=new Thickness(20),Margin=new Thickness(0,14,0,14)};
            var picture=new StackPanel();
            if(welcomeStep==5){
                var sample=new Metric{Remaining=72,ResetAt=DateTime.UtcNow.AddHours(2)};var example=BuildMetric(picture);
                example.Track.SizeChanged+=(s,e)=>PaintMetric(example,sample,"5h",true,TimeSpan.FromHours(5));PaintMetric(example,sample,"5h",true,TimeSpan.FromHours(5));
                picture.Children.Add(Text("เครดิตเหลือ 72% · เวลาผ่านไป 60% · เวลาเหลือ 40%",12));
            }else{picture.Children.Add(Text(welcomeStep==3?"Quorilo Monitor       ↗  —":"◀  Codex 82%  ·  Gemini Apps 64%",20,true));picture.Children.Add(Text(welcomeStep==3?"ปักหมุด → วิดเจ็ตอยู่บนหน้าจอ":"ลาก ↕ เพื่อจัดลำดับ · ปิดแสดงเพื่อซ่อนการ์ด",12));}
            illustration.Child=picture;welcomeBody.Children.Add(illustration);
            welcomeBody.Children.Add(Text(welcomeStep==3?"กดหมุดเพื่อแสดงตลอด หรือปิดหมุดให้วิดเจ็ตซ่อนริมจอ\nเลื่อนเมาส์ไปที่แท็บขอบจอเพื่อเปิดกลับ":welcomeStep==4?"ตั้งค่า → มอนิเตอร์ เลือกแสดงเฉพาะบริการที่ต้องการ\nซ่อนการ์ดยังอ่านต่อ · Pause หยุดอ่าน\nลากการ์ดหรือกด ↑ ↓ เพื่อจัดเรียง":"แถบสีและเปอร์เซ็นต์ = เครดิตที่เหลือ\nขีดสีอ่อน = เวลาที่ผ่านไปแล้วของรอบ\nชี้หรือใช้ Tab ไปที่แถบเพื่อดู % เวลาและเวลาที่เหลือก่อนรีเซ็ต\nค่าล่าสุดอยู่ต่อระหว่างรีเฟรช ดูเวลารับข้อมูลเพื่อทราบความสด\nเลือก 12H / 24H ในตั้งค่า → รูปแบบ",14));
            welcomeBody.Children.Add(Text("ภาพประกอบเป็นข้อมูลจำลอง",11));
        }
        async Task WelcomeNext()
        {
            if(welcomeBusy)return;
            if(welcomeStep==0){if(Product.AccountLicensed&&Allowed){welcomeStep=1;DrawWelcome();return;}welcomeStatus.Text=Product.AccountLicensed?"ลงชื่อเข้าใช้ Quorilo Monitor และยืนยันเครื่องก่อน":"ตรวจรหัสก่อนเพื่อไปต่อ";return;}
            if(welcomeStep==1&&!preferences.Monitors.Any(x=>x.Visible)){welcomeStatus.Text="เลือกอย่างน้อยหนึ่งบริการ";return;}
            if(welcomeStep==2&&!Product.AccountLicensed&&Product.Commercial&&license!=null&&!license.Allowed){if(!HasFresh()){welcomeStatus.Text="ยังไม่มีเครดิตใหม่ · เชื่อมอย่างน้อยหนึ่งบริการก่อน หรือทำภายหลัง";return;}welcomeBusy=true;try{await license.ReactivateAsync(true);readers.Broadcast("LicenseReload");}catch(LicenseFailure e){welcomeStatus.Text=LicenseClient.Message(e.Code);return;}finally{welcomeBusy=false;}}
            if(welcomeStep>=5){preferences.OnboardingViewed=true;Save();welcomeWindow.Close();return;}welcomeStep++;DrawWelcome();
        }
    }
}

