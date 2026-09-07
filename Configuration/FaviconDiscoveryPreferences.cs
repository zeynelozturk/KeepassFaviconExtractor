using System;

namespace FaviconExtractor
{
    internal static class FaviconDiscoveryPreferences
    {
        public static readonly int[] PreferredIconSizes = new[] { 64, 32, 16 };
        public static readonly TimeSpan HtmlRequestTimeout = TimeSpan.FromSeconds(6);
    }
}
