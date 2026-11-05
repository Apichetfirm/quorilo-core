using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
[assembly:AssemblyTitle(CreditMonitor.Product.Name)]
[assembly:AssemblyProduct(CreditMonitor.Product.Name)]
[assembly:AssemblyCompany("Firm Impulse")]
[assembly:AssemblyVersion(CreditMonitor.Product.AssemblyVersion)]
[assembly:AssemblyFileVersion(CreditMonitor.Product.AssemblyVersion)]
namespace CreditMonitor
{
    internal sealed class MetricView
    {
        public TextBlock Label,Value,Reset;
        public Border Track,Fill,ElapsedTick;
        public Border Badge;
        public Grid Row;
#if !TRIAL
        public TextBlock Burn;
        public BurnRate Rate;
#endif
    }
    internal sealed class MonitorView
    {
        public Border Card;
        public TextBlock Title,Status,Report;
        public System.Windows.Shapes.Ellipse Dot;
        public Button Pause;
        public MetricView Primary,Secondary;
        public StackPanel Panel,Actions,StatusRow;
        public Grid Metrics;
        public StackPanel PrimaryPanel,SecondaryPanel;
        public UIElement Refresh,Drag;
    }
    public sealed partial class UnifiedMonitor:IDisposable
    {
        public readonly Window Window;
        readonly string root,settingsPath;
        readonly UnifiedReaders readers;
        readonly Dictionary<string,HubSnapshot> snapshots=new Dictionary<string,HubSnapshot>();
        readonly Dictionary<string,MonitorView> views=new Dictionary<string,MonitorView>();
        readonly HashSet<string> alerted=new HashSet<string>();
        readonly DispatcherTimer clock=new DispatcherTimer(),saveDelay=new DispatcherTimer(),edgeDelay=new DispatcherTimer(),cardFitDelay=new DispatcherTimer();
        readonly List<CreditMonitorWindowSignal> signals=new List<CreditMonitorWindowSignal>();
        UnifiedPreferences preferences;
        readonly SystemAwakeGuard awakeGuard=new SystemAwakeGuard();
        readonly MonitorWindowLifecycle windowLifecycle=new MonitorWindowLifecycle();
        Border frame;Grid cardGrid;Grid body,header;TextBlock brandLabel;TextBlock footer;Button pin;Window settingsWindow,edgeWindow;
        bool exiting,disposed,polling,edgeHidden,edgeMoving,movingWindow,fittingCards,welcomeEvaluated,firstRealCreditsLogged,hidingToTray;
        bool CanPresentWindows {get{return windowLifecycle.CanShow&&!disposed&&!hidingToTray;}}
        double expandedLeft;int animationRevision;DateTime nextLicenseCheck=DateTime.MinValue,nextHubStatusWrite=DateTime.MinValue;
        string lastHubStatusSignature;
        Forms.NotifyIcon tray;Forms.ContextMenuStrip trayMenu;
        LicenseClient license;CommerceConfiguration commerce;
#if !TRIAL
        UsageHistory usageHistory;string quotaHistoryScope,quotaHistoryViewKey;
#endif
        bool licenseBusy;TextBlock licenseStatus;StackPanel licenseDevicePanel;
        readonly string alertPath;
        public UnifiedMonitor()
        {
            StartupProfile.Mark("unified_ctor_start");
            root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            settingsPath=Path.Combine(Product.Storage,"settings.json");alertPath=Path.Combine(Product.Storage,"alert-keys.json");
            preferences=UnifiedPreferences.Load(settingsPath);readers=new UnifiedReaders(root);
            Window=new MonitorWindow(windowLifecycle){Title=Product.Name,Width=preferences.Width,Height=preferences.Height,MinWidth=280,MinHeight=100,MaxWidth=1400,MaxHeight=1000,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=preferences.Topmost,UseLayoutRounding=true,SnapsToDevicePixels=true};
            Window.FontFamily=new FontFamily(new Uri(Path.Combine(root,"Fonts")+Path.DirectorySeparatorChar),"./#Noto Sans Thai");
            Window.Resources.MergedDictionaries.Add((ResourceDictionary)CompiledViews.Load("SettingsStyles.xaml"));
            System.Windows.Shell.WindowChrome.SetWindowChrome(Window,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(WidgetRadius)});
            cardFitDelay.Interval=TimeSpan.FromMilliseconds(60);cardFitDelay.Tick+=(s,e)=>{if(edgeMoving)return;cardFitDelay.Stop();FitWindowToCards();};
            ApplyPalette(Window);BuildWindow();
            Window.SizeChanged+=(s,e)=>{if(frame!=null)frame.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,frame.ActualWidth),Math.Max(0,frame.ActualHeight)),WidgetRadius,WidgetRadius);LayoutCards(false);if(e.WidthChanged)RequestCardFit();ScheduleSave();};
            Window.LocationChanged+=(s,e)=>{if(!edgeMoving&&!edgeHidden)ScheduleSave();};
            Window.MouseEnter+=(s,e)=>edgeDelay.Stop();
            Window.MouseLeave+=(s,e)=>ScheduleEdge();
            Window.Closing+=(s,e)=>{windowLifecycle.EnterClosingCallback();try{if(windowLifecycle.IsRunning&&!exiting){e.Cancel=true;HideTray();}else{windowLifecycle.MarkClosed();}}finally{windowLifecycle.ExitClosingCallback();}};
            Window.Closed+=(s,e)=>windowLifecycle.MarkClosed();
            Window.KeyDown+=(s,e)=>{
                if(e.Key==Key.Escape)HideTray();
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.OemComma)OpenSettings();
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.M)ToggleCompact();
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.R)readers.Broadcast("Refresh");
                if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.P)TogglePin();
            };
            saveDelay.Interval=TimeSpan.FromMilliseconds(400);saveDelay.Tick+=(s,e)=>{saveDelay.Stop();SaveBounds();};
            edgeDelay.Interval=TimeSpan.FromMilliseconds(850);edgeDelay.Tick+=(s,e)=>{edgeDelay.Stop();HideEdge();};
            clock.Interval=TimeSpan.FromSeconds(Product.AccountLicensed?1:2);clock.Tick+=(s,e)=>EnforceAccountAccess();clock.Tick+=async(s,e)=>await PollAsync();
            signals.Add(new CreditMonitorWindowSignal(Program.InstanceName+"-Show",Window.Dispatcher,Show));
            signals.Add(new CreditMonitorWindowSignal(Program.InstanceName+"-Settings",Window.Dispatcher,()=>{Show();OpenSettings();}));
            signals.Add(new CreditMonitorWindowSignal(Program.InstanceName+"-Stop",Window.Dispatcher,Exit));
            commerce=CommerceConfiguration.Load();if(Product.Commercial)try{license=new LicenseClient(commerce,LicenseClient.Storage,LicenseClient.DeviceIdentity());}catch{}
            SystemEvents.PowerModeChanged+=OnPowerResume;
        }
        ThemePreset Theme{get{return ThemeCatalog.Find(preferences.Theme);}}
        double WidgetRadius{get{return Theme.Pixel?0:Theme.Key=="Minimal"?6:8;}}
        Brush Brush(string color){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}
        internal void ApplyPalette(Window window)
        {
            var t=Theme;var popupColor=(Color)ColorConverter.ConvertFromString(t.Surface);popupColor.A=255;window.Resources["SettingsPopupBrush"]=new SolidColorBrush(popupColor);window.Resources["SettingsBackgroundBrush"]=t.SurfaceBrush();window.Resources["SettingsCardBrush"]=Brush(t.MetricSurface);window.Resources["SettingsControlBrush"]=Brush(t.Hover);window.Resources["SettingsBorderBrush"]=Brush(t.Edge);window.Resources["SettingsTextBrush"]=Brush(t.Text);window.Resources["SettingsMutedBrush"]=Brush(t.Muted);window.Resources["SettingsAccentBrush"]=Brush(t.Accent);window.Resources["SettingsOnAccentBrush"]=Brush(t.Light?"#FFFFFF":"#101925");window.Resources["SettingsNavBrush"]=Brush(t.Track);window.Resources["SettingsSelectedBrush"]=Brush(t.Hover);window.Resources["SettingsGoodBrush"]=Brush(t.Light?"#137A49":"#4ADE80");window.Resources["SettingsWarningBrush"]=Brush(t.Light?"#916000":"#FACC15");window.Resources["SettingsBadBrush"]=Brush(t.Light?"#B52547":"#FB7185");
        }
        TextBlock Text(string text,double size=13,bool strong=false)
        {
            var value=new TextBlock{Text=text,FontSize=size,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};value.SetResourceReference(TextBlock.ForegroundProperty,"SettingsTextBrush");return value;
        }
        Button Button(string text,string tip,Action action,bool small=false)
        {
            var b=new Button{Content=text,ToolTip=tip,Margin=new Thickness(3,0,0,0),MinHeight=small?24:38,MinWidth=small?24:0,FontSize=small?12:13,Padding=small?new Thickness(4,2,4,2):new Thickness(12,7,12,7)};
            if(small)b.Background=Brushes.Transparent;AutomationProperties.SetName(b,tip);b.Click+=(s,e)=>{try{action();}catch{MessageBox.Show(settingsWindow??Window,"ทำรายการไม่สำเร็จ · ลองอีกครั้ง",Product.Name);}};return b;
        }
        void BuildWindow()
        {
            var chrome=System.Windows.Shell.WindowChrome.GetWindowChrome(Window);if(chrome!=null)chrome.CornerRadius=new CornerRadius(WidgetRadius);
            views.Clear();body=new Grid{Margin=new Thickness(14,10,14,10)};body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            header=new Grid{Margin=new Thickness(0,0,0,10)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});body.Children.Add(header);
            var identity=new StackPanel();var name=Text("Quorilo Monitor",16,true);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;identity.Children.Add(name);var brand=Text("",10);brandLabel=brand;brand.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");identity.Children.Add(brand);header.Children.Add(identity);
            identity.MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton!=MouseButtonState.Pressed)return;edgeDelay.Stop();movingWindow=true;try{Window.DragMove();}catch{}finally{movingWindow=false;ScheduleSave();ScheduleEdge();}};
            var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(tools,1);header.Children.Add(tools);
            tools.Children.Add(IconButton("refresh","รีเฟรชทุกบริการ",()=>readers.Broadcast("Refresh")));tools.Children.Add(IconButton("settings","ตั้งค่าและจัดลำดับ",OpenSettings));pin=IconButton("pin","ปักหมุด / ซ่อนริมจอ",TogglePin);tools.Children.Add(pin);tools.Children.Add(IconButton("compact","ย่อ / ขยาย",ToggleCompact));tools.Children.Add(IconButton("minus","ซ่อนไปถาดระบบ",HideTray));
            cardGrid=new Grid();var scroll=new ScrollViewer{Content=cardGrid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);body.Children.Add(scroll);
            footer=Text("กำลังเริ่มตัวอ่าน…",10);footer.Margin=new Thickness(0,7,0,0);footer.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");Grid.SetRow(footer,2);body.Children.Add(footer);
            frame=new Border{Child=body,CornerRadius=new CornerRadius(WidgetRadius),BorderThickness=new Thickness(1),Background=Theme.SurfaceBrush(),BorderBrush=Brush(Theme.Edge)};Window.Content=frame;
            frame.SizeChanged+=(s,e)=>frame.Clip=new RectangleGeometry(new Rect(0,0,frame.ActualWidth,frame.ActualHeight),WidgetRadius,WidgetRadius);
            foreach(var choice in preferences.Monitors){var view=BuildCard(choice);views[choice.Id]=view;cardGrid.Children.Add(view.Card);}LayoutCards();UpdatePin();
        }
        FrameworkElement ProviderLogo(string id,double size)
        {
            id=HubNames.Provider(id);
            try{var bitmap=new BitmapImage(new Uri(Path.Combine(root,"Logos",id+".png")));bitmap.Freeze();
                if(id=="ChatGPT")return new Border{Width=size,Height=size,Margin=new Thickness(0,0,7,0),Background=Brush(Theme.Text),OpacityMask=new ImageBrush(bitmap){Stretch=Stretch.Uniform},VerticalAlignment=VerticalAlignment.Center};
                return new Image{Source=bitmap,Width=size,Height=size,Margin=new Thickness(0,0,7,0),Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center};
            }catch{return new Border{Width=0};}
        }
        MonitorView BuildCard(MonitorChoice choice)
        {
            var view=new MonitorView();var panel=new StackPanel{Margin=new Thickness(11,8,11,8)};view.Panel=panel;
            var heading=new Grid();heading.ColumnDefinitions.Add(new ColumnDefinition());heading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});panel.Children.Add(heading);
            view.Title=Text("",14,true);UpdateMonitorTitle(choice,view);view.Title.TextWrapping=TextWrapping.NoWrap;view.Title.TextTrimming=TextTrimming.CharacterEllipsis;var identity=new Grid();identity.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});identity.ColumnDefinitions.Add(new ColumnDefinition());identity.Children.Add(ProviderLogo(choice.Id,18));Grid.SetColumn(view.Title,1);identity.Children.Add(view.Title);heading.Children.Add(identity);
            var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);heading.Children.Add(actions);view.Actions=actions;
            var drag=Text("⋮⋮",14,true);drag.Margin=new Thickness(4,0,4,0);drag.Cursor=Cursors.SizeAll;drag.ToolTip="ลากเพื่อจัดลำดับ · หรือใช้ปุ่มขึ้น/ลงในตั้งค่า";actions.Children.Add(drag);view.Drag=drag;
            Point start=new Point();drag.PreviewMouseLeftButtonDown+=(s,e)=>{start=e.GetPosition(drag);};drag.MouseMove+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(drag)-start).Length>6){edgeDelay.Stop();DragDrop.DoDragDrop(drag,new DataObject("FirmImpulse.Monitor",choice.Id),DragDropEffects.Move);ScheduleEdge();}};
            var refresh=IconButton("refresh","รีเฟรช "+ServiceLabel(choice.Id),()=>readers.Send(choice.Id,"Refresh"));actions.Children.Add(refresh);view.Refresh=refresh;view.Pause=Button("","พัก / ทำงานต่อ "+ServiceLabel(choice.Id),()=>{choice.Paused=!choice.Paused;
#if !TRIAL
                MonitorView burnView;if(views.TryGetValue(choice.Id,out burnView)){burnView.Primary.Rate.Clear();burnView.Secondary.Rate.Clear();}
#endif
readers.Send(choice.Id,choice.Paused?"Pause":"Resume");Save();RebuildMonitorSettings();},true);actions.Children.Add(view.Pause);actions.Children.Add(Button("?","สถานะและวิธีเชื่อม "+ServiceLabel(choice.Id),()=>OpenConnectionGuide(choice.Id),true));
            view.Metrics=new Grid();view.Metrics.ColumnDefinitions.Add(new ColumnDefinition());view.Metrics.ColumnDefinitions.Add(new ColumnDefinition());panel.Children.Add(view.Metrics);view.PrimaryPanel=new StackPanel();view.SecondaryPanel=new StackPanel();view.Metrics.Children.Add(view.PrimaryPanel);view.Metrics.Children.Add(view.SecondaryPanel);view.Primary=BuildMetric(view.PrimaryPanel);view.Secondary=BuildMetric(view.SecondaryPanel);
