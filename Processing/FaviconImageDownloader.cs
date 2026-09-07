using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class FaviconImageDownloader
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<byte[]> DownloadAsync(Uri iconUri, CancellationToken cancellationToken)
        {
            if (iconUri == null)
            {
                throw new ArgumentNullException(nameof(iconUri));
            }

            using (HttpResponseMessage response = await HttpClient.GetAsync(iconUri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                if (response.Content == null)
                {
                    throw new InvalidOperationException("Icon response has no content.");
                }

                if (response.Content.Headers != null && response.Content.Headers.ContentLength.HasValue)
                {
                    if (response.Content.Headers.ContentLength.Value > FaviconDiscoveryPreferences.MaxIconDownloadBytes)
                    {
                        throw new InvalidOperationException("Icon response is larger than configured limit.");
                    }
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    throw new InvalidOperationException("Icon response is empty.");
                }

                if (bytes.Length > FaviconDiscoveryPreferences.MaxIconDownloadBytes)
                {
                    throw new InvalidOperationException("Icon response is larger than configured limit.");
                }

                return bytes;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AllowAutoRedirect = true;
            handler.MaxAutomaticRedirections = FaviconDiscoveryPreferences.MaxAutomaticRedirects;

            HttpClient client = new HttpClient(handler);
            client.Timeout = FaviconDiscoveryPreferences.IconDownloadTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            return client;
        }
    }
}
