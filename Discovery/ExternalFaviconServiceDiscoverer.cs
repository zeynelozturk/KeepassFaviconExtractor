using System;
using System.Collections.Generic;
using System.Drawing;
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

            string domain = siteUri.DnsSafeHost;
            Uri googleUri = new Uri("https://www.google.com/s2/favicons?domain=" + Uri.EscapeDataString(domain) + "&sz=64");
            FaviconCandidate googleCandidate = await ProbeSingleCandidateAsync(googleUri, "external-google-s2", false, cancellationToken).ConfigureAwait(false);
            if (googleCandidate != null)
            {
                candidates.Add(googleCandidate);
                return candidates;
            }

            Uri faviconImUri = new Uri("https://a.favicon.im/" + Uri.EscapeDataString(domain) + "?larger=true");
            FaviconCandidate faviconImCandidate = await ProbeSingleCandidateAsync(faviconImUri, "external-favicon-im", false, cancellationToken).ConfigureAwait(false);
            if (faviconImCandidate != null)
            {
                candidates.Add(faviconImCandidate);
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

        private static async Task<FaviconCandidate> ProbeSingleCandidateAsync(Uri requestUri, string source, bool treatAsLogo, CancellationToken cancellationToken)
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

                    Size? size = treatAsLogo ? (Size?)null : new Size(64, 64);
                    int score = FaviconScorer.Score("icon", type, size);
                    score = FaviconScorer.ApplyExternalServicePenalty(score);
                    if (treatAsLogo)
                    {
                        score = FaviconScorer.ApplyLogoPenalty(score);
                    }

                    return new FaviconCandidate
                    {
                        Source = source,
                        RelAttribute = treatAsLogo ? "logo" : "icon",
                        TypeAttribute = type,
                        SizesAttribute = string.Empty,
                        IconUri = finalUri,
                        BestSize = size,
                        Score = score
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
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
                || absolute.Contains("favicon.im");
        }

        public static Task<FaviconCandidate> DiscoverLogoAsync(Uri siteUri, CancellationToken cancellationToken)
        {
            if (siteUri == null || string.IsNullOrWhiteSpace(siteUri.DnsSafeHost))
            {
                return Task.FromResult<FaviconCandidate>(null);
            }

            string domain = siteUri.DnsSafeHost;
            Uri logoUri = new Uri("https://logos.hunter.io/" + Uri.EscapeDataString(domain));

            // This endpoint returns organization logos, which may be non-square.
            // Keep these candidates tagged as logo-specific and lower-priority than favicon sources.
            return ProbeSingleCandidateAsync(logoUri, "external-logo-hunter", true, cancellationToken);
        }
    }
}