#if !TRIAL
            string burnRoot=Product.Storage;view.Primary.Rate=new BurnRate(choice.Id=="Gemini"?Path.Combine(burnRoot,"burn-rate-gemini-5h.json"):null);view.Secondary.Rate=new BurnRate(choice.Id=="Gemini"?Path.Combine(burnRoot,"burn-rate-gemini-weekly.json"):null);
#endif
            view.Report=Text("",12);view.Report.Visibility=Visibility.Collapsed;panel.Children.Add(view.Report);
            var status=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,0)};view.Dot=new System.Windows.Shapes.Ellipse{Width=6,Height=6,Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};view.Status=Text("กำลังเริ่มตัวอ่าน",10);view.Status.TextWrapping=TextWrapping.NoWrap;view.Status.TextTrimming=TextTrimming.CharacterEllipsis;status.Children.Add(view.Dot);status.Children.Add(view.Status);panel.Children.Add(status);view.StatusRow=status;
            view.Card=new Border{Child=panel,CornerRadius=new CornerRadius(Theme.Pixel||Theme.Key=="Minimal"?0:Theme.Key=="Material"?6:4),Background=Brush(Theme.MetricSurface),BorderBrush=Brush(Theme.MetricEdge),BorderThickness=Theme.Key=="Minimal"?new Thickness(0,0,0,1):new Thickness(1),Effect=Theme.Key=="Material"?new System.Windows.Media.Effects.DropShadowEffect{Color=(Color)ColorConverter.ConvertFromString("#6D28D9"),Opacity=0.15,BlurRadius=6,ShadowDepth=1}:null,Margin=new Thickness(0,0,0,8),AllowDrop=true};
            view.Card.DragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent("FirmImpulse.Monitor")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
            view.Card.Drop+=(s,e)=>{if(!e.Data.GetDataPresent("FirmImpulse.Monitor"))return;Reorder((string)e.Data.GetData("FirmImpulse.Monitor"),choice.Id);e.Handled=true;};
            return view;
        }
        MetricView BuildMetric(Panel parent)
        {
            var view=new MetricView();var row=new Grid{Margin=new Thickness(0,5,0,0)};view.Row=row;row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(53)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            view.Label=Text("5h",12,true);row.Children.Add(view.Label);view.Value=Text("--",15,true);view.Value.TextWrapping=TextWrapping.NoWrap;view.Value.HorizontalAlignment=HorizontalAlignment.Center;view.Badge=new Border{Child=view.Value,MinWidth=58,Padding=new Thickness(7,1,7,1),CornerRadius=new CornerRadius(Theme.Pixel?0:12),BorderThickness=new Thickness(1)};Grid.SetColumn(view.Badge,2);row.Children.Add(view.Badge);
            view.Fill=new Border{HorizontalAlignment=HorizontalAlignment.Left,Width=0,CornerRadius=new CornerRadius(Theme.BarRadius)};view.Track=new Border{Height=5,CornerRadius=new CornerRadius(Theme.BarRadius),Background=Brush(Theme.Track),Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};AddElapsedWindowTick(view);Grid.SetColumn(view.Track,1);row.Children.Add(view.Track);parent.Children.Add(row);
            view.Reset=Text("resets in —",10);view.Reset.Margin=new Thickness(0,1,0,0);view.Reset.HorizontalAlignment=HorizontalAlignment.Center;view.Reset.TextWrapping=TextWrapping.NoWrap;view.Reset.TextTrimming=TextTrimming.CharacterEllipsis;view.Reset.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");parent.Children.Add(view.Reset);
#if !TRIAL
            view.Burn=Text("Burn rate —",10);view.Burn.Margin=new Thickness(0,2,0,0);view.Burn.HorizontalAlignment=HorizontalAlignment.Center;view.Burn.Visibility=Visibility.Collapsed;parent.Children.Add(view.Burn);
#endif
            return view;
        }
        void LayoutCards(bool fit=true)
        {
            if(cardGrid==null)return;cardGrid.RowDefinitions.Clear();cardGrid.ColumnDefinitions.Clear();
            bool compact=preferences.Compact;int columns=Math.Min(Math.Max(1,preferences.Monitors.Count(x=>x.Visible)),compact?1:Window.Width>=1050?3:Window.Width>=720?2:1);
            for(int n=0;n<columns;n++)cardGrid.ColumnDefinitions.Add(new ColumnDefinition());
            body.Margin=compact?new Thickness(9,6,9,6):new Thickness(12,9,12,9);header.Margin=new Thickness(0,0,0,compact?5:10);brandLabel.Visibility=footer.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            int index=0;foreach(var choice in preferences.Monitors){MonitorView view;if(!views.TryGetValue(choice.Id,out view))continue;view.Card.Visibility=choice.Visible?Visibility.Visible:Visibility.Collapsed;if(!choice.Visible)continue;
                int row=index/columns;if(cardGrid.RowDefinitions.Count<=row)cardGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(view.Card,row);Grid.SetColumn(view.Card,index%columns);view.Card.Margin=new Thickness(index%columns>0?8:0,0,0,8);
                view.Primary.Reset.Visibility=view.Secondary.Reset.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                view.Panel.Margin=compact?new Thickness(7,3,7,3):new Thickness(9,7,9,7);view.Card.Margin=new Thickness(index%columns>0?8:0,0,0,compact?4:8);
                view.StatusRow.Visibility=view.Refresh.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                if(compact&&view.StatusRow.Children.Contains(view.Dot)){view.StatusRow.Children.Remove(view.Dot);view.Actions.Children.Insert(0,view.Dot);}
                if(!compact&&view.Actions.Children.Contains(view.Dot)){view.Actions.Children.Remove(view.Dot);view.StatusRow.Children.Insert(0,view.Dot);}
                view.Pause.MinHeight=view.Pause.MinWidth=compact?20:28;view.Pause.Padding=new Thickness(3,0,3,0);view.Pause.FontSize=compact?11:14;
                view.Metrics.RowDefinitions.Clear();view.Metrics.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});if(!compact)view.Metrics.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                Grid.SetRow(view.PrimaryPanel,0);Grid.SetColumn(view.PrimaryPanel,0);Grid.SetColumnSpan(view.PrimaryPanel,compact?1:2);Grid.SetRow(view.SecondaryPanel,compact?0:1);Grid.SetColumn(view.SecondaryPanel,compact?1:0);Grid.SetColumnSpan(view.SecondaryPanel,compact?1:2);
                view.PrimaryPanel.Margin=new Thickness(0,0,compact?7:0,0);
                foreach(var m in new[]{view.Primary,view.Secondary}){m.Row.Margin=new Thickness(0,compact?1:5,0,0);m.Row.ColumnDefinitions[0].Width=new GridLength(compact?36:53);m.Label.FontSize=compact?10:12;m.Track.Height=Theme.Key=="Minimal"?2:Theme.Key=="Material"?6:5;m.Badge.CornerRadius=new CornerRadius(Theme.Key=="Minimal"?2:Theme.Key=="Material"?8:12);m.Badge.MinWidth=compact?34:58;m.Badge.Padding=compact?new Thickness(3,0,3,0):new Thickness(7,1,7,1);m.Track.Margin=new Thickness(compact?3:8,0,compact?4:8,0);}
                UpdateMonitorTitle(choice,view);
                view.Title.FontWeight=Theme.Key=="Minimal"?FontWeights.Normal:FontWeights.SemiBold;view.Title.FontSize=Math.Min(compact?13:18,Math.Max(13,Window.Width/columns/27));view.Primary.Value.FontSize=view.Secondary.Value.FontSize=compact?12:Math.Min(23,Math.Max(16,Window.Width/columns/22));
                index++;
            }
            if(footer!=null)footer.Text=index==0?"ยังไม่ได้เลือกการ์ด · เปิดตั้งค่าเพื่อเลือกแสดง":"หนึ่งบัญชีต่อบริการ · Preview "+Product.Version.Replace("-preview","");
            if(fit)RequestCardFit();
        }
        void Reorder(string from,string before)
        {
            var item=preferences.Monitors.FirstOrDefault(x=>x.Id==from);var target=preferences.Monitors.FirstOrDefault(x=>x.Id==before);if(item==null||target==null||item==target)return;
            preferences.Monitors.Remove(item);int index=preferences.Monitors.IndexOf(target);preferences.Monitors.Insert(index,item);LayoutCards();Save();UpdateEdge();RebuildMonitorSettings();
        }
        void Move(string id,int direction)
        {
            int index=preferences.Monitors.FindIndex(x=>x.Id==id),target=index+direction;if(index<0||target<0||target>=preferences.Monitors.Count)return;
            var item=preferences.Monitors[index];preferences.Monitors.RemoveAt(index);preferences.Monitors.Insert(target,item);LayoutCards();Save();UpdateEdge();RebuildMonitorSettings();
        }
        bool FullFeatures{get{return Product.LocalCoreAccess||(license!=null&&license.Full);}}
        bool MultipleAccounts{get{return Product.LocalCoreAccess||(license!=null&&license.MultipleAccounts);}}
        bool AccountAllowed(string id){return Allowed&&(!HubNames.Additional(id)||MultipleAccounts);}
        bool Allowed{get{return Product.LocalCoreAccess||(license!=null&&license.Allowed);}}
        void UpdateMonitorTitle(MonitorChoice choice,MonitorView view){string serviceName=ServiceLabel(choice.Id);view.Title.Text=choice.CardTitle(serviceName);view.Title.ToolTip=choice.FullTitle(serviceName);}
        string ServiceLabel(string id){HubSnapshot data;snapshots.TryGetValue(id,out data);return HubNames.Label(id,data==null?null:data.source);}
        void EnforceAccountAccess()
        {
            if(!Product.AccountLicensed||Allowed)return;
            readers.StopTrialReaders();
            foreach(var choice in preferences.Monitors){snapshots[choice.Id]=null;if(views.ContainsKey(choice.Id))Paint(choice,null);}
        }
        async Task PollAsync()
        {
            if(polling||disposed)return;polling=true;
            try{
                if(Product.AccountLicensed){await CheckLicense(false);if(!Allowed)readers.StopTrialReaders();}if(!welcomeEvaluated){welcomeEvaluated=true;if(preferences.ShouldShowWelcome(Product.AccountLicensed,Allowed))OpenWelcome();}
                readers.SetAccountScope(!Allowed?null:Product.Commercial?(license.Grant==null?null:license.Grant.subjectHash):Product.LocalCoreScope);
                var ensureTasks=new List<Task>();
                foreach(var choice in preferences.Monitors.ToArray())if(!choice.Paused&&AccountAllowed(choice.Id))ensureTasks.Add(readers.EnsureAsync(choice.Id));
                if(ensureTasks.Count>0)await Task.WhenAll(ensureTasks);
                if(disposed)return;
                foreach(var choice in preferences.Monitors.ToArray()){if(!views.ContainsKey(choice.Id))continue;if(!AccountAllowed(choice.Id))readers.Stop(choice.Id);var data=AccountAllowed(choice.Id)?readers.Read(choice.Id):null;snapshots[choice.Id]=data;
                    if(viewForRefresh(choice.Id)!=null)viewForRefresh(choice.Id).IsEnabled=!readers.Refreshing(choice.Id);
                    if(data!=null){if(data.paused!=choice.Paused)readers.Send(choice.Id,choice.Paused?"Pause":"Resume");if(choice.Id=="Gemini"&&data.refreshSeconds!=preferences.GeminiRefreshSeconds)readers.Send(choice.Id,"Gemini"+preferences.GeminiRefreshSeconds);}
                    Paint(choice,data);
                    if(!firstRealCreditsLogged&&Fresh(data)&&(CurrentMetric(data,true)!=null||CurrentMetric(data,false)!=null)){firstRealCreditsLogged=true;StartupProfile.Mark("first_real_credits",choice.Id);}
                }
                if(Product.Commercial&&!Product.AccountLicensed&&DateTime.UtcNow>=nextLicenseCheck&&!licenseBusy){nextLicenseCheck=DateTime.UtcNow.AddMinutes(1);await CheckLicense(false);}
                UpdateLicenseText();UpdateMonitorSettingsStatus();UpdateEdge();CheckCombinedAlerts();SyncCompanion();UpdateInsights();
                WriteHubStatusAsync();
            }catch{footer.Text="กำลังเชื่อมต่อใหม่ · เปิดตั้งค่าเพื่อดูสถานะ";}finally{polling=false;}
        }
        void WriteHubStatusAsync()
        {
            string signature=Window.IsVisible+"|"+edgeHidden+"|"+String.Join(",",preferences.Monitors.Select(x=>x.Id+":"+(x.Visible?"1":"0")+":"+(x.Paused?"1":"0")));
            if(signature==lastHubStatusSignature&&DateTime.UtcNow<nextHubStatusWrite)return;
            lastHubStatusSignature=signature;
            nextHubStatusWrite=DateTime.UtcNow.AddSeconds(5);
            string json=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{
                version=Product.Version,commercial=Product.Commercial,visible=Window.IsVisible,edgeHidden=edgeHidden,
                updatedAt=DateTime.UtcNow.ToString("o"),
                order=preferences.Monitors.Select(x=>new{id=x.Id,visible=x.Visible,paused=x.Paused}).ToArray()
            });
            Task.Run(()=>{try{Directory.CreateDirectory(Product.Storage);File.WriteAllText(Path.Combine(Product.Storage,"hub-status.json"),json,new System.Text.UTF8Encoding(false));}catch{}});
        }        Metric CurrentMetric(HubSnapshot data,bool primary)
        {
            if(!Allowed||data==null||!data.HeartbeatCurrent)return null;var raw=primary?data.primary:data.secondary;var metric=raw==null?null:raw.ToMetric();if(metric!=null&&metric.ResetAt.HasValue&&metric.ResetAt.Value<=DateTime.UtcNow)return null;return metric;
        }
        bool Fresh(HubSnapshot data){return data!=null&&!data.paused&&(data.state=="Live"||data.state=="Partial")&&data.Received.HasValue&&DateTime.UtcNow-data.Received.Value<TimeSpan.FromMinutes(3)&&DateTime.UtcNow-data.Received.Value>TimeSpan.FromSeconds(-5);}
        void Paint(MonitorChoice choice,HubSnapshot data)
        {
            MonitorView view=views[choice.Id];bool fresh=Reading(choice,data).Verified;view.Pause.Content=ToolIcon(choice.Paused?"play":"pause",preferences.Compact?11:13);
            UpdateMonitorTitle(choice,view);
            PaintMetric(view.Primary,CurrentMetric(data,true),data==null?"5h":data.primaryLabel,fresh,TimeSpan.FromHours(5));PaintMetric(view.Secondary,CurrentMetric(data,false),data==null?"Weekly":data.secondaryLabel,fresh,TimeSpan.FromDays(7));
#if !TRIAL
            RecordQuotaHistory(choice,data,fresh);
            PaintBurn(view.Primary,CurrentMetric(data,true),data,fresh,data==null?"5h":data.primaryLabel,choice.Paused);PaintBurn(view.Secondary,CurrentMetric(data,false),data,fresh,data==null?"Weekly":data.secondaryLabel,choice.Paused);
#endif
            bool reportOnly=data!=null&&data.capability=="UsageReport";view.Metrics.Visibility=reportOnly?Visibility.Collapsed:Visibility.Visible;view.Report.Visibility=data!=null&&!String.IsNullOrWhiteSpace(data.reportSummary)?Visibility.Visible:Visibility.Collapsed;view.Report.Text=data==null?"":data.reportSummary??"";
            var reading=Reading(choice,data);fresh=reading.Verified;
            view.Status.Text=reading.Summary;view.Card.ToolTip=ServiceLabel(choice.Id)+" · "+choice.Nickname+"\n"+reading.Summary+"\n"+reading.Detail;view.Status.ToolTip=view.Card.ToolTip;
            view.Dot.BeginAnimation(UIElement.OpacityProperty,null);view.Dot.Fill=Brush(fresh?"#4ADE80":choice.Paused?Theme.Muted:data!=null&&(data.state=="Error"||data.state=="LoginRequired")?"#FB7185":"#94A3B8");view.Dot.Opacity=1;
            if(fresh&&preferences.AnimateStatus&&SystemParameters.ClientAreaAnimation&&Window.IsVisible)view.Dot.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0.4,1,TimeSpan.FromSeconds(1.8)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        }
