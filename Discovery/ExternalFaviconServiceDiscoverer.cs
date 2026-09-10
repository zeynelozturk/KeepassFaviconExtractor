using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
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
            Dictionary<string, DnsResolutionState> dnsStateByDomain = new Dictionary<string, DnsResolutionState>(StringComparer.OrdinalIgnoreCase);
            foreach (string domain in GetDomainCandidates(siteUri.DnsSafeHost))
            {
                bool isExactHost = string.Equals(domain, siteUri.DnsSafeHost, StringComparison.OrdinalIgnoreCase);
                DnsResolutionState dnsState = await GetDnsResolutionStateAsync(domain, dnsStateByDomain, cancellationToken).ConfigureAwait(false);
                foreach (string source in ExternalProviderSources)
                {
                    if (ShouldSkipProviderForDnsState(source, dnsState))
                    {
                        continue;
                    }

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
            client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/x-icon,image/svg+xml,image/apng,image/*,*/*;q=0.8");
            return client;
        }

        private static async Task<FaviconCandidate> ProbeSingleCandidateAsync(Uri requestUri, string source, bool treatAsLogo, bool isExactHost, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpResponseMessage response = await HttpRetryPolicy.ExecuteWithRetryAsync(
                    () => HttpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
                    r => r != null && HttpRetryPolicy.IsTransientStatusCode(r.StatusCode),
                    ex => HttpRetryPolicy.IsTransientException(ex, cancellationToken),
                    cancellationToken).ConfigureAwait(false))
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

                    if (await IsLikelyPlaceholderResponseAsync(source, response).ConfigureAwait(false))
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

                    Uri candidateIconUri = GetCandidateIconUri(requestUri, finalUri, source, response);
                    if (candidateIconUri == null)
                    {
                        return null;
                    }

                    return new FaviconCandidate
                    {
                        Source = source,
                        RelAttribute = rel,
                        TypeAttribute = type,
                        SizesAttribute = string.Empty,
                        IconUri = candidateIconUri,
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
            "external-google-faviconv2",
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

            if (string.Equals(source, "external-google-faviconv2", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri("https://t2.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://" + escapedDomain + "&size=64");
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

            if (string.Equals(source, "external-google-faviconv2", StringComparison.OrdinalIgnoreCase))
            {
                return FaviconDiscoveryPreferences.GoogleFaviconV2ExternalScoreBonus;
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
                || absolute.Contains("/faviconv2")
                || absolute.Contains("favicon.im")
                || absolute.Contains("icons.duckduckgo.com/ip3/")
                || absolute.Contains("favicone.com/")
                || absolute.Contains("favicon.vemetric.com/");
        }

        private static Uri GetCandidateIconUri(Uri requestUri, Uri finalUri, string source, HttpResponseMessage response)
        {
            if (IsGoogleExternalSource(source))
            {
                Uri contentLocation = GetResponseContentLocationUri(response, finalUri);
                if (contentLocation == null || IsGoogleOwnedHost(contentLocation.Host))
                {
                    return null;
                }

                return requestUri;
            }

            return finalUri;
        }

        private static bool IsGoogleExternalSource(string source)
        {
            return string.Equals(source, "external-google-s2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "external-google-faviconv2", StringComparison.OrdinalIgnoreCase);
        }

        private static Uri GetResponseContentLocationUri(HttpResponseMessage response, Uri fallbackBaseUri)
        {
            if (response == null || response.Content == null || response.Content.Headers == null)
            {
                return null;
            }

            Uri contentLocation = response.Content.Headers.ContentLocation;
            if (contentLocation == null)
            {
                return null;
            }

            if (contentLocation.IsAbsoluteUri)
            {
                return contentLocation;
            }

            if (fallbackBaseUri == null)
            {
                return null;
            }

            return new Uri(fallbackBaseUri, contentLocation);
        }

        private static bool IsGoogleOwnedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return true;
            }

            return host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".gstatic.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "google.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "gstatic.com", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<DnsResolutionState> GetDnsResolutionStateAsync(string domain, Dictionary<string, DnsResolutionState> cache, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return DnsResolutionState.Unknown;
            }

            DnsResolutionState cached;
            if (cache != null && cache.TryGetValue(domain, out cached))
            {
                return cached;
            }

            DnsResolutionState state = await ResolveDnsStateAsync(domain, cancellationToken).ConfigureAwait(false);
            if (cache != null)
            {
                cache[domain] = state;
            }

            return state;
        }

        private static async Task<DnsResolutionState> ResolveDnsStateAsync(string domain, CancellationToken cancellationToken)
        {
            try
            {
                Task<IPAddress[]> resolveTask = Dns.GetHostAddressesAsync(domain);
                Task timeoutTask = Task.Delay(FaviconDiscoveryPreferences.FallbackProbeTimeout, cancellationToken);
                Task completed = await Task.WhenAny(resolveTask, timeoutTask).ConfigureAwait(false);
                if (completed != resolveTask)
                {
                    return DnsResolutionState.Unknown;
                }

                IPAddress[] addresses = await resolveTask.ConfigureAwait(false);
                return addresses != null && addresses.Length > 0
                    ? DnsResolutionState.Resolved
                    : DnsResolutionState.Unresolved;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.HostNotFound || ex.SocketErrorCode == SocketError.NoData)
                {
                    return DnsResolutionState.Unresolved;
                }

                return DnsResolutionState.Unknown;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return DnsResolutionState.Unknown;
            }
            catch
            {
                return DnsResolutionState.Unknown;
            }
        }

        internal static bool ShouldSkipProviderForDnsState(string source, DnsResolutionState dnsState)
        {
            return dnsState == DnsResolutionState.Unresolved
                && IsPlaceholderProneExternalSource(source);
        }

        internal static bool IsPlaceholderProneExternalSource(string source)
        {
            return string.Equals(source, "external-favicone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "external-vemetric", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "external-favicon-im", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsKnownPlaceholderHash(string source, string sha256Hex)
        {
            if (string.IsNullOrWhiteSpace(sha256Hex))
            {
                return false;
            }

            if (string.Equals(source, "external-favicone", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(sha256Hex, "7b4142212706d008a65036f8993385a8371f321c656bdba5b4f3e00e5721dc77", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(source, "external-vemetric", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(sha256Hex, "2a93c17cbf5e17b98f3edd00ff751cbb91954adbdc3559668b1b78d81df87710", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(source, "external-favicon-im", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(sha256Hex, "f594faa8f108ab9610dac7541200eadcaf558bd8944dc57e28f411a9a060ea9e", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        internal static bool IsLikelyPlaceholder(string source, byte[] responseBytes)
        {
            if (!IsPlaceholderProneExternalSource(source) || responseBytes == null || responseBytes.Length == 0)
            {
                return false;
            }

            string sha256Hex = ComputeSha256Hex(responseBytes);
            return IsKnownPlaceholderHash(source, sha256Hex);
        }

        private static async Task<bool> IsLikelyPlaceholderResponseAsync(string source, HttpResponseMessage response)
        {
            if (!IsPlaceholderProneExternalSource(source) || response == null || response.Content == null)
            {
                return false;
            }

            byte[] responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return IsLikelyPlaceholder(source, responseBytes);
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
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

        internal enum DnsResolutionState
        {
            Unknown = 0,
            Resolved = 1,
            Unresolved = 2
        }
    }
}
