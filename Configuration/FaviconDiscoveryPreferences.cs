using System;
using System.Collections.Generic;

namespace FaviconExtractor
{
    internal static class FaviconDiscoveryPreferences
    {
        public static readonly int[] PreferredIconSizes = new[] { 64, 32, 16 };
        public static readonly TimeSpan TotalDiscoveryTimeout = TimeSpan.FromSeconds(12);
        public static readonly TimeSpan HtmlRequestTimeout = TimeSpan.FromSeconds(4);
        public static readonly TimeSpan FallbackProbeTimeout = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan IconDownloadTimeout = TimeSpan.FromSeconds(8);
        public const int MaxAutomaticRedirects = 6;
        public const int MaxFallbackRequestsPerLookup = 6;
        public const int MaxFallbackRequestsWhenBlocked = 3;
        public const int MaxIconDownloadBytes = 1024 * 1024;
        public const int FallbackScorePenalty = 150;
        public const int ExternalServiceScorePenalty = 190;
        public const int LogoScorePenalty = 120;
        public const int NormalizedIconSize = 64;
        public const bool UpscaleSmallImagesDuringNormalization = false;

        public static readonly IReadOnlyList<string> WellKnownFaviconPaths = new[]
        {
            "/favicon.ico",
            "/favicon.png",
            "/favicon.svg",
            "/apple-touch-icon.png",
            "/apple-touch-icon-precomposed.png",
            "/apple-touch-icon-180x180.png",
            "/apple-touch-icon-152x152.png",
            "/apple-touch-icon-120x120.png",
            "/android-chrome-192x192.png",
            "/android-chrome-512x512.png",
            "/mstile-150x150.png",
            "/icon.png",
            "/icons/icon-192x192.png"
        };
    }
}