#if !TRIAL
        bool EnsureQuotaHistory()
        {
            string scope=!FullFeatures?null:Product.Commercial?(license.Grant==null?null:license.Grant.subjectHash):Product.LocalCoreScope;
            if(scope!="owner"&&!(Product.FreeCore&&scope==Product.LocalCoreScope)&&!System.Text.RegularExpressions.Regex.IsMatch(scope??"","^[a-f0-9]{64}$"))scope=null;
            string viewKey=scope==null?null:scope+"|"+MultipleAccounts;
            if(viewKey!=quotaHistoryViewKey){if(quotaHistoryPage!=null)quotaHistoryPage.Children.Clear();quotaHistoryViewKey=viewKey;}
            if(scope!=quotaHistoryScope){quotaHistoryScope=scope;usageHistory=null;}
            if(scope==null)return false;
            try{if(usageHistory==null)usageHistory=new UsageHistory(Path.Combine(Product.Storage,"quota-history",scope+".json"));return true;}catch{usageHistory=null;return false;}
        }
        bool QuotaHistoryCurrent(UsageHistory expected,string expectedScope){return EnsureQuotaHistory()&&expectedScope==quotaHistoryScope&&Object.ReferenceEquals(expected,usageHistory);}
        string QuotaHistoryIdentity(MonitorChoice choice,HubSnapshot data,string label)
        {
            if(data==null||String.IsNullOrWhiteSpace(data.sourceKey)||data.sourceKey=="unknown"||data.sourceKey=="codex")return null;            return choice.Id+"|"+data.sourceKey+"|"+data.source+"|"+label;        }
        void RecordQuotaHistory(MonitorChoice choice,HubSnapshot data,bool fresh)
        {
            if(!FullFeatures){EnsureQuotaHistory();return;}
            if(!preferences.ShowQuotaHistory&&usageHistory==null)return;
            if(!EnsureQuotaHistory())return;
            if(quotaHistoryPage!=null&&quotaHistoryPage.Children.Count==0)BuildQuotaHistoryPage(quotaHistoryPage);
            if(!preferences.ShowQuotaHistory||!AccountAllowed(choice.Id)||data==null||!data.Received.HasValue)return;
            string provider=HubNames.Label(choice.Id,data.source);
            for(int index=0;index<2;index++)
            {
                var metric=CurrentMetric(data,index==0);
                if(metric==null||!metric.ResetAt.HasValue)continue;
                string label=index==0?data.primaryLabel:data.secondaryLabel;
                if(String.IsNullOrWhiteSpace(label))label=index==0?"5h":"Weekly";
                string identity=QuotaHistoryIdentity(choice,data,label);
                if(String.IsNullOrWhiteSpace(identity))continue;
                try{usageHistory.Record(provider,identity,label,metric.Remaining,metric.ResetAt.Value.ToUniversalTime(),data.Received.Value.ToUniversalTime(),fresh&&!choice.Paused);}
                catch{}
            }
        }
