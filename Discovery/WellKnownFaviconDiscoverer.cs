using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class WellKnownFaviconDiscoverer
    {
        private static readonly Regex SizePattern = new Regex("(?<w>\\d{2,4})x(?<h>\\d{2,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<List<FaviconCandidate>> DiscoverAsync(IEnumerable<Uri> baseUris, int maxProbeCount, CancellationToken cancellationToken)
        {
            if (baseUris == null)
            {
                return new List<FaviconCandidate>();
            }

            List<Uri> normalizedBases = baseUris
                .Where(u => u != null && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                .Select(NormalizeBaseUri)
                .Distinct()
                .ToList();

            List<Uri> probeUris = BuildProbeUris(normalizedBases);
            List<FaviconCandidate> candidates = new List<FaviconCandidate>();
            int attemptedProbeCount = 0;

            foreach (Uri probeUri in probeUris)
            {
                if (attemptedProbeCount >= maxProbeCount)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                attemptedProbeCount++;

                FaviconCandidate candidate = await ProbeAsync(probeUri, cancellationToken).ConfigureAwait(false);
                if (candidate != null)
                {
                    candidates.Add(candidate);
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
            client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/x-icon,image/svg+xml,image/apng,image/*,*/*;q=0.8");
            return client;
        }

        private static Uri NormalizeBaseUri(Uri uri)
        {
            return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        }

        private static List<Uri> BuildProbeUris(List<Uri> baseUris)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Uri> probeUris = new List<Uri>();

            foreach (Uri baseUri in baseUris)
            {
                foreach (string path in FaviconDiscoveryPreferences.WellKnownFaviconPaths)
                {
                    Uri probeUri;
                    if (!Uri.TryCreate(baseUri, path, out probeUri))
                    {
                        continue;
                    }

                    if (seen.Add(probeUri.AbsoluteUri))
                    {
                        probeUris.Add(probeUri);
                    }
                }
            }

            return probeUris;
        }

        private static async Task<FaviconCandidate> ProbeAsync(Uri probeUri, CancellationToken cancellationToken)
        {
            try
            {
                HttpResponseMessage response = await SendHeadThenGetAsync(probeUri, cancellationToken).ConfigureAwait(false);
                if (response == null)
                {
                    return null;
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    Uri finalUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                        ? response.RequestMessage.RequestUri
                        : probeUri;

                    if (FaviconDiscoveryPreferences.EnforcePrivateAddressBlocking
                        && await NetworkSafety.IsPrivateOrLoopbackUriAsync(finalUri, FaviconDiscoveryPreferences.FallbackProbeTimeout, cancellationToken).ConfigureAwait(false))
                    {
                        return null;
                    }

                    string type = response.Content != null && response.Content.Headers != null && response.Content.Headers.ContentType != null
                        ? response.Content.Headers.ContentType.MediaType
                        : string.Empty;

                    if (!LooksLikeImage(type, finalUri))
                    {
                        return null;
                    }

                    Size? size = TryParseSizeFromUri(finalUri);
                    int score = FaviconScorer.Score("icon", type, size);
                    score = FaviconScorer.ApplyFallbackPenalty(score);

                    return new FaviconCandidate
                    {
                        Source = "fallback-probe",
                        RelAttribute = "fallback-probe",
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

        private static async Task<HttpResponseMessage> SendHeadThenGetAsync(Uri probeUri, CancellationToken cancellationToken)
        {
            HttpRequestMessage headRequest = new HttpRequestMessage(HttpMethod.Head, probeUri);
            HttpResponseMessage headResponse;

            try
            {
                headResponse = await HttpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)headResponse.StatusCode >= 200 && (int)headResponse.StatusCode < 400)
                {
                    return headResponse;
                }

                headResponse.Dispose();
            }
            catch
            {
            }

            HttpRequestMessage getRequest = new HttpRequestMessage(HttpMethod.Get, probeUri);
            return await HttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

            // Check if URL ends with a supported image extension
            foreach (string ext in FaviconDiscoveryPreferences.SupportedImageExtensions)
            {
                if (absolute.EndsWith(ext))
                {
                    return true;
                }
            }

            return false;
        }

        private static Size? TryParseSizeFromUri(Uri uri)
        {
            Match match = SizePattern.Match(uri.AbsoluteUri);
            if (!match.Success)
            {
                return null;
            }

            int width;
            int height;
            if (!int.TryParse(match.Groups["w"].Value, out width) || !int.TryParse(match.Groups["h"].Value, out height))
            {
                return null;
            }

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return new Size(width, height);
        }
    }
}
