using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class FallbackDiagnosticsService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private const string WebpDiagnosticSampleBase64 = "UklGRqIBAABXRUJQVlA4TJUBAAAvH8AHELfCIGDbNuZPt7uJbdFA0LZtvO38IT8M27aNpOvtv26/O1uMIklSlALW/2t1sYFVQIdB/pACBDEQpQJEmYLGb3yAqnFZPI6I1xUBWQCvLQqKsIBMqKhxmCDU7RgHUIogHNMKiyoA+juAEAQx6tc/nxJpxd8VEoWsOsqvnbL6+nwqZLDCw1QSPmFKg4wkDA1ZuPXeKKMDYiTbpq25z37vG+/btm3va+Sfis/5EUT0H23btqGQuZN7RBQX+I4lhVmOH0SpQs8UxUwvTM4VjdwwijzRyosCU48Z+JL1d37xlXY5PLQlne/kODKM/lXio0dpIyP31VOg+xtbA2pPmUclx1sZStci9w2g9SrF2SPApthTQGlbVFoGZuS4BIyLkpMS9L8GQOtRzU8XSouAsSWKrQIGMPan6qUJQPVWlFtPmRd1n22g+azhrAQsaZw9AVQfNNyUgGlbwxAwDjTuvQ50vjXsAcyJhhWA/eJ95VkAKu/F88uzU4JRu4jjS167k7N3UsTX35X2LvV3/Q//gva/EgEA";

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
            AppendWebpDecoderStatus(sb, onLine);
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

                            bool isLikelyPlaceholder = false;
                            if (ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource(source) && response.Content != null)
                            {
                                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                                isLikelyPlaceholder = ExternalFaviconServiceDiscoverer.IsLikelyPlaceholder(source, responseBytes);
                            }

                            bool ok = response.IsSuccessStatusCode
                                && LooksLikeImage(type, finalUri)
                                && HasUsableContentLocationForSource(source, response)
                                && !isLikelyPlaceholder;
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
                                + (isLikelyPlaceholder ? ", placeholder=detected" : string.Empty)
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

        private static void AppendWebpDecoderStatus(StringBuilder sb, Action<string> onLine)
        {
            string beforeProbe = WebpDecoder.GetRuntimeDiagnosticInfo();
            AppendLine(sb, "WebP loader: " + SummarizeWebpRuntimeInfo(beforeProbe), onLine);
            try
            {
                byte[] webpSample = Convert.FromBase64String(WebpDiagnosticSampleBase64);
                using (Bitmap bitmap = WebpDecoder.DecodeToBitmap(webpSample))
                {
                    bool ok = bitmap.Width > 0 && bitmap.Height > 0;
                    AppendLine(sb, "WebP native decoder: " + (ok ? "OK (embedded sample decoded)" : "FAIL (decoded image has invalid dimensions)"), onLine);
                }

                string afterProbe = WebpDecoder.GetRuntimeDiagnosticInfo();
                AppendLine(sb, "WebP loader (after probe): " + SummarizeWebpRuntimeInfo(afterProbe), onLine);
            }
            catch (Exception ex)
            {
                // If the native runtime is unavailable, provide a clearer message so users
                // know WebP support is disabled and how to remediate.
                string message = ex.Message;
                if (ex is InvalidOperationException && ex.Message.Contains("WEBP native runtime is not available"))
                {
                    message = "WEBP native runtime is not available. Install native binaries via scripts\\bootstrap-native-webp.ps1 or ensure Visual C++ runtime (UCRT) is present.";
                }

                AppendLine(sb, "WebP native decoder: FAIL (" + ex.GetType().Name + ": " + message + ")", onLine);
                if (ex.InnerException != null)
                {
                    AppendLine(sb, "WebP native decoder inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message, onLine);
                }

                string afterProbe = WebpDecoder.GetRuntimeDiagnosticInfo();
                AppendLine(sb, "WebP loader (after probe): " + SummarizeWebpRuntimeInfo(afterProbe), onLine);
            }
        }

        private static string SummarizeWebpRuntimeInfo(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "unavailable";
            }

            string bitness = ExtractDiagnosticValue(raw, "process-bitness=") ?? "unknown";
            string available = ExtractDiagnosticValue(raw, "webp-native-available=") ?? "unknown";
            string failure = ExtractDiagnosticValue(raw, "webp-native-failure=");

            string foundWebp = ExtractFirstFoundPath(raw, "libwebp.dll");
            string foundSharpYuv = ExtractFirstFoundPath(raw, "libsharpyuv.dll");

            List<string> parts = new List<string>();
            parts.Add("process=" + bitness);
            parts.Add("available=" + available);

            if (!string.IsNullOrWhiteSpace(foundWebp))
            {
                string folder = Path.GetDirectoryName(foundWebp) ?? foundWebp;
                parts.Add("location=" + folder);
            }
            else
            {
                parts.Add("location=not-found");
            }

            parts.Add("libwebp=" + (!string.IsNullOrWhiteSpace(foundWebp) ? "FOUND" : "MISS"));
            parts.Add("libsharpyuv=" + (!string.IsNullOrWhiteSpace(foundSharpYuv) ? "FOUND" : "MISS"));

            if (!string.IsNullOrWhiteSpace(failure))
            {
                parts.Add("failure=" + TruncateDiagnosticValue(failure, 120));
            }

            return string.Join(", ", parts.ToArray());
        }

        private static string ExtractDiagnosticValue(string raw, string key)
        {
            string[] segments = raw.Split(new[] { " | " }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    return segment.Substring(key.Length);
                }
            }

            return null;
        }

        private static string ExtractFirstFoundPath(string raw, string fileName)
        {
            string[] segments = raw.Split(new[] { " | " }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.StartsWith("FOUND ", StringComparison.OrdinalIgnoreCase)
                    && segment.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return segment.Substring("FOUND ".Length);
                }
            }

            return null;
        }

        private static string TruncateDiagnosticValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
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
            client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/x-icon,image/svg+xml,image/apng,image/*,*/*;q=0.8");
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

        private static bool HasUsableContentLocationForSource(string source, HttpResponseMessage response)
        {
            if (!IsGoogleExternalSource(source))
            {
                return true;
            }

            Uri contentLocation = response != null
                && response.Content != null
                && response.Content.Headers != null
                ? response.Content.Headers.ContentLocation
                : null;

            return contentLocation != null
                && contentLocation.IsAbsoluteUri
                && !IsGoogleOwnedHost(contentLocation.Host);
        }

        private static bool IsGoogleExternalSource(string source)
        {
            return string.Equals(source, "external-google-s2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "external-google-faviconv2", StringComparison.OrdinalIgnoreCase);
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
    }
}
