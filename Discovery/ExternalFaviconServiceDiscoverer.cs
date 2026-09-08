using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class ExternalFaviconServiceDiscoverer
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<List<FaviconCandidate>> DiscoverAsync(Uri siteUri, CancellationToken cancellationToken)
        {
            List<FaviconCandidate> candidates = new List<FaviconCandidate>();
            if (siteUri == null || string.IsNullOrWhiteSpace(siteUri.DnsSafeHost))
            {
                return candidates;
            }

            List<Task<FaviconCandidate>> probes = new List<Task<FaviconCandidate>>();
            foreach (string domain in GetDomainCandidates(siteUri.DnsSafeHost))
            {
                bool isExactHost = string.Equals(domain, siteUri.DnsSafeHost, StringComparison.OrdinalIgnoreCase);
                foreach (string source in ExternalProviderSources)
                {
                    Uri requestUri = BuildProviderUri(source, domain);
                    probes.Add(ProbeSingleCandidateAsync(requestUri, source, false, isExactHost, cancellationToken));
                }
            }

            FaviconCandidate[] results = await Task.WhenAll(probes).ConfigureAwait(false);
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] != null)
                {
                    candidates.Add(results[i]);
                }
            }

            return candidates;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            handler.MaxAutomaticRedirections = FaviconDiscoveryPreferences.MaxAutomaticRedirects;

            HttpClient client = new HttpClient(handler);
            client.Timeout = FaviconDiscoveryPreferences.FallbackProbeTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            return client;
        }

        private static async Task<FaviconCandidate> ProbeSingleCandidateAsync(Uri requestUri, string source, bool treatAsLogo, bool isExactHost, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpResponseMessage response = await HttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    Uri finalUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                        ? response.RequestMessage.RequestUri
                        : requestUri;

                    string type = response.Content != null && response.Content.Headers != null && response.Content.Headers.ContentType != null
                        ? response.Content.Headers.ContentType.MediaType
                        : string.Empty;

                    if (!LooksLikeImage(type, finalUri))
                    {
                        return null;
                    }

                    if (IsUnsupportedContentType(type))
                    {
                        return null;
                    }

                    Size? size = null;
                    string rel = treatAsLogo ? "logo" : "icon";
                    int score = FaviconScorer.Score("icon", type, size);
                    score = FaviconScorer.ApplyExternalServicePenalty(score);
                    score += GetExternalSourceScoreAdjustment(source);

                    if (isExactHost)
                    {
                        score += FaviconDiscoveryPreferences.ExactHostScoreBonus;
                    }
                    if (treatAsLogo)
                    {
                        score = FaviconScorer.ApplyLogoPenalty(score);
                    }

                    return new FaviconCandidate
                    {
                        Source = source,
                        RelAttribute = rel,
                        TypeAttribute = type,
                        SizesAttribute = string.Empty,
                        IconUri = GetCandidateIconUri(requestUri, finalUri, source),
                        BestSize = size,
                        Score = score
                    };
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        internal static IReadOnlyList<string> GetDomainCandidates(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return new string[0];
            }

            List<string> candidates = new List<string>();
            candidates.Add(host);

            string[] labels = host.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (labels.Length >= 3)
            {
                string parentDomain = string.Join(".", labels.Skip(1));
                if (!string.IsNullOrWhiteSpace(parentDomain) &&
                    !string.Equals(parentDomain, host, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(parentDomain);
                }
            }

            return candidates;
        }

        internal static readonly string[] ExternalProviderSources = new[]
        {
            "external-google-s2",
            "external-duckduckgo-ip3",
            "external-favicone",
            "external-vemetric",
            "external-favicon-im"
        };

        internal static Uri BuildProviderUri(string source, string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException("Domain is required.", nameof(domain));
            }

            string escapedDomain = Uri.EscapeDataString(domain);
            if (string.Equals(source, "external-google-s2", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://www.google.com/s2/favicons?domain=" + escapedDomain + "&sz=64");
            }

            if (string.Equals(source, "external-duckduckgo-ip3", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://icons.duckduckgo.com/ip3/" + escapedDomain + ".ico");
            }

            if (string.Equals(source, "external-favicone", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://favicone.com/" + escapedDomain + "?s=128");
            }

            if (string.Equals(source, "external-vemetric", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://favicon.vemetric.com/" + escapedDomain);
            }

            if (string.Equals(source, "external-favicon-im", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://a.favicon.im/" + escapedDomain + "?larger=true");
            }

            throw new ArgumentException("Unsupported external provider source: " + source, nameof(source));
        }

        internal static int GetExternalSourceScoreAdjustment(string source)
        {
            if (string.Equals(source, "external-google-s2", StringComparison.OrdinalIgnoreCase))
            {
                return FaviconDiscoveryPreferences.GoogleExternalScoreBonus;
            }

            if (string.Equals(source, "external-duckduckgo-ip3", StringComparison.OrdinalIgnoreCase))
            {
                return FaviconDiscoveryPreferences.DuckDuckGoExternalScoreBonus;
            }

            if (string.Equals(source, "external-favicone", StringComparison.OrdinalIgnoreCase))
            {
                return FaviconDiscoveryPreferences.FaviconeExternalScoreBonus;
            }

            if (string.Equals(source, "external-vemetric", StringComparison.OrdinalIgnoreCase))
            {
                return FaviconDiscoveryPreferences.VemetricExternalScoreBonus;
            }

            if (string.Equals(source, "external-favicon-im", StringComparison.OrdinalIgnoreCase))
            {
                return -FaviconDiscoveryPreferences.FaviconImExternalScorePenalty;
            }

            return 0;
        }

        private static bool LooksLikeImage(string type, Uri uri)
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                string normalized = type.ToLowerInvariant();
                if (normalized.StartsWith("image/")) return true;
                if (normalized.Contains("icon")) return true;
            }

            string absolute = uri.AbsoluteUri.ToLowerInvariant();
            return absolute.EndsWith(".ico")
                || absolute.EndsWith(".png")
                || absolute.EndsWith(".svg")
                || absolute.EndsWith(".jpg")
                || absolute.EndsWith(".jpeg")
                || absolute.EndsWith(".webp")
                || absolute.EndsWith(".gif")
                || absolute.Contains("/s2/favicons")
                || absolute.Contains("favicon.im")
                || absolute.Contains("icons.duckduckgo.com/ip3/")
                || absolute.Contains("favicone.com/")
                || absolute.Contains("favicon.vemetric.com/");
        }

        private static Uri GetCandidateIconUri(Uri requestUri, Uri finalUri, string source)
        {
            if (string.Equals(source, "external-google-s2", StringComparison.OrdinalIgnoreCase))
            {
                return requestUri;
            }

            return finalUri;
        }

        private static bool IsUnsupportedContentType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            return type.IndexOf("image/avif", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static async Task<FaviconCandidate> DiscoverLogoAsync(Uri siteUri, CancellationToken cancellationToken)
        {
            if (siteUri == null || string.IsNullOrWhiteSpace(siteUri.DnsSafeHost))
            {
                return null;
            }

            foreach (string domain in GetDomainCandidates(siteUri.DnsSafeHost))
            {
                Uri logoUri = new Uri("https://logos.hunter.io/" + Uri.EscapeDataString(domain));

                // This endpoint returns organization logos, which may be non-square.
                // Keep these candidates tagged as logo-specific and lower-priority than favicon sources.
                bool isExactHost = string.Equals(domain, siteUri.DnsSafeHost, StringComparison.OrdinalIgnoreCase);
                FaviconCandidate candidate = await ProbeSingleCandidateAsync(logoUri, "external-logo-hunter", true, isExactHost, cancellationToken).ConfigureAwait(false);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
