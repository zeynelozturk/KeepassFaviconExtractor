using System;
using System.Net;
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

            using (HttpResponseMessage response = await HttpRetryPolicy.ExecuteWithRetryAsync(
                () => HttpClient.GetAsync(iconUri, HttpCompletionOption.ResponseContentRead, cancellationToken),
                r => r != null && HttpRetryPolicy.IsTransientStatusCode(r.StatusCode),
                ex => HttpRetryPolicy.IsTransientException(ex, cancellationToken),
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                if (response.Content == null)
                {
                    throw new InvalidOperationException("Icon response has no content.");
                }

                Uri finalUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                    ? response.RequestMessage.RequestUri
                    : iconUri;

                if (FaviconDiscoveryPreferences.EnforcePrivateAddressBlocking
                    && await NetworkSafety.IsPrivateOrLoopbackUriAsync(finalUri, FaviconDiscoveryPreferences.FallbackProbeTimeout, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Icon response target is a private or loopback address.");
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
            client.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/x-icon,image/svg+xml,image/apng,image/*,*/*;q=0.8");
            return client;
        }
    }

    internal static class HttpRetryPolicy
    {
        private static readonly TimeSpan[] RetryDelays = new[]
        {
            TimeSpan.FromMilliseconds(250)
        };

        internal static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            Func<T, bool> shouldRetryResult,
            Func<Exception, bool> shouldRetryException,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    T result = await operation().ConfigureAwait(false);
                    bool shouldRetry = shouldRetryResult != null && shouldRetryResult(result);
                    if (!shouldRetry || attempt >= RetryDelays.Length)
                    {
                        return result;
                    }

                    DisposeIfNeeded(result);
                }
                catch (Exception ex) when (shouldRetryException != null && shouldRetryException(ex) && attempt < RetryDelays.Length)
                {
                }

                TimeSpan delay = RetryDelays[attempt];
                attempt++;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 408 || code == 429 || (code >= 500 && code <= 599);
        }

        internal static bool IsTransientException(Exception ex, CancellationToken cancellationToken)
        {
            if (ex is HttpRequestException)
            {
                return true;
            }

            if (ex is OperationCanceledException)
            {
                return !cancellationToken.IsCancellationRequested;
            }

            return false;
        }

        private static void DisposeIfNeeded<T>(T instance)
        {
            IDisposable disposable = instance as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }
}