#endif
        static string Age(DateTime time){double seconds=Math.Max(0,(DateTime.UtcNow-time).TotalSeconds);return seconds<60?Math.Floor(seconds)+" วินาทีก่อน":seconds<3600?Math.Floor(seconds/60)+" นาทีก่อน":Math.Floor(seconds/3600)+" ชั่วโมงก่อน";}
        void PaintMetric(MetricView view,Metric metric,string label,bool fresh,TimeSpan windowDuration)
        {
            view.Label.Text=String.IsNullOrWhiteSpace(label)?"—":label;view.Value.Text=metric==null?"--":metric.Display;var color=Brush(!fresh||metric==null?Theme.Muted:Theme.MetricColor(metric.Remaining));view.Value.Foreground=view.Fill.Background=color;view.Track.Opacity=fresh?1:0.55;
            double width=Math.Max(0,view.Track.ActualWidth);view.Fill.Width=metric==null?0:width*metric.Remaining/100;PaintElapsedWindowTick(view,metric,windowDuration);
            var badgeColor=((SolidColorBrush)color).Color;badgeColor.A=24;view.Badge.Background=Theme.Key=="Minimal"?Brushes.Transparent:new SolidColorBrush(badgeColor);badgeColor.A=65;view.Badge.BorderBrush=new SolidColorBrush(badgeColor);
            view.Reset.Text=Reset(metric);ApplyReadingHelp(view,metric,windowDuration,fresh);
        }
