using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        Window connectionGuideWindow;
        string connectionGuideId;
        TextBlock connectionGuideStatus;
        Button connectionGuideRetry;
        readonly System.Collections.Generic.Dictionary<string,TextBlock> welcomeReadings=new System.Collections.Generic.Dictionary<string,TextBlock>();
        ReadingState Reading(MonitorChoice choice,HubSnapshot data)
        {
            if(HubNames.Additional(choice.Id)&&!MultipleAccounts)return new ReadingState{Summary="ต้องใช้ Monitor Pro · บัญชียังเก็บในเครื่อง",Detail="เปิดสิทธิ์ใช้งานเพื่อตรวจแผน Monitor Pro ก่อนอ่านบัญชีเพิ่มเติม",Action="rights"};
            if(data==null&&AccountAllowed(choice.Id)&&!choice.Paused&&!String.IsNullOrEmpty(readers.Error(choice.Id)))return new ReadingState{Summary="เริ่มตัวอ่านไม่สำเร็จ",Detail="กดตรวจอีกครั้ง หากยังไม่สำเร็จให้ตรวจไฟล์ติดตั้งและเปิดวิธีเชื่อม",Action="refresh"};
            return ReadingPresentation.Describe(HubNames.Provider(choice.Id),data,choice.Paused,AccountAllowed(choice.Id),readers.Refreshing(choice.Id),DateTime.UtcNow);
        }
        void ApplyReadingHelp(MetricView view,Metric metric,TimeSpan duration,bool fresh)
        {
            string help=ReadingPresentation.MetricHelp(metric,duration,DateTime.UtcNow)+"\n"+Reset(metric,true)+(fresh?"":"\nเครดิตที่แสดงยังไม่ยืนยันว่าเป็นค่าปัจจุบัน");
            view.Value.ToolTip=view.Reset.ToolTip=help;
            var tip=view.Track.ToolTip as ToolTip;
            if(tip==null){tip=new ToolTip{PlacementTarget=view.Track,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};view.Track.ToolTip=tip;var openedTip=tip;view.Track.GotKeyboardFocus+=(s,e)=>openedTip.IsOpen=true;view.Track.LostKeyboardFocus+=(s,e)=>openedTip.IsOpen=false;}
            tip.Content=help;
            view.Track.Focusable=true;AutomationProperties.SetName(view.Track,view.Label.Text+" · "+help);AutomationProperties.SetHelpText(view.Track,help);
            ToolTipService.SetShowDuration(view.Track,20000);
        }
        void OpenConnectionGuide(string id)
        {
            if(HubNames.External(id)){OpenAdditionalProviderSetup(id);return;}
            if(disposed||!windowLifecycle.CanShow)return;
            var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==id);if(choice==null)return;
            if(connectionGuideWindow!=null){connectionGuideWindow.Close();}
            edgeDelay.Stop();Reveal();connectionGuideId=id;
            var window=new Window{Title="เชื่อม "+ServiceLabel(id),Width=560,Height=540,MinWidth=420,MinHeight=420,Owner=Window,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=Window.FontFamily};
            connectionGuideWindow=window;foreach(var d in Window.Resources.MergedDictionaries)window.Resources.MergedDictionaries.Add(d);ApplyPalette(window);window.SetResourceReference(Window.BackgroundProperty,"SettingsBackgroundBrush");
            var body=new StackPanel{Margin=new Thickness(24)};Heading(body,ServiceLabel(id)+" · "+choice.Nickname);
            body.Children.Add(Text(ReadingPresentation.Instructions(HubNames.Provider(id),ServiceLabel(id)=="Gemini CLI"),14));
            connectionGuideStatus=Text("",13);connectionGuideStatus.Margin=new Thickness(0,20,0,12);AutomationProperties.SetLiveSetting(connectionGuideStatus,AutomationLiveSetting.Polite);body.Children.Add(connectionGuideStatus);
            var actions=new WrapPanel();actions.Children.Add(Button("เปิดการเชื่อมต่อ","เปิดหน้าต่างเชื่อมเมื่อคุณกด",()=>ConnectMonitor(id)));connectionGuideRetry=Button("ตรวจอีกครั้ง","รับข้อมูลใหม่เพื่อยืนยันการเชื่อม",()=>RetryReading(id));actions.Children.Add(connectionGuideRetry);actions.Children.Add(Button("เสร็จ / ทำภายหลัง","ปิดวิธีเชื่อม",()=>window.Close()));body.Children.Add(actions);
            body.Children.Add(Text("ยืนยันสำเร็จเมื่อรับโควตาที่ถูกต้องภายใน 90 วินาที · ข้ามแล้วกลับมาเชื่อมภายหลังได้",11));
            window.Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(s,e)=>UpdateConnectionGuide();
            window.Closed+=(s,e)=>{timer.Stop();if(Object.ReferenceEquals(connectionGuideWindow,window)){connectionGuideWindow=null;connectionGuideStatus=null;connectionGuideRetry=null;connectionGuideId=null;}if(!disposed&&windowLifecycle.CanShow)ScheduleEdge();};
            UpdateConnectionGuide();timer.Start();window.Show();
        }
        async void RetryReading(string id)
        {
            var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==id);if(choice==null)return;
            if(!AccountAllowed(id)){OpenSettings();if(settingsWindow!=null){var dock=settingsWindow.Content as DockPanel;var tabs=dock==null?null:dock.Children.OfType<TabControl>().FirstOrDefault();if(tabs!=null)foreach(TabItem item in tabs.Items)if((string)item.Header=="สิทธิ์ใช้งาน")tabs.SelectedItem=item;}return;}
            if(choice.Paused){choice.Paused=false;readers.Send(id,"Resume");Save();RebuildMonitorSettings();}
            try{await readers.EnsureAsync(id);if(disposed||!preferences.Monitors.Any(x=>x.Id==id)||!AccountAllowed(id))return;readers.Send(id,"Resume");readers.Send(id,"Refresh");UpdateConnectionGuide();}
            catch{if(connectionGuideStatus!=null)connectionGuideStatus.Text="เริ่มตัวอ่านไม่ได้ · ตรวจไฟล์ติดตั้งแล้วลองอีกครั้ง";}
        }
        void UpdateConnectionGuide()
        {
            if(connectionGuideStatus==null||connectionGuideId==null)return;
            var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==connectionGuideId);if(choice==null)return;
            HubSnapshot data;snapshots.TryGetValue(choice.Id,out data);var state=Reading(choice,data);
            connectionGuideStatus.Text=state.Summary+"\n"+state.Detail;
            connectionGuideRetry.Content=state.Action=="resume"?"ทำงานต่อและตรวจใหม่":state.Action=="rights"?"เปิดสิทธิ์ใช้งาน":"ตรวจอีกครั้ง";
            connectionGuideRetry.IsEnabled=!readers.Refreshing(choice.Id);
        }
        void UpdateWelcomeReadings()
        {
            foreach(var pair in welcomeReadings){var choice=preferences.Monitors.FirstOrDefault(x=>x.Id==pair.Key);if(choice==null)continue;HubSnapshot data;snapshots.TryGetValue(choice.Id,out data);var state=Reading(choice,data);pair.Value.Text=state.Summary+"\n"+state.Detail;}
        }
    }
}
