using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal enum UriSafetyClassification
    {
        Public = 0,
        PrivateOrLoopback = 1,
        Unknown = 2
    }

    internal static class NetworkSafety
    {
        public static async Task<bool> IsPrivateOrLoopbackUriAsync(Uri uri, TimeSpan resolveTimeout, CancellationToken cancellationToken)
        {
            UriSafetyClassification classification = await ClassifyUriSafetyAsync(uri, resolveTimeout, cancellationToken).ConfigureAwait(false);
            return classification == UriSafetyClassification.PrivateOrLoopback;
        }

        public static async Task<UriSafetyClassification> ClassifyUriSafetyAsync(Uri uri, TimeSpan resolveTimeout, CancellationToken cancellationToken)
        {
            if (uri == null || !uri.IsAbsoluteUri)
            {
                return UriSafetyClassification.Unknown;
            }

            if (uri.IsLoopback)
            {
                return UriSafetyClassification.PrivateOrLoopback;
            }

            IPAddress parsed;
            if (IPAddress.TryParse(uri.Host, out parsed))
            {
                return IsPrivateOrLoopbackAddress(parsed)
                    ? UriSafetyClassification.PrivateOrLoopback
                    : UriSafetyClassification.Public;
            }

            IPAddress[] addresses = await ResolveAddressesAsync(uri.Host, resolveTimeout, cancellationToken).ConfigureAwait(false);
            if (addresses == null || addresses.Length == 0)
            {
                return UriSafetyClassification.Unknown;
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                if (IsPrivateOrLoopbackAddress(addresses[i]))
                {
                    return UriSafetyClassification.PrivateOrLoopback;
                }
            }

            return UriSafetyClassification.Public;
        }

        internal static bool IsPrivateOrLoopbackAddress(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                if (bytes.Length != 4)
                {
                    return false;
                }

                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || bytes[0] == 127
                    || bytes[0] == 0;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.Equals(IPAddress.IPv6Loopback))
                {
                    return true;
                }

                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
                {
                    return true;
                }

                byte[] bytes = address.GetAddressBytes();
                if (bytes.Length == 16)
                {
                    return (bytes[0] & 0xFE) == 0xFC;
                }
            }

            return false;
        }

        private static async Task<IPAddress[]> ResolveAddressesAsync(string host, TimeSpan resolveTimeout, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            try
            {
                Task<IPAddress[]> resolveTask = Dns.GetHostAddressesAsync(host);
                Task timeoutTask = Task.Delay(resolveTimeout, cancellationToken);
                Task completed = await Task.WhenAny(resolveTask, timeoutTask).ConfigureAwait(false);
                if (completed != resolveTask)
                {
                    return null;
                }

                return await resolveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return null;
            }
            catch (SocketException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
