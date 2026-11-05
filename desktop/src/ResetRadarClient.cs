using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    internal sealed class ResetRadarItem
    {
        public DateTime At;
        public string Reason;
    }

    internal sealed class ResetRadarSnapshot
    {
        public DateTime FetchedAt;
        public string Origin;
        public string Error;
        public double MeanDays;
        public double Probability24Hours;
        public double Probability72Hours;
        public readonly List<ResetRadarItem> Recent = new List<ResetRadarItem>();
    }

    internal static class ResetRadarClient
    {
        const string Endpoint = "https://reset.quorilo.app/api/radar";

        public static async Task<ResetRadarSnapshot> FetchAsync()
        {
            return await Task.Run(() =>
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(Endpoint);
                request.Method = "GET";
                request.Accept = "application/json";
                request.UserAgent = "Quorilo-Monitor/0.3 ResetRadar";
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Reset Radar is unavailable");
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    var snapshot = ReadMap(root, "snapshot");
                    var result = new ResetRadarSnapshot
                    {
                        Origin = ReadString(snapshot, "origin"),
                        Error = ReadString(snapshot, "error"),
                        FetchedAt = ReadDate(snapshot, "fetchedAt")
                    };
                    var forecast = ReadMap(root, "forecast");
                    result.MeanDays = ReadNumber(forecast, "meanDays");
                    result.Probability24Hours = ReadNumber(forecast, "p24");
                    result.Probability72Hours = ReadNumber(forecast, "p72");
                    object eventValue = null;
                    var events = snapshot != null && snapshot.TryGetValue("events", out eventValue) ? eventValue as ArrayList : null;
                    if (events == null && eventValue is object[]) events = new ArrayList((object[])eventValue);
                    foreach (var row in events ?? new ArrayList())
                    {
                        var item = row as Dictionary<string, object>;
                        if (item == null || ReadString(item, "type") != "reset") continue;
                        var at = ReadDate(item, "at");
                        if (at == DateTime.MinValue) at = ReadDate(item, "ledgerAt");
                        if (at == DateTime.MinValue) continue;
                        result.Recent.Add(new ResetRadarItem { At = at, Reason = ReadString(item, "reason") });
                    }
                    result.Recent.Sort((a, b) => b.At.CompareTo(a.At));
                    if (result.Recent.Count > 5) result.Recent.RemoveRange(5, result.Recent.Count - 5);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        static Dictionary<string, object> ReadMap(IDictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value)) return value as Dictionary<string, object>;
            return null;
        }

        static string ReadString(IDictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }

        static DateTime ReadDate(IDictionary<string, object> map, string key)
        {
            DateTime value;
            return DateTime.TryParse(ReadString(map, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value) ? value : DateTime.MinValue;
        }

        static double ReadNumber(IDictionary<string, object> map, string key)
        {
            object value;
            double result;
            return map != null && map.TryGetValue(key, out value) && Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? result : 0;
        }
    }
}
