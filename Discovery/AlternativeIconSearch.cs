using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    // Provider boundary also permits deterministic, offline coverage of partial results and cancellation.
    internal sealed class AlternativeIconSources
    {
        public Func<string, CancellationToken, Task<HtmlFaviconDiscoveryResult>> Html = HtmlFaviconDiscoverer.DiscoverAsync;
        public Func<Uri, CancellationToken, Task<List<FaviconCandidate>>> External = ExternalFaviconServiceDiscoverer.DiscoverAsync;
        public Func<List<Uri>, int, CancellationToken, Task<List<FaviconCandidate>>> WellKnown = WellKnownFaviconDiscoverer.DiscoverAsync;
        public Func<Uri, CancellationToken, Task<FaviconCandidate>> Logo = ExternalLogoDiscoverer.DiscoverAsync;
        public Func<FaviconCandidate, CancellationToken, Task<PreparedIcon>> Prepare = CandidatePreparation.PrepareAsync;
        public TimeSpan SourceTimeout = TimeSpan.FromSeconds(10);
    }

    internal sealed class AlternativeIconSearchResult
    {
        public int Count { get; set; }
        public bool TimedOut { get; set; }
        public int FailedSources { get; set; }
    }

    internal static class AlternativeIconSearch
    {
        internal const int MaxCandidates = 24;
        internal static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(45);

        public static async Task<AlternativeIconSearchResult> SearchAsync(string url, bool directImage,
            Action<PreparedIcon> onResult, Action<string> onStatus, CancellationToken token, AlternativeIconSources sources = null)
        {
            sources = sources ?? new AlternativeIconSources();
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("The entry needs an HTTP/HTTPS website or image URL. Android-only entries are not supported by the chooser.");

            var result = new AlternativeIconSearchResult();
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var images = new HashSet<string>(StringComparer.Ordinal);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(SearchTimeout);
                var searchToken = deadline.Token;
                Func<IEnumerable<FaviconCandidate>, CancellationToken, Task> prepare = async (candidates, preparationToken) =>
                {
                    foreach (var candidate in candidates.Where(c => c != null && c.IconUri != null)
                        .OrderByDescending(c => c.Score))
                    {
                        preparationToken.ThrowIfCancellationRequested();
                        if (urls.Count >= MaxCandidates) break;
                        if (!urls.Add(candidate.IconUri.AbsoluteUri)) continue;
                        try
                        {
                            onStatus("Loading icons (" + urls.Count + ")…");
                            var icon = await sources.Prepare(candidate, preparationToken).ConfigureAwait(false);
                            preparationToken.ThrowIfCancellationRequested();
                            if (images.Add(icon.ContentKey)) { result.Count++; onResult(icon); }
                        }
                        catch (OperationCanceledException) { preparationToken.ThrowIfCancellationRequested(); result.FailedSources++; }
                        catch (Exception) { result.FailedSources++; }
                    }
                };
                // Each existing provider gets its own turn even when HTML supplies a good icon.
                // Automatic discovery retains its early fallback decisions unchanged.
                try
                {
                    if (directImage)
                        await prepare(new[] { new FaviconCandidate { Source = "direct-image", IconUri = uri } }, searchToken).ConfigureAwait(false);
                    else
                    {
                        Uri pageUri = uri;
                        await RunSourceAsync(async sourceToken =>
                        {
                            onStatus("Checking website icons…");
                            var html = await sources.Html(url, sourceToken).ConfigureAwait(false);
                            pageUri = html.PageUri ?? uri;
                            await prepare((html.Candidates ?? new List<FaviconCandidate>()).Take(8), sourceToken).ConfigureAwait(false);
                        }, searchToken, result, sources.SourceTimeout).ConfigureAwait(false);
                        await RunSourceAsync(async sourceToken =>
                        {
                            onStatus("Checking favicon services…");
                            await prepare((await sources.External(pageUri, sourceToken).ConfigureAwait(false))
                                .OrderByDescending(c => c.Score).Take(8), sourceToken).ConfigureAwait(false);
                        }, searchToken, result, sources.SourceTimeout).ConfigureAwait(false);
                        await RunSourceAsync(async sourceToken =>
                        {
                            onStatus("Checking common icon locations…");
                            await prepare(await sources.WellKnown(new List<Uri> { uri, pageUri },
                                FaviconDiscoveryPreferences.MaxFallbackRequestsPerLookup, sourceToken).ConfigureAwait(false), sourceToken).ConfigureAwait(false);
                        }, searchToken, result, sources.SourceTimeout).ConfigureAwait(false);
                        await RunSourceAsync(async sourceToken =>
                        {
                            onStatus("Checking logo alternatives…");
                            await prepare(new[] { await sources.Logo(pageUri, sourceToken).ConfigureAwait(false) }, sourceToken).ConfigureAwait(false);
                        }, searchToken, result, sources.SourceTimeout).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    result.TimedOut = true;
                }
            }
            return result;
        }

        private static async Task RunSourceAsync(Func<CancellationToken, Task> action, CancellationToken token, AlternativeIconSearchResult result, TimeSpan timeout)
        {
            // A slow HTML candidate must not consume the entire alternative-search budget.
            using (var source = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                source.CancelAfter(timeout);
                try { source.Token.ThrowIfCancellationRequested(); await action(source.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); result.FailedSources++; }
                catch (Exception) { result.FailedSources++; }
            }
        }
    }
}
