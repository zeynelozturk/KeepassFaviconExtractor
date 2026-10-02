using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FaviconExtractor.Networking
{
    /// <summary>
    /// Best-effort lookup of Android app icons from the Google Play web page.
    /// This is intentionally isolated and non-throwing for exploratory testing.
    /// </summary>
    public static class AndroidAppIconProvider
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public sealed class LookupResult
        {
            public Image Icon { get; set; }
            public string SourceUrl { get; set; }
            public string ErrorMessage { get; set; }
            public bool Success => Icon != null && string.IsNullOrEmpty(ErrorMessage);
        }

        /// <summary>
        /// Attempts to find the app icon for the given package name on the Play Store page.
        /// Returns a LookupResult; on failure the result contains ErrorMessage and null Icon.
        /// Never throws.
        /// </summary>
        public static async Task<LookupResult> LookupAsync(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return new LookupResult { ErrorMessage = "Package name is empty." };
            }

            try
            {
                // Build Play Store URL (use en locale as a reasonable default)
                string playUrl = $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(packageName)}&hl=en&gl=US";

                using (var resp = await httpClient.GetAsync(playUrl).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        return new LookupResult { ErrorMessage = $"Play Store returned {(int)resp.StatusCode} - {resp.ReasonPhrase}", SourceUrl = playUrl };
                    }

                    string html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Try to find og:image meta tag first
                    string imageUrl = null;
                    var m = Regex.Match(html, "<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        imageUrl = m.Groups[1].Value;
                    }

                    // Fallback: look for link rel=image_src
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        m = Regex.Match(html, "<link[^>]+rel=[\"']image_src[\"'][^>]+href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                        if (m.Success)
                            imageUrl = m.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        return new LookupResult { ErrorMessage = "Could not locate icon URL in Play Store page markup.", SourceUrl = playUrl };
                    }

                    // Some URLs may be protocol-relative
                    if (imageUrl.StartsWith("//"))
                        imageUrl = "https:" + imageUrl;

                    // Download image bytes
                    try
                    {
                        using (var imgResp = await httpClient.GetAsync(imageUrl).ConfigureAwait(false))
                        {
                            if (!imgResp.IsSuccessStatusCode)
                            {
                                return new LookupResult { ErrorMessage = $"Failed to download icon image: {(int)imgResp.StatusCode}", SourceUrl = imageUrl };
                            }

                            byte[] data = await imgResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            using (var ms = new MemoryStream(data))
                            {
                                Image img = Image.FromStream(ms);
                                return new LookupResult { Icon = img, SourceUrl = imageUrl };
                            }
                        }
                    }
                    catch (Exception exImg)
                    {
                        return new LookupResult { ErrorMessage = "Downloading or decoding icon failed: " + exImg.Message, SourceUrl = imageUrl };
                    }
                }
            }
            catch (Exception ex)
            {
                return new LookupResult { ErrorMessage = "Network or parsing error: " + ex.Message };
            }
        }
    }
}
