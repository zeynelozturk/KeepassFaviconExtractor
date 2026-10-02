using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor.Networking
{
    /// <summary>
    /// Best-effort lookup of Android app icons from the Google Play web page.
    /// This is intentionally isolated and non-throwing for exploratory testing.
    /// </summary>
    public static class AndroidAppIconProvider
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = RequestTimeout
        };

        public sealed class LookupResult
        {
            public byte[] IconBytes { get; set; }
            public string ContentType { get; set; }
            public string SourceUrl { get; set; }
            public string ErrorMessage { get; set; }
            public bool Success => IconBytes != null && IconBytes.Length > 0 && string.IsNullOrEmpty(ErrorMessage);
        }

        public static Task<LookupResult> LookupAsync(string packageName)
        {
            return LookupAsync(packageName, CancellationToken.None);
        }

        public static async Task<LookupResult> LookupAsync(string packageName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return new LookupResult { ErrorMessage = "Package name is empty." };
            }

            try
            {
                string playUrl = $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(packageName)}&hl=en&gl=US";
                using (var pageResponse = await GetWithRetryAsync(playUrl, cancellationToken).ConfigureAwait(false))
                {
                    if (!pageResponse.IsSuccessStatusCode)
                    {
                        return new LookupResult
                        {
                            ErrorMessage = $"Play Store returned {(int)pageResponse.StatusCode} - {pageResponse.ReasonPhrase}",
                            SourceUrl = playUrl
                        };
                    }

                    string html = await pageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string imageUrl = TryExtractImageUrl(html);
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        return new LookupResult
                        {
                            ErrorMessage = "Could not locate icon URL in Play Store page markup.",
                            SourceUrl = playUrl
                        };
                    }

                    if (imageUrl.StartsWith("//"))
                    {
                        imageUrl = "https:" + imageUrl;
                    }

                    using (var imageResponse = await GetWithRetryAsync(imageUrl, cancellationToken).ConfigureAwait(false))
                    {
                        if (!imageResponse.IsSuccessStatusCode)
                        {
                            return new LookupResult
                            {
                                ErrorMessage = $"Failed to download icon image: {(int)imageResponse.StatusCode}",
                                SourceUrl = imageUrl
                            };
                        }

                        byte[] rawBytes = await imageResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (rawBytes == null || rawBytes.Length == 0)
                        {
                            return new LookupResult { ErrorMessage = "Downloaded icon payload was empty.", SourceUrl = imageUrl };
                        }

                        string contentType = imageResponse.Content.Headers.ContentType != null
                            ? imageResponse.Content.Headers.ContentType.MediaType
                            : "image/png";
                        byte[] pngBytes = IconNormalizer.NormalizeToPng(rawBytes, contentType, imageUrl, cancellationToken);
                        return new LookupResult
                        {
                            IconBytes = pngBytes,
                            ContentType = "image/png",
                            SourceUrl = imageUrl
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new LookupResult { ErrorMessage = "Timed out while fetching the Android app icon." };
            }
            catch (Exception ex)
            {
                return new LookupResult { ErrorMessage = "Network or parsing error while looking up the Android app icon: " + ex.Message };
            }
        }

        private static async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
        {
            for (int retry = 0; retry < MaxAttempts; retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    if (retry < MaxAttempts - 1)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (retry + 1)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
                catch
                {
                    if (retry < MaxAttempts - 1)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (retry + 1)), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

            return null;
        }

        private static string TryExtractImageUrl(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            Match match = Regex.Match(html, "<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = Regex.Match(html, "<link[^>]+rel=[\"']image_src[\"'][^>]+href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

    }
}