#if !TRIAL
        void PaintBurn(MetricView view,Metric metric,HubSnapshot data,bool fresh,string label,bool paused)
        {
            bool show=preferences.ShowBurnRate&&FullFeatures;var before=view.Burn.Visibility;view.Burn.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            double? rate=null;
            if(show&&metric!=null&&data!=null&&data.Received.HasValue){string identity=data.source!=null&&data.source.StartsWith("Gemini Apps",StringComparison.Ordinal)?data.sourceKey+"|"+data.source+"|"+label:data.processId+"|"+data.observationScope+"|"+data.sourceKey+"|"+data.source+"|"+label;rate=view.Rate.Observe(metric.Remaining,data.Received.Value,DateTime.UtcNow,metric.ResetAt,identity,fresh&&!paused);}
            else view.Rate.Clear();
            view.Burn.Text=rate.HasValue?"≈ "+BurnRate.Format(rate.Value)+"%/นาที":"Burn rate — · รอข้อมูลใหม่";
            view.Burn.ToolTip="เฉลี่ยจากโควตาที่อ่านได้ย้อนหลังสูงสุด 5 นาที · กู้คืนตัวอย่างล่าสุดหลังเริ่มโปรแกรมได้\nจุดเปอร์เซ็นต์/นาที แยกแต่ละรอบโควตา · 0.0 = ไม่พบโควตาลดในช่วงนี้ · <0.1 = ใช้เล็กน้อย\nโควตาอาจอัปเดตเป็นช่วง ๆ ความละเอียดและความล่าช้าขึ้นกับผู้ให้บริการ";
            if(before!=view.Burn.Visibility)RequestCardFit();
        }
