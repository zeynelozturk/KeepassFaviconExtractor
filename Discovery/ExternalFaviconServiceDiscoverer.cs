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

            foreach (string domain in GetDomainCandidates(siteUri.DnsSafeHost))
            {
                Uri googleUri = new Uri("https://www.google.com/s2/favicons?domain_url=" + Uri.EscapeDataString("https://" + domain) + "&sz=64");
                FaviconCandidate googleCandidate = await ProbeSingleCandidateAsync(googleUri, "external-google-s2", false, cancellationToken).ConfigureAwait(false);
                if (googleCandidate != null)
                {
                    candidates.Add(googleCandidate);
                }

                Uri faviconImUri = new Uri("https://a.favicon.im/" + Uri.EscapeDataString(domain) + "?larger=true");
                FaviconCandidate faviconImCandidate = await ProbeSingleCandidateAsync(faviconImUri, "external-favicon-im", false, cancellationToken).ConfigureAwait(false);
                if (faviconImCandidate != null)
                {
                    candidates.Add(faviconImCandidate);
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

                    Size? size = await TryReadImageSizeAsync(response, type, finalUri).ConfigureAwait(false);
                    string rel = treatAsLogo ? "logo" : "icon";
                    int score = FaviconScorer.Score("icon", type, size);
                    score = FaviconScorer.ApplyExternalServicePenalty(score);
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

        private static async Task<Size?> TryReadImageSizeAsync(HttpResponseMessage response, string type, Uri uri)
        {
            if (response == null || response.Content == null)
            {
                return null;
            }

            long? contentLength = response.Content.Headers != null ? response.Content.Headers.ContentLength : null;
            if (contentLength.HasValue && contentLength.Value > FaviconDiscoveryPreferences.MaxIconDownloadBytes)
            {
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0 || bytes.Length > FaviconDiscoveryPreferences.MaxIconDownloadBytes)
            {
                return null;
            }

            if (IsSvg(type, uri))
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image image = Image.FromStream(stream, true, true))
                {
                    return new Size(image.Width, image.Height);
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSvg(string type, Uri uri)
        {
            if (!string.IsNullOrWhiteSpace(type) && type.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string absolute = uri.AbsoluteUri;
            return absolute.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
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
                FaviconCandidate candidate = await ProbeSingleCandidateAsync(logoUri, "external-logo-hunter", true, cancellationToken).ConfigureAwait(false);
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
