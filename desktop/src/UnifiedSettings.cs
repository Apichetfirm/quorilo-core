using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        StackPanel monitorSettings;
#if !TRIAL
        StackPanel quotaHistoryPage;int quotaHistoryTabIndex=-1;
#endif
        string monitorGroupFilter="";
        bool updateBusy;
        System.Threading.CancellationTokenSource updateCancellation;
        TextBlock updateStatus;
        Button cancelUpdate;
        readonly Dictionary<string,TextBlock> monitorSettingNames=new Dictionary<string,TextBlock>();
        readonly Dictionary<string,TextBlock> monitorSettingStatus=new Dictionary<string,TextBlock>();
        void OpenSettings()
        {
            if(disposed||!windowLifecycle.CanShow)return;
            edgeDelay.Stop();Reveal();if(settingsWindow!=null){settingsWindow.Activate();return;}
            settingsWindow=new Window{Title="ตั้งค่า · "+Product.Name,Width=720,Height=730,MinWidth=530,MinHeight=450,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,FontFamily=Window.FontFamily,FontSize=13,Topmost=preferences.Topmost};
            settingsWindow.Resources.MergedDictionaries.Add((ResourceDictionary)CompiledViews.Load("SettingsStyles.xaml"));ApplyPalette(settingsWindow);settingsWindow.SetResourceReference(Window.BackgroundProperty,"SettingsBackgroundBrush");
            var rootPanel=new DockPanel{Margin=new Thickness(20)};var header=new StackPanel{Margin=new Thickness(0,0,0,15)};header.Children.Add(Text("Quorilo Monitor",23,true));header.Children.Add(Text("เลือกบริการและจัดลำดับการแสดง",12));DockPanel.SetDock(header,Dock.Top);rootPanel.Children.Add(header);
            var footerPanel=new DockPanel{Margin=new Thickness(0,14,0,0)};var done=Button("เสร็จแล้ว","ปิดตั้งค่า",()=>settingsWindow.Close());DockPanel.SetDock(done,Dock.Right);footerPanel.Children.Add(done);footerPanel.Children.Add(Text("บันทึกอัตโนมัติ · "+Product.Version+" · "+(Product.FreeCore?"Free Core":Product.Commercial?"Beta":"Owner"),11));DockPanel.SetDock(footerPanel,Dock.Bottom);rootPanel.Children.Add(footerPanel);
            var tabs=new TabControl();rootPanel.Children.Add(tabs);monitorSettings=Page(tabs,"มอนิเตอร์");RebuildMonitorSettings();
            var appearance=Page(tabs,"รูปแบบ");Heading(appearance,"ธีมและการแสดงผล");
            AddThemeChoices(appearance);
            var sizes=new WrapPanel{Margin=new Thickness(0,5,0,12)};sizes.Children.Add(Button("แถบกะทัดรัด","ใช้แถบกะทัดรัด",()=>{preferences.Compact=false;ToggleCompact();}));sizes.Children.Add(Button("การ์ดปกติ","ใช้การ์ดปกติ",()=>{preferences.Compact=true;ToggleCompact();}));appearance.Children.Add(sizes);
            appearance.Children.Add(Text("เปิดและจัดการแอปจากไอคอน Quorilo Monitor ใน system tray",12));
            Check(appearance,"อยู่เหนือหน้าต่างอื่น",preferences.Topmost,value=>{preferences.Topmost=value;Window.Topmost=value;settingsWindow.Topmost=value;if(edgeWindow!=null)edgeWindow.Topmost=value;});
            Check(appearance,"เปิดพร้อม Windows",Lifecycle.StartupEnabled,value=>{try{Lifecycle.SetStartup(value,typeof(UnifiedMonitor).Assembly.Location);}catch{MessageBox.Show(settingsWindow,"ตั้งค่าเปิดพร้อม Windows ไม่สำเร็จ",Product.Name);}});
            var awakeStatus=Text(awakeGuard.Enabled?"กำลังป้องกัน Sleep อัตโนมัติ · จอยังดับได้ตามปกติ":"ไม่ได้ป้องกัน Sleep อัตโนมัติ",11);
            CheckBox awakeChoice=null;
            awakeChoice=Check(appearance,"Keep this program awake (Prevent sleep)",preferences.KeepAwake,value=>{
                if(awakeGuard.SetEnabled(value))preferences.KeepAwake=value;
                else{awakeChoice.IsChecked=preferences.KeepAwake;MessageBox.Show(settingsWindow,"Windows ไม่รับคำขอป้องกัน Sleep · ลองใหม่อีกครั้ง",Product.Name);}
                awakeStatus.Text=awakeGuard.Enabled?"กำลังป้องกัน Sleep อัตโนมัติ · จอยังดับได้ตามปกติ":"ไม่ได้ป้องกัน Sleep อัตโนมัติ";
            });
            appearance.Children.Add(awakeStatus);
            appearance.Children.Add(Text("ทำงานต่อเมื่อซ่อนใน tray · กด Sleep เองได้ · ออกจากโปรแกรมแล้วคืนค่าตามปกติ",11));
            Check(appearance,"ไฟสถานะหรี่–สว่างเมื่อข้อมูลยังสด",preferences.AnimateStatus,value=>preferences.AnimateStatus=value);
#if !TRIAL
            if(FullFeatures)Check(appearance,"แสดง Burn rate · % เครดิตที่ใช้ต่อนาที",preferences.ShowBurnRate&&FullFeatures,value=>{preferences.ShowBurnRate=value&&FullFeatures;foreach(var v in views.Values){v.Primary.Rate.Clear();v.Secondary.Rate.Clear();}LayoutCards();});
            appearance.Children.Add(Text("เฉลี่ยย้อนหลังสูงสุด 5 นาที · เริ่มคำนวณหลัง 15 วินาที\n%/นาที = จุดเปอร์เซ็นต์ที่ใช้ต่อนาที ไม่ใช่ยอด token หรือเงิน",11));
#endif
            Heading(appearance,"เวลารีเซ็ต");var format=new ComboBox{ItemsSource=new[]{"24 ชั่วโมง · 18:30","12 ชั่วโมง · 6:30 PM"},SelectedIndex=preferences.TimeFormat=="12H"?1:0,Margin=new Thickness(0,0,0,12)};format.SelectionChanged+=(s,e)=>{preferences.TimeFormat=format.SelectedIndex==1?"12H":"24H";Save();};appearance.Children.Add(format);
            Heading(appearance,"ซ่อนริมจอ");Check(appearance,"ปักหมุด · ไม่ซ่อนอัตโนมัติ",preferences.Pinned,value=>{preferences.Pinned=value;UpdatePin();if(value)Reveal();});Check(appearance,"แสดง % 5h ของบริการที่เชื่อมต่อบนแท็บริมจอ",preferences.EdgeShowPercent,value=>{preferences.EdgeShowPercent=value;UpdateEdge();});Check(appearance,"เลื่อนเปิด / ซ่อนพร้อม Motion Blur",preferences.Motion,value=>preferences.Motion=value);
            var edge=new ComboBox{ItemsSource=new[]{"ขอบที่ใกล้ที่สุด","ซ้าย","ขวา"},SelectedIndex=preferences.EdgeSide=="Left"?1:preferences.EdgeSide=="Right"?2:0,Margin=new Thickness(0,0,0,12)};edge.SelectionChanged+=(s,e)=>{preferences.EdgeSide=edge.SelectedIndex==1?"Left":edge.SelectedIndex==2?"Right":"Nearest";Save();};appearance.Children.Add(edge);
            Heading(appearance,"รอบอ่าน Gemini Apps");var interval=new ComboBox{ItemsSource=new[]{"15 วินาที","30 วินาที","60 วินาที"},SelectedIndex=preferences.GeminiRefreshSeconds==30?1:preferences.GeminiRefreshSeconds==60?2:0,Margin=new Thickness(0,0,0,12)};interval.SelectionChanged+=(s,e)=>{preferences.GeminiRefreshSeconds=interval.SelectedIndex==1?30:interval.SelectedIndex==2?60:15;readers.Send("Gemini","Gemini"+preferences.GeminiRefreshSeconds);Save();};appearance.Children.Add(interval);appearance.Children.Add(Text("ย่อหรือซ่อนการ์ดแล้วยังอ่านต่อ · เมื่อเชื่อมต่อผิดพลาด Gemini Apps จะลดความถี่การลองใหม่เอง",12));
            var alerts=Page(tabs,"แจ้งเตือน");Heading(alerts,"แจ้งเตือนเครดิตต่ำ");Check(alerts,"เปิดแจ้งเตือนแบบรวม",preferences.AlertsEnabled,value=>preferences.AlertsEnabled=value);alerts.Children.Add(Text("แจ้งชื่อบริการและชื่อบัญชี · ไม่เตือนจากข้อมูลเก่า เลือกเปิดเตือนแต่ละบริการในหน้ามอนิเตอร์",12));
            var threshold=Text("เตือนเมื่อเหลือ "+preferences.AlertThreshold+"%",15,true);alerts.Children.Add(threshold);var slider=new Slider{Minimum=1,Maximum=50,Value=preferences.AlertThreshold,IsSnapToTickEnabled=true,TickFrequency=1,Margin=new Thickness(0,10,0,16)};slider.ValueChanged+=(s,e)=>{preferences.AlertThreshold=(int)slider.Value;threshold.Text="เตือนเมื่อเหลือ "+preferences.AlertThreshold+"%";Save();};alerts.Children.Add(slider);
            Check(alerts,"แจ้งเมื่อผู้ให้บริการยืนยันรอบรีเซ็ตใหม่",preferences.ResetAlertsEnabled,value=>preferences.ResetAlertsEnabled=value);
            Check(alerts,"งดแจ้งเตือน 22:00–08:00",preferences.QuietHours,value=>preferences.QuietHours=value);var snooze=new WrapPanel{Margin=new Thickness(0,8,0,0)};snooze.Children.Add(Button("พัก 1 ชั่วโมง","พักแจ้งเตือนหนึ่งชั่วโมง",()=>{preferences.SnoozeUntil=DateTime.Now.AddHours(1);Save();}));snooze.Children.Add(Button("ทำงานต่อ","ยกเลิกพักแจ้งเตือน",()=>{preferences.SnoozeUntil=null;Save();}));alerts.Children.Add(snooze);
#if !TRIAL
            if(FullFeatures){quotaHistoryTabIndex=tabs.Items.Count;quotaHistoryPage=Page(tabs,"ประวัติโควตา");BuildQuotaHistoryPage(quotaHistoryPage);tabs.SelectionChanged+=(s,e)=>{if(tabs.SelectedIndex==quotaHistoryTabIndex&&quotaHistoryPage!=null)BuildQuotaHistoryPage(quotaHistoryPage);};}
#endif
            BuildAssist(Page(tabs,"ผู้ช่วย"));BuildCompanion(Page(tabs,"มือถือ"));
            var rights=Page(tabs,Product.FreeCore?"Free Core":"สิทธิ์ใช้งาน");BuildLicensePage(rights);
            var about=Page(tabs,"เกี่ยวกับ");Heading(about,"Quorilo Monitor");about.Children.Add(Text("ผู้ผลิต · Firm Impulse",13));
            about.Children.Add(Text("ดูเครดิต AI หลายบริการในหน้าต่างเดียว\nWindows Preview · "+(MultipleAccounts?"หลายบัญชีต่อบริการ · โปรไฟล์แยกกัน":"หนึ่งบัญชีต่อบริการ · เพิ่มหลายบัญชีได้ด้วย Monitor Pro"),13));
            about.Children.Add(Button("แนะนำวิธีใช้ในแอป","ดูภาพแนะนำ",()=>OpenWelcome(true)));
            Heading(about,"บริการที่รองรับ");
            AboutService(about,"Codex","ดูเครดิตสำหรับงาน Codex ของบัญชี ChatGPT\nยังไม่ครอบคลุมการแชตทั่วไปบนเว็บ ChatGPT");
            AboutService(about,"Claude Code","ดูเครดิตที่ Claude Code รายงานจากบัญชีของคุณ\nแอป Claude Desktop อย่างเดียวใช้เป็นแหล่งข้อมูลไม่ได้");
            AboutService(about,"Gemini Apps","ดูเครดิต 5 ชั่วโมงและรายสัปดาห์จากหน้าการใช้งาน Gemini\nGemini CLI เป็นช่องทางเพิ่มเติม และมีเครดิตแยกกัน");
            if(!Product.FreeCore){Heading(about,"ดูบนมือถือ");
            about.Children.Add(Text("จับคู่กับคอมพิวเตอร์เพื่อดูเครดิตผ่านเว็บบนมือถือ\nAndroid มีรุ่นทดลองแจ้งเตือน · iPhone ดูผ่านเว็บ",13));
            }
            about.Children.Add(Button("ส่ง feedback","เปิดฟอร์ม feedback",()=>OpenLink("https://docs.google.com/forms/d/e/1FAIpQLSeQaK_qCX-cUKuuz-myn_F_kfHAEjs2ExTzTyAp3O1e0KOeIg/viewform")));
            Heading(about,"การติดตั้งและอัปเดต");about.Children.Add(Text(PublisherTrust.CurrentDescription(),12));
            about.Children.Add(Text(Product.FreeCore?"Free Core ไม่มีการหมดอายุหรือการตรวจสิทธิ์ชำระเงิน\nรุ่น source build นี้ไม่ดาวน์โหลดหรือติดตั้งอัปเดตอัตโนมัติ การตรวจไฟล์และลายเซ็นผู้เผยแพร่ยังคงบังคับสำหรับเส้นทางอัปเดตที่รองรับ":"ตรวจสิทธิ์ ไฟล์อัปเดต และผู้ลงนามก่อนเปิดโฟลเดอร์ให้ติดตั้ง · ไม่เริ่มติดตั้งอัตโนมัติ\nตัวติดตั้งเก็บโปรแกรมรุ่นก่อนหน้าไว้สำหรับย้อนกลับ โดยไม่แตะข้อมูลบัญชี",12));
            updateStatus=Text("",12);about.Children.Add(updateStatus);cancelUpdate=Button("ยกเลิกดาวน์โหลด","ยกเลิกและลบไฟล์ที่ยังไม่ครบ",()=>{if(updateCancellation!=null)updateCancellation.Cancel();});cancelUpdate.Visibility=Visibility.Collapsed;about.Children.Add(cancelUpdate);
            if(Product.Commercial&&license!=null&&license.Grant!=null&&(license.Grant.kind=="paid"||license.Grant.kind=="subscription"))about.Children.Add(Button("ตรวจอัปเดต","ตรวจและดาวน์โหลดอัปเดตที่ผ่านการยืนยัน",CheckForUpdate));
            settingsWindow.Content=rootPanel;settingsWindow.StateChanged+=(s,e)=>{if(settingsWindow!=null&&settingsWindow.WindowState==WindowState.Minimized)settingsWindow.Close();};var openedSettings=settingsWindow;settingsWindow.Closed+=(s,e)=>{if(Object.ReferenceEquals(settingsWindow,openedSettings)){if(updateCancellation!=null)updateCancellation.Cancel();updateStatus=null;cancelUpdate=null;settingsWindow=null;monitorSettings=null;monitorSettingStatus.Clear();monitorSettingNames.Clear();licenseStatus=null;licenseDevicePanel=null;}if(!disposed&&windowLifecycle.CanShow){Save();ScheduleEdge();}};settingsWindow.Show();UpdateLicenseText();
        }
        async void CheckForUpdate()
        {
            if(updateBusy||license==null)return;updateBusy=true;
            var owner=settingsWindow;var cancellation=new System.Threading.CancellationTokenSource();updateCancellation=cancellation;
            try {
                if(updateStatus!=null)updateStatus.Text="กำลังตรวจสิทธิ์และรุ่นอัปเดต…";
                var manifest=await license.CheckUpdateAsync();
                if(cancellation.IsCancellationRequested||disposed||owner==null||owner!=settingsWindow)return;
                if(MessageBox.Show(owner,"รุ่นปัจจุบัน "+Product.Version+" → "+manifest.version+"\nขนาด "+Math.Ceiling(manifest.size/1024.0/1024.0)+" MB · ต้องผ่านการตรวจไฟล์และลายเซ็นผู้เผยแพร่\nดาวน์โหลดไฟล์ติดตั้งหรือไม่?",Product.Name,MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
                if(updateStatus!=null)updateStatus.Text="กำลังดาวน์โหลดและตรวจความถูกต้อง…";if(cancelUpdate!=null)cancelUpdate.Visibility=Visibility.Visible;
                var file=await VerifiedUpdate.DownloadAsync(manifest,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Product.StorageName,"VerifiedUpdates"),cancellation.Token);
                if(cancellation.IsCancellationRequested||disposed||owner!=settingsWindow)return;
                if(!license.Allowed)throw new LicenseFailure("PurchaseRequired");
                if(updateStatus!=null)updateStatus.Text="ตรวจไฟล์และลายเซ็นผู้เผยแพร่แล้ว · พร้อมให้คุณติดตั้ง";
                MessageBox.Show(owner,"ตรวจไฟล์และลายเซ็นแล้ว · ปิด Quorilo Monitor ก่อนเปิดตัวติดตั้ง\nหากรุ่นใหม่มีปัญหา เปิดตัวติดตั้งนี้อีกครั้งแล้วเลือกย้อนกลับรุ่นก่อนหน้า",Product.Name);
                if(cancellation.IsCancellationRequested||disposed||owner!=settingsWindow)return;
                System.Diagnostics.Process.Start("explorer.exe","/select,\""+file+"\"");
            } catch(OperationCanceledException){if(updateStatus!=null&&owner==settingsWindow)updateStatus.Text="ยกเลิกแล้ว · โปรแกรมรุ่นเดิมยังใช้งานได้";}
            catch(LicenseFailure error) {if(owner==settingsWindow&&updateStatus!=null)updateStatus.Text=error.Code=="UpdatePublisherUnavailable"?"รุ่นนี้ยังไม่มีลายเซ็นผู้เผยแพร่ที่เชื่อถือได้ · ระบบจึงยังไม่เปิดไฟล์อัปเดต":error.Code=="InvalidUpdatePublisher"?"ลายเซ็นไฟล์ไม่ผ่านหรือผู้ลงนามไม่ตรงกับโปรแกรมเดิม · ไม่เปิดไฟล์":LicenseClient.Message(error.Code);}
            catch {if(owner==settingsWindow&&updateStatus!=null)updateStatus.Text="ติดต่อระบบอัปเดตไม่ได้ · ตรวจอินเทอร์เน็ตแล้วลองอีกครั้ง";}
            finally {if(cancelUpdate!=null&&owner==settingsWindow)cancelUpdate.Visibility=Visibility.Collapsed;if(Object.ReferenceEquals(updateCancellation,cancellation))updateCancellation=null;cancellation.Dispose();updateBusy=false;}
        }
#if !TRIAL
        void BuildQuotaHistoryPage(StackPanel page)
        {
            if(page==null)return;
            bool available=EnsureQuotaHistory();page.Children.Clear();
            if(!FullFeatures){page.Children.Add(Text("ต้องมีสิทธิ์ Full เพื่อดูประวัติของบัญชีนี้",12));return;}
            Heading(page,"ประวัติโควตาในเครื่อง");
            page.Children.Add(Text("กราฟใช้เฉพาะเปอร์เซ็นต์เครดิตคงเหลือที่อ่านได้จริง · แยกบริการ บัญชี และรอบโควตา · เก็บในเครื่องไม่เกิน 7 วัน · ไม่มีข้อมูล token หรือค่าใช้จ่าย",12));
            Check(page,"เก็บประวัติโควตา (ปิดอยู่จนกว่าจะเปิดเอง)",preferences.ShowQuotaHistory,value=>{preferences.ShowQuotaHistory=value&&FullFeatures;BuildQuotaHistoryPage(page);});
            if(!available)page.Children.Add(Text("พื้นที่เก็บประวัติยังใช้ไม่ได้ · ตรวจพื้นที่ Local AppData แล้วเปิดโปรแกรมใหม่",12));
            foreach(var choice in preferences.Monitors.Where(x=>AccountAllowed(x.Id)))
            {
                HubSnapshot data;snapshots.TryGetValue(choice.Id,out data);
                if(data==null||String.IsNullOrWhiteSpace(data.sourceKey)||data.sourceKey=="unknown")continue;
                string provider=HubNames.Label(choice.Id,data.source);
                string[] labels={String.IsNullOrWhiteSpace(data.primaryLabel)?"5h":data.primaryLabel,String.IsNullOrWhiteSpace(data.secondaryLabel)?"Weekly":data.secondaryLabel};
                for(int n=0;n<labels.Length;n++)
                {
                    string label=labels[n],identity=QuotaHistoryIdentity(choice,data,label);
                    var segments=usageHistory==null||String.IsNullOrWhiteSpace(identity)?new List<List<UsageHistoryPoint>>():usageHistory.GetSegments(provider,identity,label,DateTime.UtcNow);
                    AddQuotaHistoryCard(page,choice.FullTitle(provider),label,segments);
                }
            }
            page.Children.Add(Button("ล้างประวัติโควตาในเครื่อง","ลบเฉพาะไฟล์กราฟประวัติโควตาของ Quorilo Monitor",()=>
            {
                if(usageHistory==null||!FullFeatures)return;string scope=quotaHistoryScope;var ownedHistory=usageHistory;
                if(MessageBox.Show(settingsWindow,"ลบประวัติโควตาที่เก็บในเครื่องทั้งหมดหรือไม่?","Quorilo Monitor",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                if(!QuotaHistoryCurrent(ownedHistory,scope))return;
                try{ownedHistory.Clear();BuildQuotaHistoryPage(page);}catch{MessageBox.Show(settingsWindow,"ล้างประวัติไม่สำเร็จ · ลองใหม่อีกครั้ง",Product.Name);}
            }));
        }
        void AddQuotaHistoryCard(Panel page,string provider,string label,List<List<UsageHistoryPoint>> segments)
        {
            var card=new StackPanel{Margin=new Thickness(12)};
            var title=Text(provider+" · "+label,14,true);card.Children.Add(title);
            var all=segments.SelectMany(x=>x).OrderBy(x=>x.observedAtUtc).ToList();
            if(all.Count==0)card.Children.Add(Text("ยังไม่มีข้อมูลย้อนหลังที่เพียงพอ · จะเริ่มเก็บเมื่อเปิดใช้และมีเครดิตสด",11));
            else
            {
                var latest=all[all.Count-1];card.Children.Add(Text(new Metric{Remaining=latest.remaining}.Display+" คงเหลือ · ล่าสุด "+latest.observedAtUtc.ToLocalTime().ToString("HH:mm"),11));
                var canvas=new Canvas{Width=520,Height=82};
                foreach(double y in new[]{1.0,41.0,81.0})
                {
                    var grid=new System.Windows.Shapes.Line{X1=0,X2=520,Y1=y,Y2=y,Stroke=Brush("#66808080"),StrokeThickness=1};canvas.Children.Add(grid);
                }
                DateTime min=all.First().observedAtUtc,max=all.Last().observedAtUtc;
                double span=Math.Max(1,(max-min).TotalSeconds);
                foreach(var segment in segments)
                {
                    var accent=Brush(Theme.Accent);
                    var line=new System.Windows.Shapes.Polyline{Stroke=accent,StrokeThickness=2,StrokeLineJoin=System.Windows.Media.PenLineJoin.Round};
                    foreach(var point in segment)
                    {
                        double x=8+504*(point.observedAtUtc-min).TotalSeconds/span;
                        double y=77-0.72*point.remaining;
                        line.Points.Add(new System.Windows.Point(x,y));
                        var dot=new System.Windows.Shapes.Ellipse{Width=4,Height=4,Fill=accent};Canvas.SetLeft(dot,x-2);Canvas.SetTop(dot,y-2);canvas.Children.Add(dot);
                    }
                    if(line.Points.Count>1)canvas.Children.Add(line);
                }
                var frame=new Border{Child=new Viewbox{Child=canvas,Stretch=System.Windows.Media.Stretch.Fill},Height=92,Margin=new Thickness(0,6,0,4),Padding=new Thickness(4),CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1)};
                frame.SetResourceReference(Border.BackgroundProperty,"SettingsControlBrush");frame.SetResourceReference(Border.BorderBrushProperty,"SettingsBorderBrush");card.Children.Add(frame);
                card.Children.Add(Text(all.Count+" จุด · เส้นแบ่งเมื่อเริ่มรอบใหม่หรือข้อมูลขาดช่วง",10));
            }
            var border=new Border{Child=card,CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),Margin=new Thickness(0,8,0,0)};
            border.SetResourceReference(Border.BackgroundProperty,"SettingsCardBrush");border.SetResourceReference(Border.BorderBrushProperty,"SettingsBorderBrush");page.Children.Add(border);
        }
#endif
        StackPanel Page(TabControl tabs,string title)
        {
            var panel=new StackPanel{Margin=new Thickness(1,14,9,0)};var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};tabs.Items.Add(new TabItem{Header=title,Content=scroll});return panel;
        }
        void Heading(Panel panel,string title){var text=Text(title,17,true);text.Margin=new Thickness(0,5,0,10);panel.Children.Add(text);}
        CheckBox Check(Panel panel,string text,bool initial,Action<bool> action)
        {
            var check=new CheckBox{Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap},IsChecked=initial,Margin=new Thickness(0,5,0,8),FontSize=13};check.Checked+=(s,e)=>{action(true);Save();};check.Unchecked+=(s,e)=>{action(false);Save();};panel.Children.Add(check);return check;
        }
        void RebuildMonitorSettings()
        {
            if(monitorSettings==null)return;monitorSettings.Children.Clear();monitorSettingStatus.Clear();monitorSettingNames.Clear();Heading(monitorSettings,"บริการและบัญชีที่ติดตาม");monitorSettings.Children.Add(Text("ลากที่ ⋮⋮ บนการ์ดเพื่อจัดลำดับ หรือใช้ ↑ / ↓ ด้านล่าง\nซ่อนการ์ดยังอ่านต่อ · พักการอ่านจึงหยุดอ่าน\nMonitor Pro เพิ่มหลายบัญชีต่อบริการได้ โดยแต่ละบัญชีลงชื่อเข้าใช้แยกกัน",12));
            var addRow=new WrapPanel();foreach(var provider in UnifiedPreferences.Ids.Concat(UnifiedPreferences.OptionalIds)){string selected=provider;var add=Button("+ "+HubNames.Label(provider),"เพิ่มบัญชี "+HubNames.Label(provider),()=>AddMonitorAccount(selected));addRow.Children.Add(add);}monitorSettings.Children.Add(addRow);
            var optional=new WrapPanel();foreach(var provider in UnifiedPreferences.OptionalIds){var selected=provider;optional.Children.Add(Button("เชื่อม "+HubNames.Label(provider),"เพิ่มบริการทางเลือกแบบ read-only",()=>AddOptionalProvider(selected)));}monitorSettings.Children.Add(optional);
            var groups=new[]{"ทุกกลุ่ม","ยังไม่จัดกลุ่ม"}.Concat(preferences.Monitors.Select(x=>x.Group).Where(x=>!String.IsNullOrEmpty(x)).Distinct()).ToArray();var groupFilter=new ComboBox{ItemsSource=groups,SelectedIndex=Math.Max(0,Array.IndexOf(groups,monitorGroupFilter)),MinHeight=38,Margin=new Thickness(0,12,0,0)};groupFilter.SelectionChanged+=(s,e)=>{monitorGroupFilter=groupFilter.SelectedItem as string??"";RebuildMonitorSettings();};monitorSettings.Children.Add(groupFilter);

            if(!MultipleAccounts)monitorSettings.Children.Add(Button("ดูแพ็กเกจ Monitor Pro","เปิดราคา Monitor Pro",()=>OpenLink(AccountTrialClient.Origin+"/pricing/?plan=monitor_pro")));
            foreach(var choice in preferences.Monitors.Where(x=>String.IsNullOrEmpty(monitorGroupFilter)||monitorGroupFilter=="ทุกกลุ่ม"||monitorGroupFilter=="ยังไม่จัดกลุ่ม"?String.IsNullOrEmpty(monitorGroupFilter)||monitorGroupFilter=="ทุกกลุ่ม"||String.IsNullOrEmpty(x.Group):x.Group==monitorGroupFilter)){var panel=new StackPanel{Margin=new Thickness(13)};var head=new DockPanel();var moves=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(moves,Dock.Right);moves.Children.Add(Button("↑","เลื่อน "+ServiceLabel(choice.Id)+" ขึ้น",()=>Move(choice.Id,-1),true));moves.Children.Add(Button("↓","เลื่อน "+ServiceLabel(choice.Id)+" ลง",()=>Move(choice.Id,1),true));head.Children.Add(moves);var service=new StackPanel{Orientation=Orientation.Horizontal};service.Children.Add(ProviderLogo(choice.Id,22));var serviceName=Text(ServiceLabel(choice.Id),16,true);monitorSettingNames[choice.Id]=serviceName;service.Children.Add(serviceName);head.Children.Add(service);panel.Children.Add(head);
                var label=Text("",11);monitorSettingStatus[choice.Id]=label;panel.Children.Add(label);
                var nameRow=new DockPanel{Margin=new Thickness(0,8,0,4)};nameRow.Children.Add(Text("ชื่อบัญชี  ",12));var nickname=new TextBox{Text=choice.Nickname,MaxLength=MonitorChoice.NicknameMaxLength,Padding=new Thickness(8,5,8,5),MinWidth=120};nickname.SetResourceReference(TextBox.BackgroundProperty,"SettingsControlBrush");nickname.SetResourceReference(TextBox.ForegroundProperty,"SettingsTextBrush");nickname.SetResourceReference(TextBox.BorderBrushProperty,"SettingsBorderBrush");nameRow.Children.Add(nickname);panel.Children.Add(nameRow);nickname.LostKeyboardFocus+=(s,e)=>{choice.Nickname=MonitorChoice.NormalizeNickname(nickname.Text);nickname.Text=choice.Nickname;UpdateMonitorTitle(choice,views[choice.Id]);Save();};
                Check(panel,"แสดงชื่อบริการหน้าชื่อบัญชี",choice.ShowServiceName,value=>{choice.ShowServiceName=value;UpdateMonitorTitle(choice,views[choice.Id]);});
                var groupRow=new DockPanel{Margin=new Thickness(0,4,0,8)};groupRow.Children.Add(Text("กลุ่ม  ",12));var group=new TextBox{Text=choice.Group,MaxLength=24,MinHeight=36,Padding=new Thickness(8,5,8,5)};group.LostKeyboardFocus+=(s,e)=>{choice.Group=new String(group.Text.Where(c=>!Char.IsControl(c)).Take(24).ToArray()).Trim();Save();};groupRow.Children.Add(group);panel.Children.Add(groupRow);
                var switches=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,4)};Check(switches,"แสดงวิดเจ็ต",choice.Visible,value=>{choice.Visible=value;LayoutCards();UpdateEdge();});Check(switches,"พักการอ่าน",choice.Paused,value=>{choice.Paused=value;
#if !TRIAL
                MonitorView burnView;if(views.TryGetValue(choice.Id,out burnView)){burnView.Primary.Rate.Clear();burnView.Secondary.Rate.Clear();}
#endif
                if(!value&&AccountAllowed(choice.Id)){var unused=readers.EnsureAsync(choice.Id);}
                readers.Send(choice.Id,value?"Pause":"Resume");Paint(choice,snapshots.ContainsKey(choice.Id)?snapshots[choice.Id]:null);});Check(switches,"แจ้งเตือน",choice.Alerts,value=>choice.Alerts=value);foreach(FrameworkElement toggle in switches.Children)toggle.Margin=new Thickness(0,4,16,4);panel.Children.Add(switches);
                if(choice.Id=="ChatGPT")panel.Children.Add(Text("แหล่งเครดิต: Codex ที่ลงชื่อเข้าใช้ในเครื่อง\nChatGPT Plus บนเว็บอย่างเดียวยังอ่านไม่ได้ในรุ่นนี้",12));
                var actions=new WrapPanel();actions.Children.Add(Button(HubNames.Provider(choice.Id)=="Gemini"?"เชื่อมบัญชี Google":HubNames.Provider(choice.Id)=="Claude"?"เชื่อม Claude Code":"เชื่อม Codex","เชื่อม "+ServiceLabel(choice.Id),()=>OpenConnectionGuide(choice.Id)));actions.Children.Add(Button("รีเฟรช","รีเฟรช "+ServiceLabel(choice.Id),()=>{if(AccountAllowed(choice.Id))readers.Send(choice.Id,"Refresh");}));
                if(HubNames.Additional(choice.Id)){var selected=choice;actions.Children.Add(Button("นำบัญชีออกจากมอนิเตอร์","นำ "+choice.Nickname+" ออก",()=>RemoveMonitorAccount(selected)));panel.Children.Add(Text("บัญชีแยก · ใช้ปุ่มเชื่อมเพื่อลงชื่อเข้าใช้ และเปิด CLI ของบัญชีนี้เพื่อทำงาน ข้อมูลบัญชีอื่นไม่ถูกคัดลอก",12));if(HubNames.Provider(choice.Id)!="Gemini")actions.Children.Add(Button("เปิด CLI บัญชีนี้","เปิด CLI บัญชีนี้",()=>{if(AccountAllowed(choice.Id))readers.Send(choice.Id,"OpenAccount");}));}
                panel.Children.Add(actions);
                if(HubNames.Additional(choice.Id))panel.Children.Add(Button("หยุดและลบโปรไฟล์แยก","ลบเฉพาะโปรไฟล์แยกในเครื่องนี้",async()=>await ForgetIsolatedProfile(choice)));
                if(choice.Alerts){var options=new WrapPanel();Check(options,"เตือนรอบแรก",choice.AlertCurrent,value=>choice.AlertCurrent=value);Check(options,"เตือนรอบที่สอง",choice.AlertWeekly,value=>choice.AlertWeekly=value);Check(options,"เตือนรอบใหม่",choice.ResetAlerts,value=>choice.ResetAlerts=value);panel.Children.Add(options);
                    var custom=new ComboBox{ItemsSource=new[]{"ใช้เกณฑ์รวม","เหลือ 5%","เหลือ 10%","เหลือ 20%","เหลือ 30%"},SelectedIndex=choice.AlertThreshold==5?1:choice.AlertThreshold==10?2:choice.AlertThreshold==20?3:choice.AlertThreshold==30?4:0,MinHeight=36,Margin=new Thickness(0,5,0,8)};custom.SelectionChanged+=(s,e)=>{choice.AlertThreshold=new[]{0,5,10,20,30}[Math.Max(0,custom.SelectedIndex)];Save();};panel.Children.Add(custom);}
                var border=new Border{Child=panel,CornerRadius=new CornerRadius(Theme.Pixel?0:6),BorderThickness=new Thickness(1),Margin=new Thickness(0,12,0,0)};border.SetResourceReference(Border.BackgroundProperty,"SettingsCardBrush");border.SetResourceReference(Border.BorderBrushProperty,"SettingsBorderBrush");monitorSettings.Children.Add(border);
            }
            UpdateMonitorSettingsStatus();
        }
        void UpdateMonitorSettingsStatus(){foreach(var choice in preferences.Monitors){TextBlock text;MonitorView view;if(monitorSettingNames.TryGetValue(choice.Id,out text))text.Text=ServiceLabel(choice.Id);if(monitorSettingStatus.TryGetValue(choice.Id,out text)&&views.TryGetValue(choice.Id,out view))text.Text=view.Status.Text;}}
        async void ConnectMonitor(string id)
        {
            if(HubNames.External(id)){OpenAdditionalProviderSetup(id);return;}
            if(Product.AccountLicensed&&!Allowed){MessageBox.Show(settingsWindow??Window,"ลงชื่อเข้าใช้ Quorilo Monitor และตรวจสิทธิ์ก่อนเชื่อมบริการ",Product.Name);return;}
            if(!AccountAllowed(id)){MessageBox.Show(settingsWindow??Window,"หลายบัญชีต่อบริการใช้ได้ใน Monitor Pro · ข้อมูลบัญชียังคงอยู่เมื่ออัปเกรดกลับ",Product.Name);return;}
            try{var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==id);if(choice!=null&&choice.Paused){choice.Paused=false;Save();}await readers.EnsureAsync(id);if(!AccountAllowed(id))return;readers.Send(id,"Resume");for(int n=0;n<20&&!disposed;n++){if(!preferences.Monitors.Any(x=>x.Id==id)||!AccountAllowed(id))return;if(readers.Send(id,"Connect"))return;await Task.Delay(100);}if(!disposed&&preferences.Monitors.Any(x=>x.Id==id)&&AccountAllowed(id))MessageBox.Show(settingsWindow??Window,readers.Error(id)??"ตัวอ่านกำลังเริ่ม · ลองกดอีกครั้ง",Product.Name);}catch{if(!disposed)MessageBox.Show(settingsWindow??Window,"เชื่อมบริการไม่สำเร็จ · ลองใหม่",Product.Name);}
        }
        void AddMonitorAccount(string provider)
        {
            if(!MultipleAccounts){MessageBox.Show(settingsWindow??Window,"เพิ่มหลายบัญชีต่อบริการได้ด้วย Monitor Pro ฿199/เดือน หรือ ฿1,890/ปี",Product.Name);return;}
            if(preferences.Monitors.Count>=48){MessageBox.Show(settingsWindow??Window,"เพิ่มได้สูงสุด 48 บัญชีในเครื่องนี้",Product.Name);return;}
            var choice=new MonitorChoice{Id=provider+"."+Guid.NewGuid().ToString("N"),Nickname="บัญชี "+(preferences.Monitors.Count(x=>HubNames.Provider(x.Id)==provider)+1)};
            preferences.Monitors.Add(choice);views[choice.Id]=BuildCard(choice);cardGrid.Children.Add(views[choice.Id].Card);Save();LayoutCards();RebuildMonitorSettings();ConnectMonitor(choice.Id);
        }
        void RemoveMonitorAccount(MonitorChoice choice)
        {
            if(!HubNames.Additional(choice.Id))return;
            if(MessageBox.Show(settingsWindow??Window,"นำ "+choice.Nickname+" ออกจากมอนิเตอร์?\nไฟล์โปรไฟล์ยังเก็บในเครื่อง จึงไม่ใช่การออกจากระบบหรือลบบัญชี AI",Product.Name,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            readers.Retire(choice.Id);if(insights!=null)insights.ForgetAccount(choice.Id);preferences.Monitors.Remove(choice);cardGrid.Children.Remove(views[choice.Id].Card);views.Remove(choice.Id);snapshots.Remove(choice.Id);Save();LayoutCards();RebuildMonitorSettings();
        }
        void AboutService(StackPanel page,string name,string description)
        {
            var section=new StackPanel{Margin=new Thickness(0,0,0,14)};
            section.Children.Add(Text(name,15,true));
            var detail=Text(description,13);detail.LineHeight=22;detail.Margin=new Thickness(0,5,0,0);
            section.Children.Add(detail);page.Children.Add(section);
        }
        bool HasFresh(){return snapshots.Values.Any(x=>x!=null&&x.HeartbeatCurrent&&x.freshAvailable);}
        async Task CheckLicense(bool force)
        {
            if(!Product.Commercial||license==null||licenseBusy)return;licenseBusy=true;
            bool wasAllowed=Allowed;
            try{
                if(!Product.AccountLicensed&&license.HasKey&&license.Grant==null&&HasFresh()){await license.ReactivateAsync(true);readers.Broadcast("LicenseReload");}
                else{await license.RefreshAsync(HasFresh(),force);if(!wasAllowed&&Allowed)readers.Broadcast("LicenseReload");}
            }finally{licenseBusy=false;}
        }
        string DateText(DateTime? value){return value.HasValue?value.Value.ToLocalTime().ToString(preferences.TimeFormat=="12H"?"dd/MM/yyyy h:mm:ss tt":"dd/MM/yyyy HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture):"—";}
        void UpdateLicenseText()
        {
            if(licenseStatus==null)return;
            if(Product.FreeCore){licenseStatus.Text="Free Core · ใช้ฟีเจอร์ในเครื่องได้ฟรีตลอดไป\nไม่ต้องลงชื่อเข้าใช้ Quorilo ไม่มี Trial ไม่มีวันหมดอายุ และไม่ผูกกับ Stripe\nบัญชีผู้ให้บริการ AI และค่าบริการของผู้ให้บริการยังเป็นของคุณ";return;}
            if(!Product.Commercial){licenseStatus.Text="Owner · สำหรับเจ้าของ\nไม่มีวันทดลองและไม่ต้องใส่คีย์ · ห้ามส่งชุดนี้ให้ tester";return;}
            if(license==null||!license.Configured){licenseStatus.Text="ยังเชื่อมบริการสิทธิ์ไม่ได้ · ต้องใช้ชุด Beta ที่ผู้จัดทดลองส่งให้";return;}
            if(Product.AccountLicensed){licenseStatus.Text=(license.Allowed?(license.Grant.kind=="beta"?"Beta Full · 2 เครื่อง · ไม่มีการเรียกเก็บเงิน":license.Full?"Full · เปิดสิทธิ์แล้ว"+(license.Grant.billingMode=="test"?" (Stripe ทดสอบ)":""):"Trial · เปิดสิทธิ์แล้ว"):"ลงชื่อเข้าใช้ Quorilo Monitor / ตรวจสิทธิ์ออนไลน์")+"\nสิทธิ์ใช้ได้ถึง "+DateText(license.EntitlementEndsAt)+"\nเหลือ "+(license.EntitlementEndsAt.HasValue?Math.Max(0,(license.EntitlementEndsAt.Value-DateTime.UtcNow).TotalDays).ToString("F1"):"—")+" วัน\nTrial เริ่ม "+DateText(license.StartedAt)+"\nTrial ปัจจุบันถึง "+DateText(license.TrialEndsAt)+"\nตรวจออนไลน์ทุกนาที · ขาดเน็ตสูงสุด 2 นาที";if(!String.IsNullOrEmpty(license.LastError))licenseStatus.Text+="\n"+LicenseClient.Message(license.LastError);return;}
            licenseStatus.Text=license.Grant==null?"ตรวจรหัสเชิญก่อน แล้วเชื่อมบริการ AI\nทดลองเริ่มเมื่อเปิดใช้สำเร็จพร้อมเครดิตใหม่":(license.Allowed?"เปิดสิทธิ์แล้ว":"สิทธิ์หมดอายุ / ต้องตรวจออนไลน์")+"\nเริ่ม "+DateText(license.StartedAt)+"\nหมดอายุ "+DateText(license.TrialEndsAt)+"\nสิทธิ์ออฟไลน์ถึง "+DateText(license.ValidUntil);
            if(!String.IsNullOrEmpty(license.LastError))licenseStatus.Text+="\n"+LicenseClient.Message(license.LastError);
        }
        void BuildLicensePage(Panel page)
        {
            Heading(page,Product.FreeCore?"Free Core":Product.Commercial?"สิทธิ์ทดลองของคุณ":"สิทธิ์เจ้าของ");licenseStatus=Text("",14);page.Children.Add(licenseStatus);
            if(!Product.Commercial){UpdateLicenseText();return;}
            if(Product.AccountLicensed){Check(page,"แสดงหน้าต่างเข้าสู่ระบบเมื่อเปิดแอปและยังตรวจสิทธิ์ไม่ได้",!preferences.SuppressAccountLoginPrompt,value=>{preferences.SuppressAccountLoginPrompt=!value;Save();});page.Children.Add(Text("ลงชื่อเข้าใช้ Quorilo Monitor ก่อนใช้ · Trial เริ่มเมื่อกดเริ่มบนเว็บ\nจ่ายผ่าน Stripe แล้วเปลี่ยนเป็น Full ในแอปเดิมโดยอัตโนมัติ",12));page.Children.Add(Button("ลงชื่อเข้าใช้บนเว็บ","ลงชื่อเข้าใช้ Quorilo Monitor",async()=>{if(licenseBusy||license==null)return;licenseBusy=true;try{await license.SignInGoogleAsync(text=>{if(licenseStatus!=null)licenseStatus.Text=text;});readers.Broadcast("LicenseReload");}catch(LicenseFailure e){if(settingsWindow!=null)MessageBox.Show(settingsWindow,LicenseClient.Message(e.Code),Product.Name);}finally{licenseBusy=false;UpdateLicenseText();}}));page.Children.Add(Button("ตรวจสิทธิ์อีกครั้ง","ตรวจสิทธิ์",async()=>{await CheckLicense(true);UpdateLicenseText();}));page.Children.Add(Button("เริ่ม Trial / อัปเกรด Full","เปิดหน้าบัญชี",()=>Process.Start(new ProcessStartInfo(AccountTrialClient.Origin+"/download"){UseShellExecute=true})));page.Children.Add(Button("ออกจากบัญชีเครื่องนี้","ออกจากระบบ",()=>{license.SignOut();readers.StopTrialReaders();readers.Broadcast("LicenseReload");UpdateLicenseText();}));UpdateLicenseText();return;}
            page.Children.Add(Text("รหัสเดียว · 14 วันเดิม · ใช้ร่วมกับรุ่นแยกได้ 2 เครื่อง\nเปลี่ยนชื่อหรืออัปเดตไม่เริ่มเวลาใหม่",12));var input=new PasswordBox{MaxLength=160,Padding=new Thickness(10),Margin=new Thickness(0,12,0,10)};page.Children.Add(input);var actions=new WrapPanel();Button activate=null;
            activate=Button("เปิดใช้รหัส / เปิดเครื่องกลับ","เปิดใช้รหัสทดลอง",async()=>{
                if(license==null||licenseBusy)return;activate.IsEnabled=false;licenseBusy=true;
                try{if(HasFresh())await license.ActivateAsync(input.Password,true);else await license.PrepareAsync(input.Password);input.Clear();readers.Broadcast("LicenseReload");}
                catch(LicenseFailure e){MessageBox.Show(settingsWindow,LicenseClient.Message(e.Code),"เปิดสิทธิ์");}catch{MessageBox.Show(settingsWindow,"ติดต่อบริการสิทธิ์ไม่ได้ · ลองอีกครั้ง","เปิดสิทธิ์");}
                finally{licenseBusy=false;activate.IsEnabled=true;UpdateLicenseText();}
            });actions.Children.Add(activate);actions.Children.Add(Button("ตรวจสิทธิ์","ตรวจสิทธิ์อีกครั้ง",async()=>{try{await CheckLicense(true);readers.Broadcast("LicenseReload");UpdateLicenseText();}catch{MessageBox.Show(settingsWindow,"ตรวจสิทธิ์ไม่ได้ · ลองใหม่",Product.Name);}}));page.Children.Add(actions);
            var manage=new Expander{Header="เครื่องที่ใช้สิทธิ์",Margin=new Thickness(0,20,0,0)};var management=new StackPanel();management.Children.Add(Text("คืนช่องเพื่อเปลี่ยนเครื่อง เปิดเครื่องเดิมกลับได้ถ้ายังมีช่องว่าง ใช้วันหมดอายุเดิม",12));management.Children.Add(Button("ดูเครื่องที่เปิดใช้","โหลดรายการเครื่อง",async()=>await LoadDevices()));licenseDevicePanel=new StackPanel();management.Children.Add(licenseDevicePanel);manage.Content=management;page.Children.Add(manage);
        }
        async Task LoadDevices()
        {
            if(license==null||licenseDevicePanel==null)return;
            try{var devices=await license.DevicesAsync();if(licenseDevicePanel==null)return;licenseDevicePanel.Children.Clear();if(devices.Length==0)licenseDevicePanel.Children.Add(Text("ยังไม่มีเครื่องที่เปิดใช้",12));foreach(var device in devices){var row=new DockPanel{Margin=new Thickness(0,8,0,0)};var release=Button("คืนช่อง","คืนช่องเครื่อง "+device.name,async()=>{try{await license.DeactivateAsync(device.instance);readers.Broadcast("LicenseReload");await LoadDevices();UpdateLicenseText();}catch{MessageBox.Show(settingsWindow,"คืนช่องไม่ได้ · ลองใหม่",Product.Name);}});DockPanel.SetDock(release,Dock.Right);row.Children.Add(release);row.Children.Add(Text(device.name+(device.current?" · เครื่องนี้":""),12));licenseDevicePanel.Children.Add(row);}}
            catch{if(settingsWindow!=null)MessageBox.Show(settingsWindow,"โหลดรายการเครื่องไม่ได้ · ตรวจการเชื่อมต่อ",Product.Name);}
        }
    }
}
