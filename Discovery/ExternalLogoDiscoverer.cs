using System;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class ExternalLogoDiscoverer
    {
        public static Task<FaviconCandidate> DiscoverAsync(Uri siteUri, CancellationToken cancellationToken)
        {
            return ExternalFaviconServiceDiscoverer.DiscoverLogoAsync(siteUri, cancellationToken);
        }
    }
}
