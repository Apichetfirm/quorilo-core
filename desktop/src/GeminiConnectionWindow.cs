#if GEMINI
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace CreditMonitor
{
    // The native WebView occupies only the viewport, below an application-owned guidance bar.
    sealed class GeminiConnectionWindow : Window
    {
        Border viewport; TextBlock guidance; CoreWebView2Controller controller;
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
        public GeminiConnectionWindow(Action openUsage,Action reload,Action hide)
        {
            Title="เชื่อมบัญชี Gemini · Quorilo Monitor";Width=1000;Height=800;MinWidth=680;MinHeight=540;
            ShowInTaskbar=false;ShowActivated=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=new SolidColorBrush(Color.FromRgb(16,25,37));Foreground=Brushes.White;FontFamily=new FontFamily("Noto Sans Thai, Segoe UI");
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());
            var top=new StackPanel {Margin=new Thickness(18,12,18,12)};
            guidance=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=14,LineHeight=22,Margin=new Thickness(0,0,0,8)};top.Children.Add(guidance);
            var actions=new WrapPanel();top.Children.Add(actions);
            AddButton(actions,"เปิดหน้าเครดิต Gemini",openUsage);AddButton(actions,"ลองโหลดหน้านี้ใหม่",reload);AddButton(actions,"ซ่อนหน้าต่าง",hide);
            layout.Children.Add(top);viewport=new Border();Grid.SetRow(viewport,1);layout.Children.Add(viewport);Content=layout;
            SizeChanged+=(s,e)=>UpdateBounds();viewport.SizeChanged+=(s,e)=>UpdateBounds();
            LocationChanged+=(s,e)=>{if(controller!=null){UpdateBounds();controller.NotifyParentWindowPositionChanged();}};
            Activated+=(s,e)=>{if(controller!=null && controller.IsVisible){UpdateBounds();controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);}};
            StateChanged+=(s,e)=>{if(controller!=null)controller.IsVisible=IsVisible && WindowState!=WindowState.Minimized;};
            ShowStatus("WebLoginRequired");
        }
        static void AddButton(Panel parent,string text,Action action)
        {
            var button=new Button {Content=text,FontSize=13,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};button.Click+=(s,e)=>action();parent.Children.Add(button);
        }
        public void Attach(CoreWebView2Controller value){controller=value;UpdateBounds();}
        public void Detach(){controller=null;}
        public void UpdateBounds()
        {
            if(controller==null)return;
            IntPtr hwnd=new WindowInteropHelper(this).Handle;NativeRect client;if(hwnd==IntPtr.Zero || !GetClientRect(hwnd,out client))return;
            int top=120;
            if(IsVisible && viewport.IsLoaded && viewport.ActualHeight>0){var origin=new NativePoint();if(ClientToScreen(hwnd,ref origin)){var point=viewport.PointToScreen(new Point(0,0));top=Math.Max(0,(int)Math.Round(point.Y-origin.Y));}}
            else {var presentation=PresentationSource.FromVisual(this);if(presentation!=null)top=(int)Math.Round(120*presentation.CompositionTarget.TransformToDevice.M22);}
            top=Math.Min(top,Math.Max(0,client.Bottom-1));
            controller.Bounds=new System.Drawing.Rectangle(0,top,Math.Max(1,client.Right),Math.Max(1,client.Bottom-top));
        }
        public void ShowStatus(string error)
        {
            guidance.Text=error==null ? "เชื่อมแล้ว · อ่านเครดิต 5h / Weekly เบื้องหลัง หน้าต่างนี้จะซ่อนอัตโนมัติ"
                : error=="WebUsageWaiting" ? "กำลังอ่านหน้า Usage · หน้าต่างจะซ่อนเมื่อรับเครดิตจริงแล้ว"
                : error=="WebUsageUnavailable" ? "ยังไม่พบเครดิตบนหน้า Usage · ลองโหลดใหม่ หากหน้าเว็บเปลี่ยน รุ่นนี้อาจยังอ่านไม่ได้"
                : error=="WebNavigationBlocked" ? "Google พาไปนอกหน้าเชื่อมที่รองรับ · กดเปิดหน้าเครดิต Gemini เพื่อลองต่อ"
                : error=="WebSignInUnsupported" ? "Google ไม่อนุญาตการลงชื่อเข้าใช้ในหน้าต่างนี้ · ส่ง feedback ให้ผู้จัดทดลอง พร้อมชื่อสถานะนี้"
                : error=="WebNavigationSlow" || error=="WebReadUnavailable" ? "หน้าเว็บโหลดไม่สำเร็จหรือใช้เวลานาน · ลองโหลดหน้านี้ใหม่ หากยังไม่ได้ให้ส่ง feedback"
                : "ดำเนินการ Google ให้เสร็จ · หากเห็น Sign in faster ให้กด Not now ถ้ายังไม่ต้องการสร้าง passkey แล้วรอหน้า Gemini Usage";
        }
    }
}
#endif
