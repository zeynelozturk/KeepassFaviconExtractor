using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KeePassLib;

namespace FaviconExtractor
{
    // Per-plugin external dependencies. The production workflow keeps its candidate ordering,
    // normalization, assignment and cleanup; tests can supply deterministic network responses.
    internal sealed class FaviconWorkflowServices
    {
        internal Func<string, CancellationToken, Action<string>, Task<HtmlFaviconDiscoveryResult>> Discover = FaviconDiscoveryService.DiscoverAsync;
        internal Func<Uri, CancellationToken, bool, Task<byte[]>> Download = CandidatePreparation.DownloadAsync;
        internal Func<Uri, CancellationToken, Task<List<FaviconCandidate>>> DiscoverRescue = ExternalFaviconServiceDiscoverer.DiscoverAsync;
        internal Func<string, bool, Action<PreparedIcon>, Action<string>, CancellationToken, Task<AlternativeIconSearchResult>> SearchAlternatives =
            (url, direct, result, status, token) => AlternativeIconSearch.SearchAsync(url, direct, result, status, token);
        internal Action<PwEntry> RefreshIcons;
    }
}
