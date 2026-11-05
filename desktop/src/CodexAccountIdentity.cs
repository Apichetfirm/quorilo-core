using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CreditMonitor
{
    internal static class CodexAccountIdentity
    {
        public static string Hash(IDictionary<string, object> account)
        {
            if (account == null) return "unknown";
            object value = Field(account, "id") ?? Field(account, "accountId");
            bool emailFallback = value == null;
            if (emailFallback) value = Field(account, "email");
            string identity = value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(identity) || identity.Length > 512) return "unknown";
            identity = emailFallback ? identity.Trim().ToLowerInvariant() : identity.Trim();
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                return BitConverter.ToString(digest, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string Attach(string json, string key)
        {
            if (String.IsNullOrEmpty(json)) return json;
            if (key != "unknown" && !Regex.IsMatch(key ?? "", "^[a-f0-9]{24}$")) key = "unknown";
            try
            {
                var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (result == null) return json;
                result["source_key"] = key;
                return new JavaScriptSerializer().Serialize(result);
            }
            catch { return json; }
        }

        static object Field(IDictionary<string, object> account, string name)
        {
            object value;
            return account.TryGetValue(name, out value) ? value : null;
        }
    }
}
