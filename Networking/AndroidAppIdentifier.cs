using System;

namespace FaviconExtractor.Networking
{
    internal static class AndroidAppIdentifier
    {
        private const string Prefix = "androidapp://";

        public static bool TryGetPackage(string urlValue, string androidAppFieldValue, out string packageName)
        {
            packageName = null;

            if (!string.IsNullOrWhiteSpace(urlValue))
            {
                return TryParse(urlValue, out packageName);
            }

            return TryParse(androidAppFieldValue, out packageName);
        }

        public static bool TryParse(string value, out string packageName)
        {
            packageName = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string candidate = trimmed.Substring(Prefix.Length).Trim();
            if (candidate.Length == 0 || candidate.IndexOfAny(new[] { '/', '\\', ' ', '\t', '\r', '\n' }) >= 0)
            {
                return false;
            }

            packageName = candidate;
            return true;
        }
    }
}
