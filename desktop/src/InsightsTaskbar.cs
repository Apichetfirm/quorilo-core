using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Forms=System.Windows.Forms;
namespace CreditMonitor.Insights {
 // Optional child surface inside Explorer's taskbar. Fail closed when no safe slot exists.
 public sealed class InsightsTaskbar : Forms.Form {
  [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string type,string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string type,string name);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")]static extern IntPtr SetParent(IntPtr hwnd,IntPtr parent);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr hwnd);
  [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hwnd,int index,int value);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  readonly Action open;List<QuotaSample> quotas=new List<QuotaSample>();DateTime positioned=DateTime.MinValue;IntPtr host;bool wanted;
  public string ProviderFilter,WindowFilter;public string Status="Taskbar meter ปิดอยู่";
  protected override bool ShowWithoutActivation{get{return true;}}
  public InsightsTaskbar(Action show){open=show;FormBorderStyle=Forms.FormBorderStyle.None;ShowInTaskbar=false;BackColor=System.Drawing.Color.FromArgb(22,33,58);ForeColor=System.Drawing.Color.White;DoubleBuffered=true;Font=new Font("Segoe UI",8);Click+=(s,e)=>open();}
  public void SetEnabled(bool enabled){if(wanted==enabled)return;wanted=enabled;if(!enabled){Hide();Status="Taskbar meter ปิด/พักอยู่";}else positioned=DateTime.MinValue;}
  public void UpdateReadings(List<ProviderHealth> health){if(IsDisposed)return;quotas=health.Where(h=>h.State=="ready"&&h.At.HasValue&&DateTime.UtcNow-h.At.Value<TimeSpan.FromMinutes(3)).SelectMany(h=>h.Quotas).Where(q=>(String.IsNullOrEmpty(ProviderFilter)||q.Provider==ProviderFilter)&&(String.IsNullOrEmpty(WindowFilter)||q.Window==WindowFilter)).Where(q=>!q.ResetAt.HasValue||q.ResetAt>DateTime.UtcNow).OrderBy(q=>q.Remaining).Take(2).ToList();if(!wanted)return;if(DateTime.UtcNow-positioned>TimeSpan.FromSeconds(10)||!IsWindow(host)){positioned=DateTime.UtcNow;Place();}Invalidate();}
  void Place(){try{var bar=FindWindow("Shell_TrayWnd",null);Rect bounds;if(bar==IntPtr.Zero||!IsWindowVisible(bar)||!GetWindowRect(bar,out bounds)||bounds.Right-bounds.Left<bounds.Bottom-bounds.Top){Hide();Status="ยังไม่รองรับ taskbar ตำแหน่งนี้";return;}int height=bounds.Bottom-bounds.Top;if(height<20){Hide();return;}Rect tray;var notification=FindWindowEx(bar,IntPtr.Zero,"TrayNotifyWnd",null);if(notification==IntPtr.Zero||!GetWindowRect(notification,out tray)){Hide();Status="หา notification area ไม่พบ";return;}int width=280;var occupied=new List<System.Windows.Rect>();var element=AutomationElement.FromHandle(bar);var buttons=element.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem)));foreach(AutomationElement button in buttons){var rect=button.Current.BoundingRectangle;if(!rect.IsEmpty&&rect.Width>0&&rect.Height>0)occupied.Add(rect);}int found=-1;for(int x=tray.Left-width-8;x>bounds.Left+60;x-=20){var candidate=new System.Windows.Rect(x,bounds.Top+2,width,height-4);if(!occupied.Any(r=>r.IntersectsWith(candidate))){found=x;break;}}if(found<0){Hide();Status="ไม่มีพื้นที่ว่างบน taskbar · ลองย่อรายการแอป";return;}
   if(!Visible)Show();SetWindowLong(Handle,-16,(GetWindowLong(Handle,-16)&unchecked((int)~0x80000000))|0x40000000);SetParent(Handle,bar);if(GetParent(Handle)!=bar){Hide();Status="Explorer ไม่รับ taskbar surface";return;}host=bar;SetBounds(found-bounds.Left,2,width,height-4);Status="แสดงใน Windows taskbar · คลิกเปิด Insights";
  }catch{Hide();Status="Taskbar meter ยังใช้ไม่ได้บน shell นี้";}}
  static string Reset(DateTime? at){if(!at.HasValue)return "reset ?";var left=at.Value-DateTime.UtcNow;return left.TotalMinutes<=0?"reset…":left.TotalDays>=1?((int)left.TotalDays)+"d "+left.Hours+"h":((int)left.TotalHours)+"h "+left.Minutes+"m";}
  protected override void OnPaint(Forms.PaintEventArgs e){base.OnPaint(e);if(quotas.Count==0){e.Graphics.DrawString("Quorilo · รอค่าใหม่",Font,Brushes.White,5,5);return;}for(int i=0;i<quotas.Count;i++){var q=quotas[i];float y=i*Height/2f;e.Graphics.DrawString(q.Provider+" "+q.Window,Font,Brushes.White,4,y);float x=99,w=58;using(var track=new SolidBrush(System.Drawing.Color.FromArgb(72,83,111)))e.Graphics.FillRectangle(track,x,y+7,w,5);using(var fill=new SolidBrush(q.Remaining<20?System.Drawing.Color.OrangeRed:System.Drawing.Color.Turquoise))e.Graphics.FillRectangle(fill,x,y+7,(float)(w*q.Remaining/100),5);e.Graphics.DrawString(q.Remaining.ToString("0")+"% · "+Reset(q.ResetAt),Font,Brushes.White,160,y);}}
 }
}
