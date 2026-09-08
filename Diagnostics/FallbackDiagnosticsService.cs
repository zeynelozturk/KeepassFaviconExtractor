using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class FallbackDiagnosticsService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        private static readonly string[] ProbeDomains = new[]
        {
            "google.com",
            "microsoft.com",
            "github.com"
        };

        public static async Task<string> RunAsync(CancellationToken cancellationToken)
        {
            return await RunAsync(cancellationToken, null).ConfigureAwait(false);
        }

        public static async Task<string> RunAsync(CancellationToken cancellationToken, Action<string> onLine)
        {
            StringBuilder sb = new StringBuilder();
            List<string> failedProbes = new List<string>();
            int totalProbes = 0;
            int totalSuccess = 0;

            AppendLine(sb, "External fallback diagnostics", onLine);
            AppendLine(sb, string.Empty, onLine);
            AppendLine(sb, "Providers are checked with live HTTP probes. Results can vary due to network/provider state.", onLine);
            AppendLine(sb, string.Empty, onLine);

            foreach (string source in ExternalFaviconServiceDiscoverer.ExternalProviderSources)
            {
                int successCount = 0;

                AppendLine(sb, source + ":", onLine);
                for (int i = 0; i < ProbeDomains.Length; i++)
                {
                    string domain = ProbeDomains[i];
                    totalProbes++;
                    Uri uri = ExternalFaviconServiceDiscoverer.BuildProviderUri(source, domain);
                    Stopwatch sw = Stopwatch.StartNew();

                    try
                    {
                        using (HttpResponseMessage response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            sw.Stop();

                            Uri finalUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                                ? response.RequestMessage.RequestUri
                                : uri;
                            string type = response.Content != null && response.Content.Headers != null && response.Content.Headers.ContentType != null
                                ? response.Content.Headers.ContentType.MediaType
                                : string.Empty;

                            bool ok = response.IsSuccessStatusCode && LooksLikeImage(type, finalUri);
                            if (ok)
                            {
                                successCount++;
                                totalSuccess++;
                            }
                            else
                            {
                                failedProbes.Add(source + " / " + domain);
                            }

                            AppendLine(sb, "  - " + domain
                                + " => " + (ok ? "OK" : "FAIL")
                                + " (status=" + (int)response.StatusCode
                                + ", type=" + (string.IsNullOrWhiteSpace(type) ? "(none)" : type)
                                + ", " + sw.ElapsedMilliseconds + "ms)", onLine);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        sw.Stop();
                        failedProbes.Add(source + " / " + domain);
                        AppendLine(sb, "  - " + domain + " => FAIL (timeout/canceled, " + sw.ElapsedMilliseconds + "ms)", onLine);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        failedProbes.Add(source + " / " + domain);
                        AppendLine(sb, "  - " + domain + " => FAIL (" + ex.GetType().Name + ": " + ex.Message + ", " + sw.ElapsedMilliseconds + "ms)", onLine);
                    }
                }

                AppendLine(sb, "  Summary: " + successCount + "/" + ProbeDomains.Length + " probes succeeded", onLine);
                AppendLine(sb, string.Empty, onLine);
            }

            AppendLine(sb, "Overall result: " + (failedProbes.Count == 0 ? "SUCCESS" : "FAIL"), onLine);
            AppendLine(sb, "Overall summary: " + totalSuccess + "/" + totalProbes + " probes succeeded", onLine);
            if (failedProbes.Count > 0)
            {
                AppendLine(sb, "Failed probes:", onLine);
                for (int i = 0; i < failedProbes.Count; i++)
                {
                    AppendLine(sb, "  - " + failedProbes[i], onLine);
                }
            }

            return sb.ToString();
        }

        private static void AppendLine(StringBuilder sb, string line, Action<string> onLine)
        {
            sb.AppendLine(line);
            if (onLine != null)
            {
                onLine(line);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            handler.MaxAutomaticRedirections = FaviconDiscoveryPreferences.MaxAutomaticRedirects;

            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(6);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            return client;
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
    }
}
