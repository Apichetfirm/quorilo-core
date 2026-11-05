using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CreditMonitor
{
    public sealed partial class Widget
    {
        private LicenseClient license;
        private CommerceConfiguration commerce;
        private readonly DispatcherTimer licenseTimer = new DispatcherTimer();
        private TextBlock licenseSummary, licenseDetail, updateSummary;
        private UpdateManifest availableUpdate;
        private DateTime nextUpdateCheck = DateTime.MinValue;
        private bool checkingUpdate;
        private StackPanel licenseDevices;
        private UsageData pendingCommercialData;
        private bool licensingBusy;
        private bool CommercialGate { get { return Product.Commercial && !verification; } }
        private bool CommercialAllowed { get { return Product.LocalCoreAccess || !CommercialGate || (license != null && license.Allowed && (Product.AccountSlot.Length==0||license.MultipleAccounts)); } }
        private void InitializeCommerce()
        {
            commerce = CommerceConfiguration.Load();
            if (verification || !Product.Commercial) return;
            try { license = new LicenseClient(commerce, LicenseClient.Storage, LicenseClient.DeviceIdentity()); }
            catch { Log("license", "VaultUnavailable"); }
            licenseTimer.Interval = Product.AccountLicensed ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15);
            licenseTimer.Tick += async (s, e) => { EnforceTrialExpiry(); await CheckLicenseAsync(false); };
            licenseTimer.Start();
            if(Product.AccountLicensed)Microsoft.Win32.SystemEvents.PowerModeChanged+=TrialPowerChanged;
        }
        private void TrialPowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e){if(license==null||exiting)return;license.SuspendTrial();Window.Dispatcher.BeginInvoke(new Action(async()=>{if(watcher!=null){watcher.Dispose();watcher=null;}if(e.Mode==Microsoft.Win32.PowerModes.Resume){await CheckLicenseAsync(false,true);if(CommercialAllowed)await ConnectAsync();}}));}
        private void EnforceTrialExpiry(){if(!Product.AccountLicensed||CommercialAllowed||licensingBusy)return;if(watcher!=null){watcher.Dispose();watcher=null;}pendingCommercialData=null;latestData=null;lastRead=null;ApplyMetric("Current",null);ApplyMetric("Weekly",null);SetState("LicenseRequired",LicenseClient.Message(license==null?"GoogleSignInRequired":license.LastError??"TrialExpired"));}
        private bool CanStartCommercialReader()
        {
            if (!CommercialGate || (license != null && (Product.AccountLicensed?license.Allowed:license.Configured))) return true;
            string code = license == null || !license.Configured ? "NotConfigured" : (license.LastError ?? (Product.AccountLicensed ? "OnlineValidationRequired" : "NotConfigured"));
            SetState("LicenseRequired", LicenseClient.Message(code));
            return false;
        }
        private bool AcceptCommercialData(UsageData data)
        {
            if (CommercialAllowed) return true;
            pendingCommercialData = data;
            ApplyMetric("Current", null); ApplyMetric("Weekly", null);
            SetState("LicenseRequired", "เปิดตั้งค่า → สิทธิ์ใช้งาน เพื่อทดลองหรือเปิดใช้คีย์");
            if (data.HasQuota && !data.ResetPending && (DateTime.Now - data.ReadAt).TotalSeconds <= 90) BeginLicenseCheck();
            return false;
        }
        private async void BeginLicenseCheck() { await CheckLicenseAsync(true); }
        private async Task CheckLicenseAsync(bool connected, bool force = false)
        {
            if (verification || license == null || licensingBusy || exiting) return;
            licensingBusy = true;
            try {
                await license.RefreshAsync(connected, force);
                if (exiting) return;
                if(Product.AccountLicensed&&!CommercialAllowed&&watcher!=null){watcher.Dispose();watcher=null;pendingCommercialData=null;latestData=null;lastRead=null;ApplyMetric("Current",null);ApplyMetric("Weekly",null);SetState("LicenseRequired",LicenseClient.Message(license.LastError??"TrialExpired"));}
                if (CommercialAllowed && pendingCommercialData != null) { var data = pendingCommercialData; pendingCommercialData = null; ApplyData(data); }
                else if (!CommercialAllowed && lastRead.HasValue) { latestData = null; lastRead = null; ApplyMetric("Current", null); ApplyMetric("Weekly", null); SetState("LicenseRequired", LicenseClient.Message(license.LastError ?? "TrialExpired")); }
                UpdateLicenseSummary();
                if (CommercialAllowed && watcher == null && !connecting && !preferences.MonitoringPaused) {
                    await ConnectAsync();
                }
                if (CommercialAllowed) await DiscoverUpdateAsync();
            } finally { licensingBusy = false; }
        }
        private void UpdateLicenseSummary()
        {
            if (licenseSummary == null) return;
            if (verification && commerce.Ready && (license == null || license.Grant == null)) { licenseSummary.Text = commerce.Beta ? "ตัวอย่างหน้าสิทธิ์ทดลอง" : "ตัวอย่างหน้าสิทธิ์ใช้งาน"; licenseDetail.Text = commerce.Beta ? "ใช้รหัสเชิญของคุณ · วันเริ่มและวันหมดอายุแสดงเมื่อเปิดใช้สำเร็จ" : "คีย์อยู่ในอีเมลยืนยันการซื้อ"; return; }
            if (license == null || !license.Configured) { licenseSummary.Text = "ยังไม่เปิดขาย"; licenseDetail.Text = "รุ่นนี้ยังไม่เปิดระบบไลเซนส์ · ช่วงทดลองยังไม่เริ่ม"; return; }
            var grant = license.Grant;
            licenseSummary.Text = grant == null ? (commerce.Beta ? "เปิดใช้รหัสทดลอง" : "เริ่มทดลองเมื่อได้รับเครดิตครั้งแรก") : grant.kind == "trial" || grant.kind == "beta" ? "ทดลองใช้งาน · เหลือ " + Math.Max(0, (int)Math.Ceiling((license.TrialEndsAt.Value - DateTime.UtcNow).TotalDays)) + " วัน" : "ซื้อครั้งเดียว · ผู้ใช้หนึ่งคน / 2 เครื่อง";
            licenseSummary.SetResourceReference(TextBlock.ForegroundProperty, license.Allowed ? "SettingsGoodBrush" : "SettingsWarningBrush");
            licenseDetail.Text = grant == null ? "ติดตั้งและเชื่อมต่อบริการ AI ก่อน · ช่วงทดลอง 14 วันจะเริ่มเมื่ออ่านเครดิตได้" : "เชื่อมต่อเพื่อตรวจสิทธิ์ภายใน " + license.ValidUntil.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") + (grant.kind == "paid" ? "\nรับอัปเดตถึง " + DateTime.Parse(grant.updatesUntil).ToLocalTime().ToString("dd/MM/yyyy") + " · " + license.KeyHint : "");
            if (grant != null && license.StartedAt.HasValue) licenseDetail.Text = "เริ่ม " + LicenseTime(license.StartedAt.Value) + "\n" + (license.TrialEndsAt.HasValue ? "หมดอายุ " + LicenseTime(license.TrialEndsAt.Value) + "\n" : "") + "สิทธิ์ออฟไลน์ถึง " + LicenseTime(license.ValidUntil.Value) + (grant.kind == "paid" ? "\nรับอัปเดตถึง " + LicenseTime(DateTime.Parse(grant.updatesUntil)) : "");
            if (!String.IsNullOrEmpty(license.LastError)) licenseDetail.Text += "\n" + LicenseClient.Message(license.LastError);
        }
        private string LicenseTime(DateTime value) { return value.ToLocalTime().ToString(preferences.TimeFormat == "12H" ? "dd/MM/yyyy hh:mm:ss tt" : "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture); }
        private void AddCommerceSettings(TabControl tabs)
        {
            var page = Page(tabs, "สิทธิ์ใช้งาน"); PageTitle(page, "สิทธิ์ใช้งาน", commerce.Beta ? "รหัสเดียว · ทดลอง 14 วัน · ใช้วิดเจ็ตร่วมกันได้ 2 เครื่อง" : "คีย์เดียว · ใช้วิดเจ็ตร่วมกันได้สูงสุด 2 เครื่อง");
            var status = Section(page, "สิทธิ์ของคุณ"); licenseSummary = Text(status, "", true); licenseDetail = Text(status, "");
            bool configured = commerce != null && commerce.Ready;
            var input = new PasswordBox { Name = "LicenseKeyInput", FontSize = 14, Padding = new Thickness(12), MinHeight = 44, Margin = new Thickness(0, 0, 0, 10), MaxLength = 160, IsEnabled = configured };
            System.Windows.Automation.AutomationProperties.SetName(input, commerce.Beta ? "รหัสเชิญทดลองของคุณ" : "คีย์จากอีเมลยืนยันการซื้อ"); status.Children.Add(input);
            var actions = Actions(status); Button activate = null;
            activate = ActionButton(actions, "เปิดใช้คีย์", async () => {
                if (verification || license == null) return;
                activate.IsEnabled = false;
                try { var fresh = pendingCommercialData ?? latestData; await license.ActivateAsync(input.Password, fresh != null && fresh.HasQuota && !fresh.ResetPending && (DateTime.Now - fresh.ReadAt).TotalSeconds <= 90); input.Clear(); await CheckLicenseAsync(false, true); await RefreshAsync(); }
                catch (LicenseFailure error) { MessageBox.Show(settingsWindow, LicenseClient.Message(error.Code), "เปิดใช้คีย์"); }
                catch { MessageBox.Show(settingsWindow, "เปิดใช้คีย์ไม่สำเร็จ · ลองใหม่อีกครั้ง", "เปิดใช้คีย์"); }
                finally { activate.IsEnabled = configured; UpdateLicenseSummary(); }
            }); activate.IsEnabled = configured; Primary(activate);
            var purchase = ActionButton(actions, "ซื้อไลเซนส์", () => OpenCommerceLink(commerce.CheckoutUrl)); purchase.IsEnabled = configured && SafeCommerceUrl(commerce.CheckoutUrl);
            if (commerce.Beta) { activate.Content = new TextBlock { Text = "เปิดใช้ / เปิดเครื่องกลับ" }; purchase.Visibility = Visibility.Collapsed; }
            var manage = More(page, "จัดการเครื่องที่ใช้สิทธิ์");
            Text(manage, "คืนช่องเครื่องเก่าเพื่อย้ายเครื่อง · เปิดเครื่องเดิมกลับได้เมื่อมีช่องว่างและสิทธิ์ยังไม่หมด วันเริ่มและวันหมดอายุเดิมคงอยู่");
            var management = Actions(manage);
            var list = ActionButton(management, "ดูเครื่องที่เปิดใช้", async () => await LoadLicenseDevices()); list.IsEnabled = configured;
            var recheck = ActionButton(management, "ตรวจสิทธิ์", async () => await CheckLicenseAsync(pendingCommercialData != null && pendingCommercialData.HasQuota, true)); recheck.IsEnabled = configured;
            var restore = ActionButton(management, "เปิดเครื่องนี้กลับ", async () => {
                try { if (license == null) return; await license.ReactivateAsync(pendingCommercialData != null && pendingCommercialData.HasQuota); await CheckLicenseAsync(false, true); await RefreshAsync(); await LoadLicenseDevices(); }
                catch (LicenseFailure error) { if (settingsWindow != null) MessageBox.Show(settingsWindow, LicenseClient.Message(error.Code)); }
                catch { if (settingsWindow != null) MessageBox.Show(settingsWindow, "เปิดเครื่องกลับไม่ได้ · ลองใหม่อีกครั้ง"); }
            }); restore.IsEnabled = configured;
            licenseDevices = new StackPanel(); manage.Children.Add(licenseDevices);
            var updates = More(page, "อัปเดตและความช่วยเหลือ");
            updateSummary = Text(updates, "ตรวจอัปเดตอัตโนมัติทุกวัน · รับรุ่นที่ออกใน 12 เดือนแรกหลังซื้อ");
            var updateActions = Actions(updates); var update = ActionButton(updateActions, "ตรวจอัปเดต", async () => await CheckCommerceUpdate()); update.IsEnabled = configured;
            var support = ActionButton(updateActions, "ศูนย์ช่วยเหลือ", () => OpenCommerceLink(commerce.SupportUrl)); support.IsEnabled = SafeCommerceUrl(commerce.SupportUrl);
            var feedback = ActionButton(status, "ส่ง feedback", () => OpenCommerceLink(commerce.FeedbackUrl)); feedback.IsEnabled = SafeCommerceUrl(commerce.FeedbackUrl);
            if (commerce.Beta) { ((FrameworkElement)updates.Parent).Visibility = Visibility.Collapsed; }
            if (!configured) { input.Visibility = Visibility.Collapsed; actions.Visibility = Visibility.Collapsed; ((FrameworkElement)manage.Parent).Visibility = Visibility.Collapsed; ((FrameworkElement)updates.Parent).Visibility = Visibility.Collapsed; }
            UpdateLicenseSummary();
        }
        private static bool SafeCommerceUrl(string url) { Uri parsed; return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == "https" && String.IsNullOrEmpty(parsed.UserInfo); }
        private void OpenCommerceLink(string url) { if (!verification && SafeCommerceUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        private async Task LoadLicenseDevices()
        {
            if (verification || license == null || licenseDevices == null) return;
            try {
                var devices = await license.DevicesAsync(); if (settingsWindow == null || licenseDevices == null) return;
                licenseDevices.Children.Clear();
                if (devices.Length == 0) Text(licenseDevices, "ยังไม่มีเครื่องที่เปิดใช้คีย์นี้");
                foreach (var entry in devices) {
                    var row = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; licenseDevices.Children.Add(row);
                    Text(row, entry.name + (entry.current ? " · เครื่องนี้" : ""));
                    ActionButton(row, "คืนช่องเครื่องนี้", async () => {
                        if (MessageBox.Show(settingsWindow, "ยกเลิกสิทธิ์ " + entry.name + " เพื่อคืนช่องเครื่อง?", "ย้ายเครื่อง", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        try { await license.DeactivateAsync(entry.instance); await CheckLicenseAsync(false, true); await LoadLicenseDevices(); }
                        catch { if (settingsWindow != null) MessageBox.Show(settingsWindow, "ยกเลิกไม่สำเร็จ · เชื่อมต่ออินเทอร์เน็ตแล้วลองใหม่"); }
                    });
                }
            } catch (LicenseFailure error) { if (settingsWindow != null) MessageBox.Show(settingsWindow, LicenseClient.Message(error.Code)); }
            catch { if (settingsWindow != null) MessageBox.Show(settingsWindow, "โหลดรายชื่อเครื่องไม่ได้ · ลองใหม่อีกครั้ง"); }
        }
        private async Task DiscoverUpdateAsync()
        {
            if (verification || commerce.Beta || checkingUpdate || license == null || !license.Allowed || !license.HasKey || DateTime.UtcNow < nextUpdateCheck) return;
            checkingUpdate = true; nextUpdateCheck = DateTime.UtcNow.AddHours(24);
            try {
                var update = await license.CheckUpdateAsync();
                if (new Version(update.version) > new Version(Product.AssemblyVersion)) {
                    availableUpdate = update;
                    if (updateSummary != null) updateSummary.Text = "มีรุ่น " + update.version + " พร้อมอัปเดต · กดตรวจอัปเดตเพื่อติดตั้ง";
                    if (tray != null) tray.ShowBalloonTip(6000, Product.Label, "มีรุ่น " + update.version + " พร้อมอัปเดต · เปิดตั้งค่า → สิทธิ์ใช้งาน", System.Windows.Forms.ToolTipIcon.Info);
                }
            } catch { /* Background discovery stays quiet; the explicit update button shows actionable errors. */ }
            finally { checkingUpdate = false; }
        }
        private async Task CheckCommerceUpdate()
        {
            if (verification || license == null) return;
            try {
                var update = await license.CheckUpdateAsync();
                if (new Version(update.version) <= new Version(Product.AssemblyVersion)) { MessageBox.Show(settingsWindow, "คุณใช้รุ่นล่าสุดแล้ว"); return; }
                if (MessageBox.Show(settingsWindow, "พบรุ่น " + update.version + "\nดาวน์โหลดตัวติดตั้งที่ตรวจลายเซ็นและ checksum แล้ว?", "อัปเดต", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                string installer = await VerifiedUpdate.DownloadAsync(update,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Product.StorageName,"VerifiedUpdates"),System.Threading.CancellationToken.None);
                if(!license.Allowed)throw new LicenseFailure("PurchaseRequired");
                if (MessageBox.Show(settingsWindow, "ดาวน์โหลดและตรวจไฟล์แล้ว · เริ่มตัวติดตั้งรุ่น " + update.version + "?", "อัปเดต", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            } catch (LicenseFailure error) { if (settingsWindow != null) MessageBox.Show(settingsWindow, LicenseClient.Message(error.Code), "อัปเดต"); }
            catch { if (settingsWindow != null) MessageBox.Show(settingsWindow, "ดาวน์โหลดหรือตรวจไฟล์อัปเดตไม่สำเร็จ · เก็บรุ่นเดิมไว้", "อัปเดต"); }
        }
    }
}
