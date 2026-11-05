#if !TRIAL
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    public sealed class UsageHistoryPoint
    {
        public string provider;
        public string account;
        public string window;
        public double remaining;
        public DateTime resetAtUtc;
        public DateTime observedAtUtc;
    }

    internal sealed class UsageHistoryDocument
    {
        public int schema = 1;
        public List<UsageHistoryPoint> points = new List<UsageHistoryPoint>();
    }

    public sealed class UsageHistory
    {
        public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
        const int MaximumPoints = 20000;
        const int MaximumFileBytes = 2 * 1024 * 1024;
        readonly string path;
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
        List<UsageHistoryPoint> points;

        public UsageHistory(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("History path is required", "path");
            this.path = Path.GetFullPath(path);
            points = Load();
        }

        List<UsageHistoryPoint> Load()
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= 0 || file.Length > MaximumFileBytes) return new List<UsageHistoryPoint>();
                var document = serializer.Deserialize<UsageHistoryDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.schema != 1 || document.points == null || document.points.Count > MaximumPoints) return new List<UsageHistoryPoint>();
                return document.points.Where(ValidPoint).OrderBy(x => x.observedAtUtc).ToList();
            }
            catch { return new List<UsageHistoryPoint>(); }
        }

        static bool ValidPoint(UsageHistoryPoint point)
        {
            return point != null && !String.IsNullOrWhiteSpace(point.provider) && point.provider.Length <= 48 &&
                !String.IsNullOrWhiteSpace(point.account) && point.account.Length == 64 &&
                !String.IsNullOrWhiteSpace(point.window) && point.window.Length <= 32 &&
                !Double.IsNaN(point.remaining) && !Double.IsInfinity(point.remaining) && point.remaining >= 0 && point.remaining <= 100 &&
                point.observedAtUtc.Kind == DateTimeKind.Utc && point.resetAtUtc.Kind == DateTimeKind.Utc && point.resetAtUtc > point.observedAtUtc;
        }

        public bool Record(string provider, string accountIdentity, string window, double remaining, DateTime resetAtUtc, DateTime observedAtUtc, bool fresh)
        {
            if (!fresh || String.IsNullOrWhiteSpace(provider) || provider.Length > 48 || String.IsNullOrWhiteSpace(window) || window.Length > 32 ||
                String.IsNullOrWhiteSpace(accountIdentity) || Double.IsNaN(remaining) || Double.IsInfinity(remaining) || remaining < 0 || remaining > 100) return false;
            if (observedAtUtc.Kind != DateTimeKind.Utc || resetAtUtc.Kind != DateTimeKind.Utc || resetAtUtc <= observedAtUtc) return false;
            var accountHash = HashIdentity(accountIdentity);
            var previous = points.Where(x => x.provider == provider && x.account == accountHash && x.window == window)
                .OrderByDescending(x => x.observedAtUtc).FirstOrDefault();
            if (previous != null && observedAtUtc - previous.observedAtUtc < SampleInterval) return false;

            points.Add(new UsageHistoryPoint { provider = provider, account = accountHash, window = window, remaining = remaining, resetAtUtc = resetAtUtc, observedAtUtc = observedAtUtc });
            points = points.Where(x => observedAtUtc - x.observedAtUtc <= Retention)
                .OrderBy(x => x.observedAtUtc).Skip(Math.Max(0, points.Count - MaximumPoints)).ToList();
            Save();
            return true;
        }

        public List<List<UsageHistoryPoint>> GetSegments(string provider, string accountIdentity, string window, DateTime nowUtc)
        {
            if (String.IsNullOrWhiteSpace(provider) || String.IsNullOrWhiteSpace(accountIdentity) || String.IsNullOrWhiteSpace(window) || nowUtc.Kind != DateTimeKind.Utc)
                return new List<List<UsageHistoryPoint>>();
            var selected = points.Where(x => x.provider == provider && x.account == HashIdentity(accountIdentity) && x.window == window &&
                nowUtc - x.observedAtUtc <= Retention && x.observedAtUtc <= nowUtc).OrderBy(x => x.observedAtUtc).ToList();
            var result = new List<List<UsageHistoryPoint>>();
            foreach (var point in selected)
            {
                if (result.Count == 0 || result[result.Count - 1].Last().resetAtUtc != point.resetAtUtc ||
                    point.observedAtUtc - result[result.Count - 1].Last().observedAtUtc > TimeSpan.FromMinutes(15))
                    result.Add(new List<UsageHistoryPoint>());
                result[result.Count - 1].Add(point);
            }
            return result;
        }

        public RunwayEstimate EstimateRunway(string provider, string accountIdentity, string window, DateTime nowUtc, bool experimental = false)
        {
            if (!experimental) return new RunwayEstimate();
            // Use the existing retained, account-isolated samples. No additional
            // collection, identifiers, storage schema, or entitlement is introduced.
            return QuotaRunway.Estimate(GetSegments(provider, accountIdentity, window, nowUtc)
                .SelectMany(x => x).Select(x => new RunwayObservation {
                    AtUtc = x.observedAtUtc, ResetAtUtc = x.resetAtUtc, Remaining = x.remaining
                }), nowUtc, true);
        }

        public RunwayValidation BacktestRunway(string provider, string accountIdentity, string window, DateTime nowUtc, bool experimental = false)
        {
            if (!experimental) return new RunwayValidation();
            return QuotaRunway.Backtest(GetSegments(provider, accountIdentity, window, nowUtc)
                .SelectMany(x => x).Select(x => new RunwayObservation {
                    AtUtc = x.observedAtUtc, ResetAtUtc = x.resetAtUtc, Remaining = x.remaining
                }), nowUtc, true);
        }

        void Save()
        {
            var folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            string temporary = path + ".tmp";
            try
            {
                string json;
                do
                {
                    json = serializer.Serialize(new UsageHistoryDocument { points = points });
                    if (Encoding.UTF8.GetByteCount(json) <= MaximumFileBytes) break;
                    int drop = Math.Max(1, points.Count / 10);
                    points = points.OrderBy(x => x.observedAtUtc).Skip(drop).ToList();
                } while (points.Count > 0);
                if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) throw new IOException("Quota history exceeded its storage limit");
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
        }

        public void Clear()
        {
            points.Clear();
            if (File.Exists(path)) File.Delete(path);
            string temporary = path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        static string HashIdentity(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

    }
}

#endif
