using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CreditMonitor
{
    public sealed partial class UnifiedMonitor
    {
        readonly Dictionary<string,Button> themeButtons=new Dictionary<string,Button>();
        string edgeLayoutKey;
        readonly Dictionary<string,TextBlock> edgeValues=new Dictionary<string,TextBlock>();
        readonly Dictionary<string,Grid> edgeRows=new Dictionary<string,Grid>();

        FrameworkElement ToolIcon(string name,double size=13,Brush color=null)
        {
            string path=name=="refresh"?"M19 6V11H14 M5 18V13H10 M6 9A6 6 0 0 1 16 5L19 8 M18 15A6 6 0 0 1 8 19L5 16":
                name=="settings"?"M9 3L10 1H14L15 3L17 4L19 4L21 8L20 10V14L21 16L19 20L17 20L15 21L14 23H10L9 21L7 20H5L3 16L4 14V10L3 8L5 4H7Z M16 12A4 4 0 1 1 8 12A4 4 0 1 1 16 12":
                name=="pin"?"M8 3H16 M9 3V10L6 14H18L15 10V3 M12 14V21":
                name=="compact"?"M4 9V4H9 M15 20H20V15 M4 4L10 10 M20 20L14 14":
                name=="minus"?"M5 12H19":name=="play"?"M8 5L19 12L8 19Z":"M8 5V19 M16 5V19";
            var shape=new System.Windows.Shapes.Path{Data=Geometry.Parse(path),StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
            if(color==null)shape.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty,"SettingsMutedBrush");else shape.Stroke=color;
            var canvas=new Canvas{Width=24,Height=24};canvas.Children.Add(shape);
            return new Viewbox{Width=size,Height=size,Child=canvas,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};
        }
        void AddElapsedWindowTick(MetricView view)
        {
            view.ElapsedTick=new Border{Width=2,Height=11,CornerRadius=new CornerRadius(1),Background=Brush(Theme.Text),Opacity=0.78,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
            var bar=new Grid();bar.Children.Add(view.Fill);bar.Children.Add(view.ElapsedTick);view.Track.Child=bar;
        }
        void PaintElapsedWindowTick(MetricView view,Metric metric,TimeSpan windowDuration)
        {
            var ratio=QuotaFormatting.ElapsedRatio(metric,windowDuration,DateTime.UtcNow);double width=Math.Max(0,view.Track.ActualWidth);
            if(!ratio.HasValue||width<2){view.ElapsedTick.Visibility=Visibility.Collapsed;return;}
            double left=Math.Max(0,Math.Min(width-2,width*ratio.Value-1));view.ElapsedTick.Margin=new Thickness(left,0,0,0);view.ElapsedTick.Visibility=Visibility.Visible;
        }
        Button IconButton(string icon,string tip,Action action)
        {
            var button=Button("",tip,action,true);button.Content=ToolIcon(icon);return button;
        }
        FrameworkElement ThemePreview(ThemePreset theme)
        {
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text="Quorilo Monitor",FontSize=9,FontWeight=FontWeights.SemiBold,Foreground=Brush(theme.Text),Margin=new Thickness(0,0,0,4)});
            foreach(bool primary in new[]{true,false}){
                var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(27)});
                row.Children.Add(new TextBlock{Text=primary?"5h":"Weekly",FontSize=8,Foreground=Brush(theme.Text),VerticalAlignment=VerticalAlignment.Center});
                var fill=new Border{Height=4,CornerRadius=new CornerRadius(theme.BarRadius),Background=Brush(theme.MetricColor(primary?82:46)),HorizontalAlignment=HorizontalAlignment.Left};
                var track=new Border{Height=4,Background=Brush(theme.Track),CornerRadius=new CornerRadius(theme.BarRadius),Child=fill,Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};track.SizeChanged+=(s,e)=>fill.Width=Math.Max(0,track.ActualWidth)*(primary?0.82:0.46);Grid.SetColumn(track,1);row.Children.Add(track);
                var value=new TextBlock{Text=primary?"82%":"46%",FontSize=9,FontWeight=FontWeights.SemiBold,Foreground=fill.Background,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(value,2);row.Children.Add(value);panel.Children.Add(row);
            }
            return new Border{Child=panel,Height=69,Padding=new Thickness(8),CornerRadius=new CornerRadius(Math.Min(5,theme.Radius)),Background=theme.SurfaceBrush(),BorderBrush=Brush(theme.Edge),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,7)};
        }
        void AddThemeChoices(Panel parent)
        {
            themeButtons.Clear();var note=Text("ภาพตัวอย่างธีม · ตัวเลขในภาพเป็นข้อมูลจำลอง",11);note.Margin=new Thickness(0,0,0,10);note.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");parent.Children.Add(note);
            var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};parent.Children.Add(grid);
            foreach(var theme in ThemeCatalog.All){string key=theme.Key;var button=Button("","เลือกธีม "+theme.Label,()=>{preferences.Theme=key;ApplyPalette(Window);ApplyPalette(settingsWindow);BuildWindow();if(edgeWindow!=null){edgeWindow.Close();edgeWindow=null;}UpdateThemeSelection();Save();});
                var tile=new StackPanel();tile.Children.Add(ThemePreview(theme));tile.Children.Add(Text(theme.Label,12,true));var description=Text(theme.Description,10);description.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");tile.Children.Add(description);
                button.Content=tile;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(7);button.Margin=new Thickness(0,0,8,8);button.ToolTip=theme.Description+" · ภาพตัวอย่าง";themeButtons[key]=button;grid.Children.Add(button);
            }UpdateThemeSelection();
        }
        void UpdateThemeSelection()
        {
            foreach(var pair in themeButtons){bool selected=pair.Key==preferences.Theme;pair.Value.BorderBrush=Brush(selected?Theme.Accent:Theme.Edge);pair.Value.BorderThickness=new Thickness(selected?2:1);AutomationProperties.SetHelpText(pair.Value,selected?"ธีมที่ใช้อยู่":"กดเพื่อเลือกธีม");}
        }
        bool ConnectedForEdge(HubSnapshot data)
        {
            // Receipt of provider data is connection evidence; a configured bridge alone is not login proof.
            return data!=null&&(data.Received.HasValue||data.HeartbeatCurrent&&(data.capability=="NoQuota"||data.capability=="MissingRateLimits"))&&data.state!="LoginRequired"&&data.state!="MissingCli";
        }
        void UpdateEdge()
        {
            if(edgeWindow==null)return;
            var connected=preferences.Monitors.Where(x=>{HubSnapshot data;snapshots.TryGetValue(x.Id,out data);return ConnectedForEdge(data);}).ToList();
            var panel=(StackPanel)((Border)edgeWindow.Content).Child;string key=String.Join(",",connected.Select(x=>x.Id))+"|"+preferences.EdgeShowPercent;
            if(key!=edgeLayoutKey){edgeLayoutKey=key;panel.Children.Clear();edgeValues.Clear();edgeRows.Clear();
                var title=Text(connected.Count==0?"เชื่อมต่อ":"5h",9);title.TextAlignment=TextAlignment.Center;title.SetResourceReference(TextBlock.ForegroundProperty,"SettingsMutedBrush");panel.Children.Add(title);
                foreach(var choice in connected){var row=new Grid{Height=22,Margin=new Thickness(7,0,7,0)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});row.ColumnDefinitions.Add(new ColumnDefinition());var logo=ProviderLogo(choice.Id,14);logo.Margin=new Thickness(0);logo.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(logo);var value=Text("--",preferences.EdgeShowPercent?12:9.5,preferences.EdgeShowPercent);value.TextWrapping=TextWrapping.NoWrap;value.TextAlignment=TextAlignment.Right;Grid.SetColumn(value,1);row.Children.Add(value);edgeValues[choice.Id]=value;edgeRows[choice.Id]=row;panel.Children.Add(row);}
                edgeWindow.Width=preferences.EdgeShowPercent?72:118;edgeWindow.Height=connected.Count==0?40:22+22*connected.Count;
            }
            foreach(var choice in connected){var data=snapshots[choice.Id];var metric=String.Equals(data.primaryLabel,"5h",StringComparison.OrdinalIgnoreCase)?CurrentMetric(data,true):null;bool fresh=Reading(choice,data).Verified;var value=edgeValues[choice.Id];value.Text=preferences.EdgeShowPercent?(metric==null?"--":metric.Display):ServiceLabel(choice.Id);value.Foreground=Brush(!preferences.EdgeShowPercent||!fresh||metric==null?Theme.Muted:Theme.MetricColor(metric.Remaining));
                string help=ServiceLabel(choice.Id)+" · "+choice.Nickname+" · 5h "+(metric==null?"--":metric.Display)+"\n"+Reading(choice,data).Summary+" · "+(data.Received.HasValue?Age(data.Received.Value):"ยังไม่มีข้อมูล")+"\n"+Reset(metric,true);edgeRows[choice.Id].ToolTip=help;AutomationProperties.SetName(edgeRows[choice.Id],help);
            }
            edgeWindow.ToolTip="ชี้เพื่อเปิด Quorilo Monitor · แสดงบริการที่เคยรับข้อมูลจากบัญชี\n"+(connected.Count==0?"เปิดตั้งค่าเพื่อเชื่อมบริการ":String.Join("\n",connected.Select(x=>ServiceLabel(x.Id)+" · "+x.Nickname)));
            // Keep a growing/shrinking tab within the same display and attached to its selected edge.
            if(edgeHidden){var area=WorkArea();bool left=preferences.EdgeSide=="Left"||preferences.EdgeSide=="Nearest"&&expandedLeft+Window.Width/2<area.Left+area.Width/2;edgeWindow.Left=left?area.Left:area.Right-edgeWindow.Width;edgeWindow.Top=Math.Max(area.Top,Math.Min(Window.Top,area.Bottom-edgeWindow.Height));}
        }
    }
}
