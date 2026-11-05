using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        Window additionalProviderWindow;
        bool AdditionalSetupCurrent(Window window,MonitorChoice choice,string expectedScope){return !disposed&&Object.ReferenceEquals(additionalProviderWindow,window)&&preferences.Monitors.Any(current=>Object.ReferenceEquals(current,choice))&&AccountAllowed(choice.Id)&&ProviderAccountScope()==expectedScope;}
        string ProviderAccountScope(){return !Allowed?null:Product.Commercial?(license.Grant==null?null:license.Grant.subjectHash):Product.LocalCoreScope;}
        void AddOptionalProvider(string provider)
        {
            if(!HubNames.External(provider))return;
            var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==provider);
            if(choice==null){if(preferences.Monitors.Count>=48)return;choice=new MonitorChoice{Id=provider};preferences.Monitors.Add(choice);views[choice.Id]=BuildCard(choice);cardGrid.Children.Add(views[choice.Id].Card);Save();LayoutCards();RebuildMonitorSettings();}
            OpenAdditionalProviderSetup(choice.Id);
        }
        void OpenAdditionalProviderSetup(string id)
        {
            if(disposed||!windowLifecycle.CanShow||!AccountAllowed(id)){OpenSettings();return;}
            var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==id);if(choice==null)return;
            if(additionalProviderWindow!=null)additionalProviderWindow.Close();string expectedScope=ProviderAccountScope();readers.SetAccountScope(expectedScope);
            AdditionalProviderCredential saved=readers.ExternalCredential(id);
            var window=new Window{Title="เชื่อม "+HubNames.Label(id),Width=630,Height=620,MinWidth=460,MinHeight=440,Owner=Window,ShowInTaskbar=false,FontFamily=Window.FontFamily,WindowStartupLocation=WindowStartupLocation.CenterOwner};additionalProviderWindow=window;
            foreach(var dictionary in Window.Resources.MergedDictionaries)window.Resources.MergedDictionaries.Add(dictionary);ApplyPalette(window);window.SetResourceReference(Window.BackgroundProperty,"SettingsBackgroundBrush");
            var body=new StackPanel{Margin=new Thickness(24)};Heading(body,HubNames.Label(id)+" · อ่านอย่างเดียว");
            bool cursor=HubNames.Provider(id)=="Cursor";
            body.Children.Add(Text(cursor?"Cursor Enterprise Admin API · แสดงงบและการใช้ on-demand ของสมาชิกที่คุณเลือก\nแยกจาก included quota · ตรวจรายชั่วโมง · ไม่คาดเดาเวลารีเซ็ต":"GitHub Copilot ส่วนตัว · รายงาน premium requests ที่เรียกเก็บจากบัญชีคุณ\nPAT แบบ fine-grained ต้องมี Plan: read · ไม่รวมการใช้ที่องค์กรจ่าย\nแสดงจำนวนตาม API โดยไม่สร้าง % โควตาที่ API ไม่ได้ให้มา",13));
            body.Children.Add(Button("เปิดเอกสารการสร้าง credential","เปิดเอกสารทางการเมื่อคุณกด",()=>OpenLink(cursor?"https://cursor.com/docs/account/teams/admin-api":"https://docs.github.com/en/rest/billing/usage")));
            body.Children.Add(Text(cursor?"อีเมลหรือ userId ของสมาชิก Cursor":"GitHub username ของเจ้าของ PAT",13,true));
            var account=new TextBox{Text=saved==null?"":saved.Account,MaxLength=254,MinHeight=40,Padding=new Thickness(10),Margin=new Thickness(0,5,0,14)};body.Children.Add(account);
            body.Children.Add(Text(cursor?"Admin API key":"Personal access token · Plan read",13,true));
            var secret=new PasswordBox{MaxLength=4096,MinHeight=40,Padding=new Thickness(10),Margin=new Thickness(0,5,0,12)};body.Children.Add(secret);
            body.Children.Add(Text(saved==null?"กรอก credential ในแอปนี้ · ไม่ส่งผ่านแชตหรือบันทึกลงซอร์ส":"บันทึก credential แบบเข้ารหัสไว้แล้ว · เว้นช่องว่างเพื่อใช้ตัวเดิมกับบัญชีเดิม",11));
            var status=Text("ยังไม่อ่านข้อมูลจนกว่าคุณจะบันทึกและตรวจ",12);status.Margin=new Thickness(0,12,0,12);body.Children.Add(status);
            var buttons=new WrapPanel();buttons.Children.Add(Button("บันทึกและตรวจ","เข้ารหัส credential และตรวจบัญชีที่เลือก",()=>{
                if(!AdditionalSetupCurrent(window,choice,expectedScope)){secret.Clear();status.Text="บัญชีนี้ถูกนำออกหรือสิทธิ์เปลี่ยนแล้ว · ปิดและเปิดใหม่";return;}
                try{string selected=account.Text.Trim();var credential=new AdditionalProviderCredential{Account=selected,Token=String.IsNullOrEmpty(secret.Password)&&saved!=null&&String.Equals(selected,saved.Account,StringComparison.OrdinalIgnoreCase)?saved.Token:secret.Password};readers.ConfigureExternal(id,credential);saved=null;secret.Clear();snapshots.Remove(id);choice.Paused=false;Save();readers.Send(id,"Resume");var unused=readers.EnsureAsync(id);status.Text="บันทึกแบบเข้ารหัสแล้ว · กำลังตรวจสิทธิ์อ่านรายงาน…";}
                catch{status.Text="บันทึกไม่ได้ · ตรวจชื่อบัญชี credential และสิทธิ์ read-only";}
            }));buttons.Children.Add(Button("ลืม credential บัญชีนี้","หยุดอ่านและลบ credential ของบัญชีนี้",()=>{
                if(!AdditionalSetupCurrent(window,choice,expectedScope))return;
                if(MessageBox.Show(window,"หยุดอ่านและลบ credential ที่บันทึกไว้สำหรับบัญชีนี้?",Product.Name,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                if(!AdditionalSetupCurrent(window,choice,expectedScope))return;
                readers.ForgetExternal(id);saved=null;secret.Clear();snapshots.Remove(id);choice.Paused=true;Save();status.Text="ลบ credential ในเครื่องแล้ว · หากต้องการเพิกถอน token ให้ทำในผู้ให้บริการด้วย";
            }));buttons.Children.Add(Button("เสร็จ / ทำภายหลัง","ปิดหน้าต่าง",()=>window.Close()));body.Children.Add(buttons);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(s,e)=>{if(!AdditionalSetupCurrent(window,choice,expectedScope)){secret.Clear();window.Close();return;}HubSnapshot data=snapshots.ContainsKey(id)?snapshots[id]:null;if(data!=null){var state=Reading(choice,data);status.Text=state.Summary+"\n"+state.Detail;}};
            window.Closed+=(s,e)=>{timer.Stop();secret.Clear();saved=null;if(Object.ReferenceEquals(additionalProviderWindow,window))additionalProviderWindow=null;};window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};timer.Start();window.Show();
        }
        async Task ForgetIsolatedProfile(MonitorChoice choice)
        {
            if(!HubNames.Additional(choice.Id))return;
            if(MessageBox.Show(settingsWindow??Window,"หยุดตัวอ่านและลบโปรไฟล์แยกของ "+choice.Nickname+" ในเครื่องนี้?\nบัญชีหลักของบริการไม่เปลี่ยน และไม่ได้ลบบัญชีจากผู้ให้บริการ",Product.Name,MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            try{
                if(HubNames.External(choice.Id))readers.ForgetExternal(choice.Id);else await readers.ForgetProfileAsync(choice.Id);
                if(notificationPolicy!=null)notificationPolicy.Forget(choice.Id);if(insights!=null)insights.ForgetAccount(choice.Id);preferences.Monitors.Remove(choice);snapshots.Remove(choice.Id);cardGrid.Children.Remove(views[choice.Id].Card);views.Remove(choice.Id);Save();LayoutCards();RebuildMonitorSettings();
            }catch{MessageBox.Show(settingsWindow??Window,"ตัวอ่านยังหยุดไม่เสร็จหรือโปรไฟล์ถูกล็อก · ยังไม่ลบข้อมูล ลองใหม่ภายหลัง",Product.Name);}
        }
    }
}
