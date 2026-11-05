using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    public sealed class UpdateManifest
    {
        public int schema;
        public long size;
        public string product, flavor, platform, device, version, publishedAt, issuedAt, expiresAt, sha256, url;
    }
    public static class VerifiedUpdate
    {
        static DateTime Time(string value) { return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal); }
        public static UpdateManifest Verify(string reply, string publicKey, string device, string currentVersion, DateTime updatesUntil, DateTime now, string flavor = "Unified")
        {
            try {
                if (String.IsNullOrEmpty(reply) || reply.Length > 32768) throw new InvalidDataException();
                var envelope = new JavaScriptSerializer().Deserialize<LicenseEnvelope>(reply);
                if (envelope == null || envelope.payload == null || envelope.signature == null || envelope.payload.Length > 16384 || envelope.signature.Length > 1024) throw new InvalidDataException();
                byte[] bytes = Convert.FromBase64String(envelope.payload);
                using (var rsa = new RSACryptoServiceProvider()) {
                    rsa.PersistKeyInCsp = false; rsa.FromXmlString(publicKey);
                    if (rsa.KeySize < 2048 || !rsa.VerifyData(bytes, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(envelope.signature))) throw new InvalidDataException();
                }
                var item = new JavaScriptSerializer().Deserialize<UpdateManifest>(Encoding.UTF8.GetString(bytes));
                Version target, current; Uri uri;
                if (item == null || item.schema != 1 || item.product != "quota-monitor-suite-update" || item.flavor != flavor || item.platform != "windows-x64" || item.device != device ||
                    !Version.TryParse(item.version, out target) || !Version.TryParse(currentVersion.Split('-')[0], out current) || target <= current || target.Build < 0 || target.Revision >= 0 ||
                    item.size <= 0 || item.size > 200L * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(item.sha256 ?? "", "^[a-fA-F0-9]{64}$") ||
                    !Uri.TryCreate(item.url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment) ||
                    Time(item.publishedAt) > updatesUntil || Time(item.publishedAt) > now.AddMinutes(1) || Time(item.issuedAt) > now.AddMinutes(1) || Time(item.expiresAt) <= now ||
                    Time(item.expiresAt) <= Time(item.issuedAt) || Time(item.expiresAt) - Time(item.issuedAt) > TimeSpan.FromMinutes(15)) throw new InvalidDataException();
                return item;
            } catch { throw new LicenseFailure("InvalidUpdate"); }
        }
        // Never execute a download. A verified installer is presented for the user's explicit installation.
        public static async Task<string> DownloadAsync(UpdateManifest item, string destination, CancellationToken cancellation)
        {
            Uri address;
            if(item==null||!System.Text.RegularExpressions.Regex.IsMatch(item.version??"","^\\d+\\.\\d+\\.\\d+$")||item.size<=0||item.size>200L*1024*1024||
                !System.Text.RegularExpressions.Regex.IsMatch(item.sha256??"","^[a-fA-F0-9]{64}$")||
                !Uri.TryCreate(item.url,UriKind.Absolute,out address)||address.Scheme!="https"||!String.IsNullOrEmpty(address.UserInfo)||!String.IsNullOrEmpty(address.Fragment))throw new LicenseFailure("InvalidUpdate");
            cancellation.ThrowIfCancellationRequested();
            var folder = Path.GetFullPath(destination); Directory.CreateDirectory(folder);
            string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".part");
            string target = Path.Combine(folder, "Credit-Monitor-Setup-" + item.version + ".exe");
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(item.url);
                request.Method = "GET"; request.AllowAutoRedirect = false; request.Timeout = 60000; request.ReadWriteTimeout = 60000;
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                    deadline.CancelAfter(TimeSpan.FromMinutes(3));
                    using (deadline.Token.Register(request.Abort)) using (var response = (HttpWebResponse)await request.GetResponseAsync()) {
                        if (response.StatusCode != HttpStatusCode.OK || (response.ContentLength >= 0 && response.ContentLength != item.size)) throw new InvalidDataException();
                        using (var input = response.GetResponseStream()) using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                            byte[] buffer = new byte[65536]; int count; long length = 0;
                            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token)) > 0) {
                                length += count; if (length > item.size) throw new InvalidDataException();
                                await output.WriteAsync(buffer, 0, count, deadline.Token);
                            }
                            if (length != item.size) throw new InvalidDataException();
                        }
                    }
                }
                using (var hash = SHA256.Create()) using (var input = File.OpenRead(temporary)) {
                    if (!String.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""), item.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                }
                cancellation.ThrowIfCancellationRequested();
                await Task.Run(()=>PublisherTrust.RequireSamePublisher(temporary,System.Reflection.Assembly.GetExecutingAssembly().Location),cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                return target;
            } catch(LicenseFailure){throw;}
            catch(OperationCanceledException){throw;}
            catch { if(cancellation.IsCancellationRequested)throw new OperationCanceledException(cancellation);throw new LicenseFailure("InvalidUpdate"); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