#endif
        string Reset(Metric metric,bool full=false)
        {
            if(metric==null||!metric.ResetAt.HasValue)return "resets in —";DateTime date=metric.ResetAt.Value.ToLocalTime();string day=full?date.ToString("dd MMM yyyy",CultureInfo.InvariantCulture):date.Date==DateTime.Today?"Today":date.Date==DateTime.Today.AddDays(1)?"Tomorrow":date.ToString("dd MMM",CultureInfo.InvariantCulture);
            return QuotaFormatting.ResetLabel(metric)+" | "+day+" "+date.ToString(preferences.TimeFormat=="12H"?"h:mm tt":"HH:mm",CultureInfo.InvariantCulture);
        }
        void LoadAlertHistory()
        {
            try{if(File.Exists(alertPath)&&new FileInfo(alertPath).Length<65536)foreach(var key in new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(alertPath))??new string[0])alerted.Add(key);}catch{}
        }
        void SetupTray()
        {
            if(tray!=null||disposed)return;
            trayMenu=new Forms.ContextMenuStrip{Font=new System.Drawing.Font("Segoe UI",9)};
            Action<string,Action> add=(name,action)=>trayMenu.Items.Add(name,null,(s,e)=>action());add("แสดง Quorilo Monitor",Show);add("Insights · ประวัติการใช้",OpenInsights);add("ตั้งค่า / จัดลำดับ",OpenSettings);add("รีเฟรชทุกบริการ",()=>readers.Broadcast("Refresh"));add("ย่อ / ขยาย",ToggleCompact);trayMenu.Items.Add(new Forms.ToolStripSeparator());add("ส่ง feedback",()=>OpenLink("https://docs.google.com/forms/d/e/1FAIpQLSeQaK_qCX-cUKuuz-myn_F_kfHAEjs2ExTzTyAp3O1e0KOeIg/viewform"));add("ออกจากโปรแกรม",Exit);
            tray=new Forms.NotifyIcon{Icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),Text="Quorilo Monitor",ContextMenuStrip=trayMenu,Visible=true};tray.DoubleClick+=(s,e)=>Show();tray.BalloonTipClicked+=(s,e)=>ShowNotificationAccounts();
        }
        void CheckCombinedAlerts(){CheckAccountNotifications();}
        void OnPowerResume(object sender,PowerModeChangedEventArgs e){
#if !TRIAL
            if(e.Mode==PowerModes.Suspend||e.Mode==PowerModes.Resume)Window.Dispatcher.BeginInvoke(new Action(()=>{foreach(var v in views.Values){v.Primary.Rate.Clear();v.Secondary.Rate.Clear();}}));
#endif
            if(Product.AccountLicensed&&e.Mode==PowerModes.Suspend&&license!=null){license.SuspendTrial();Window.Dispatcher.BeginInvoke(new Action(()=>readers.StopTrialReaders()));}if(e.Mode==PowerModes.Resume&&!disposed)Window.Dispatcher.BeginInvoke(new Action(()=>{if(disposed)return;awakeGuard.SetEnabled(preferences.KeepAwake);if(Product.AccountLicensed&&license!=null){license.SuspendTrial();readers.StopTrialReaders();}readers.Broadcast("Refresh");nextLicenseCheck=DateTime.MinValue;}));}
        void Save(){try{preferences.Save(settingsPath);}catch{if(footer!=null)footer.Text="บันทึกตั้งค่าไม่ได้ · ตรวจสิทธิ์โฟลเดอร์";}}
        void ScheduleSave(){if(!disposed&&!edgeMoving&&!edgeHidden){saveDelay.Stop();saveDelay.Start();}}
        void SaveBounds(){if(edgeMoving||edgeHidden)return;preferences.Width=Window.Width;preferences.Height=Window.Height;preferences.Left=Window.Left;preferences.Top=Window.Top;Save();}
        void RequestCardFit(){if(disposed||fittingCards)return;cardFitDelay.Stop();cardFitDelay.Start();}
        void FitWindowToCards()
        {
            if(disposed||frame==null||fittingCards)return;fittingCards=true;
            try{
                // Measure the current cards and chrome without a fixed height; the scroll viewer
                // remains available when their natural height exceeds this display's work area.
                frame.Measure(new Size(Window.Width,Double.PositiveInfinity));
                var area=WorkArea();double limit=Math.Max(Window.MinHeight,Math.Min(Window.MaxHeight,area.Height));
                double height=Math.Max(Window.MinHeight,Math.Min(limit,Math.Ceiling(frame.DesiredSize.Height)));
                if(Math.Abs(Window.Height-height)>0.5)Window.Height=height;
                if(!edgeHidden){
                    if(!Double.IsNaN(Window.Top))Window.Top=Math.Max(area.Top,Math.Min(Window.Top,area.Bottom-height));
                    if(!Double.IsNaN(Window.Left))Window.Left=Math.Max(area.Left,Math.Min(Window.Left,area.Right-Window.Width));
                    SaveBounds();
                }
            }finally{fittingCards=false;}
        }
        void ToggleCompact(){preferences.Compact=!preferences.Compact;Window.Width=preferences.Compact?340:320;LayoutCards();SaveBounds();}
        void UpdatePin(){if(pin!=null){pin.Content=ToolIcon("pin",13,Brush(preferences.Pinned?Theme.Accent:Theme.Muted));((FrameworkElement)pin.Content).RenderTransformOrigin=new Point(0.5,0.5);((FrameworkElement)pin.Content).RenderTransform=new RotateTransform(preferences.Pinned?0:35);pin.ToolTip=preferences.Pinned?"ปักหมุดอยู่ · ไม่ซ่อนริมจอ":"ซ่อนริมจออัตโนมัติ";}}
        void TogglePin(){preferences.Pinned=!preferences.Pinned;UpdatePin();Save();if(preferences.Pinned)Reveal();else ScheduleEdge();}
        void ScheduleEdge(){if(!CanPresentWindows)return;if(!preferences.Pinned&&!edgeHidden&&!edgeMoving&&!movingWindow&&settingsWindow==null&&welcomeWindow==null&&connectionGuideWindow==null&&Window.IsVisible){edgeDelay.Stop();edgeDelay.Start();}}
        Rect WorkArea(){try{var source=PresentationSource.FromVisual(Window);var rect=Forms.Screen.FromHandle(new WindowInteropHelper(Window).Handle).WorkingArea;var transform=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;var a=transform.Transform(new Point(rect.Left,rect.Top));var b=transform.Transform(new Point(rect.Right,rect.Bottom));return new Rect(a,b);}catch{return SystemParameters.WorkArea;}}
        void HideEdge()
        {
            if(!CanPresentWindows||edgeMoving||preferences.Pinned||Window.IsMouseOver||settingsWindow!=null||welcomeWindow!=null||connectionGuideWindow!=null||movingWindow||!Window.IsVisible)return;var area=WorkArea();bool left=preferences.EdgeSide=="Left"||preferences.EdgeSide=="Nearest"&&Window.Left+Window.Width/2<area.Left+area.Width/2;expandedLeft=Window.Left;SaveBounds();edgeHidden=true;
            if(edgeWindow==null){edgeLayoutKey=null;var panel=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,3,0,3)};
                edgeWindow=new Window{Width=72,Height=40,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Topmost=preferences.Topmost,FontFamily=Window.FontFamily,Content=new Border{Child=panel,Background=Theme.SurfaceBrush(),BorderBrush=Brush(Theme.Edge),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(Theme.Pixel?0:5)}};ApplyPalette(edgeWindow);edgeWindow.Closing+=(s,e)=>{if(!windowLifecycle.IsExitingOrClosed&&!disposed){e.Cancel=true;HideTray();}};edgeWindow.MouseEnter+=(s,e)=>Reveal();edgeWindow.MouseLeftButtonDown+=(s,e)=>Reveal();
            }
            UpdateEdge();
            edgeWindow.Left=left?area.Left:area.Right-edgeWindow.Width;edgeWindow.Top=Math.Max(area.Top,Math.Min(Window.Top,area.Bottom-edgeWindow.Height));UpdateEdge();
            AnimateLeft(left?area.Left-Window.Width:area.Right,()=>{if(!CanPresentWindows||!edgeHidden)return;Window.Hide();Window.Left=expandedLeft;edgeWindow.Show();});
        }
        void AnimateLeft(double target,Action done)
        {
            if(!CanPresentWindows)return;int revision=++animationRevision;edgeMoving=true;Window.BeginAnimation(Window.LeftProperty,null);
            if(!preferences.Motion||!SystemParameters.ClientAreaAnimation){Window.Left=target;edgeMoving=false;if(revision==animationRevision&&CanPresentWindows)done();return;}
            var effect=new HorizontalMotionBlurEffect();frame.Effect=effect;effect.BeginAnimation(HorizontalMotionBlurEffect.RadiusProperty,new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(240),KeyFrames={new LinearDoubleKeyFrame(0,KeyTime.FromPercent(0)),new LinearDoubleKeyFrame(0.016,KeyTime.FromPercent(0.4)),new LinearDoubleKeyFrame(0,KeyTime.FromPercent(1))}});
            var move=new DoubleAnimation(Window.Left,target,TimeSpan.FromMilliseconds(240)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};move.Completed+=(s,e)=>{if(revision!=animationRevision||!CanPresentWindows)return;Window.BeginAnimation(Window.LeftProperty,null);Window.Left=target;frame.Effect=null;edgeMoving=false;done();};Window.BeginAnimation(Window.LeftProperty,move);
        }
        void CancelEdgeAnimation(bool restoreExpanded){bool restoreTarget=restoreExpanded&&(edgeHidden||edgeMoving);animationRevision++;double left=Window.Left;Window.BeginAnimation(Window.LeftProperty,null);if(frame!=null)frame.Effect=null;edgeMoving=false;Window.Left=restoreTarget?expandedLeft:left;}
        void Reveal(){edgeDelay.Stop();if(!CanPresentWindows||!edgeHidden)return;double from=Window.Left;bool pending=edgeMoving;CancelEdgeAnimation(false);edgeHidden=false;if(edgeWindow!=null)edgeWindow.Hide();var area=WorkArea();if(!pending)from=expandedLeft+Window.Width/2<area.Left+area.Width/2?area.Left-Window.Width:area.Right;Window.Left=from;if(!ShowMainWindow())return;AnimateLeft(expandedLeft,()=>{});}
        bool ShowMainWindow(){if(!CanPresentWindows)return false;Window.Show();return true;}
        void HideTray(){if(hidingToTray||disposed||windowLifecycle.IsClosed)return;hidingToTray=true;try{edgeDelay.Stop();CancelEdgeAnimation(true);edgeHidden=false;if(connectionGuideWindow!=null)connectionGuideWindow.Close();if(welcomeWindow!=null)welcomeWindow.Close();if(settingsWindow!=null)settingsWindow.Close();if(edgeWindow!=null)edgeWindow.Hide();Window.Hide();}finally{hidingToTray=false;}}
        Button viewForRefresh(string id){MonitorView v;return views.TryGetValue(id,out v)?v.Refresh as Button:null;}
        public void Show(){if(!CanPresentWindows)return;Reveal();if(!ShowMainWindow())return;Window.Activate();ScheduleEdge();}
        void OpenLink(string url){Uri uri;if(Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https")Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
        public void Start()
        {
            if(disposed||!windowLifecycle.CanShow)return;
            StartupProfile.Mark("unified_start_begin");
            awakeGuard.SetEnabled(preferences.KeepAwake);
            var area=WorkArea();
            Window.Left=preferences.Left.HasValue?Math.Max(area.Left,Math.Min(preferences.Left.Value,area.Right-Window.Width)):area.Right-Window.Width-24;
            Window.Top=preferences.Top.HasValue?Math.Max(area.Top,Math.Min(preferences.Top.Value,area.Bottom-Window.Height)):area.Top+40;
            if(!ShowMainWindow())return;
            StartupProfile.Mark("unified_window_shown");
            foreach(var choice in preferences.Monitors)Paint(choice,null);
            if(!Product.Commercial||(license!=null&&!license.HasKey)){welcomeEvaluated=true;if(preferences.ShouldShowWelcome(Product.AccountLicensed,Allowed))OpenWelcome();}
            Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>StartupProfile.Mark("unified_ui_ready")));
            ScheduleEdge();
            clock.Start();
            Window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(async ()=>{
                if(disposed||!windowLifecycle.CanShow)return;
                SetupTray();
                LoadAlertHistory();
                SetupInsights();
                StartupProfile.Mark("unified_background_init_done");
                await PollAsync();
                StartupProfile.Mark("unified_first_poll_done");
                if(!disposed&&windowLifecycle.CanShow&&preferences.ShouldShowWelcome(Product.AccountLicensed,Allowed))OpenWelcome();
            }));
        }
        public void Exit(){if(!windowLifecycle.BeginClose())return;exiting=true;SaveBounds();Dispose();if(welcomeWindow!=null)welcomeWindow.Close();if(settingsWindow!=null)settingsWindow.Close();if(edgeWindow!=null)edgeWindow.Close();if(!windowLifecycle.IsClosed)Window.Close();}
        public void Dispose(){if(disposed)return;awakeGuard.Dispose();disposed=true;if(additionalProviderWindow!=null)additionalProviderWindow.Close();if(updateCancellation!=null)updateCancellation.Cancel();if(connectionGuideWindow!=null)connectionGuideWindow.Close();DisposeInsights();clock.Stop();saveDelay.Stop();edgeDelay.Stop();cardFitDelay.Stop();animationRevision++;SystemEvents.PowerModeChanged-=OnPowerResume;readers.Dispose();foreach(var signal in signals)signal.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}if(trayMenu!=null)trayMenu.Dispose();}
    }
    public static class Program
    {
        public static readonly string InstanceName="Local\\"+Product.StorageName+"-"+WindowsIdentity.GetCurrent().User.Value;
        [STAThread] public static int Main(string[] args)
        {
            StartupProfile.Mark("process_entry");
            if(args.Contains("--uninstall"))return Lifecycle.Uninstall(false);if(args.Contains("--finish-uninstall"))return Lifecycle.FinishUninstall();
            using(var mutex=new Mutex(false,InstanceName)){bool acquired;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(!acquired){try{using(var signal=EventWaitHandle.OpenExisting(InstanceName+"-Show"))signal.Set();}catch{}return 0;}StartupProfile.Mark("mutex_acquired");
                try{var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};MonitorExceptionLog.Install(app,()=>Product.Storage);using(var hub=new UnifiedMonitor()){app.MainWindow=hub.Window;app.SessionEnding+=(s,e)=>hub.Exit();hub.Start();app.Run();}return 0;}catch(Exception e){MonitorExceptionLog.Write(e,"Program.Main",Product.Storage);MessageBox.Show("เปิด Quorilo Monitor ไม่สำเร็จ\n"+e.Message,Product.Name);return 1;}finally{mutex.ReleaseMutex();}}
        }
    }
}
