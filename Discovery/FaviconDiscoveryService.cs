using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal static class FaviconDiscoveryService
    {
        private static readonly Regex BlockedStatusCodePattern = new Regex("\\b(401|403|429)\\b", RegexOptions.Compiled);

        public static async Task<HtmlFaviconDiscoveryResult> DiscoverAsync(string inputUrl)
        {
            Uri inputUri;
            if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out inputUri) ||
                (inputUri.Scheme != Uri.UriSchemeHttp && inputUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("The entry URL must be an absolute HTTP/HTTPS URL.");
            }

            HtmlFaviconDiscoveryResult level1Result = null;
            string level1Error = null;
            bool timedOut = false;
            string discoveryNote = null;

            using (CancellationTokenSource cts = new CancellationTokenSource(FaviconDiscoveryPreferences.TotalDiscoveryTimeout))
            {
                try
                {
                    level1Result = await HtmlFaviconDiscoverer.DiscoverAsync(inputUrl, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    level1Error = "Timed out while fetching page HTML.";
                }
                catch (Exception ex)
                {
                    level1Error = ex.Message;
                }

                List<FaviconCandidate> mergedCandidates = new List<FaviconCandidate>();
                Uri pageUri = inputUri;

                if (level1Result != null)
                {
                    pageUri = level1Result.PageUri ?? inputUri;
                    if (level1Result.Candidates != null)
                    {
                        mergedCandidates.AddRange(level1Result.Candidates);
                    }
                }

                if (mergedCandidates.Count == 0 && !timedOut)
                {
                    try
                    {
                        List<FaviconCandidate> externalCandidates = await ExternalFaviconServiceDiscoverer
                            .DiscoverAsync(pageUri, cts.Token)
                            .ConfigureAwait(false);
                        mergedCandidates.AddRange(externalCandidates);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = true;
                    }
                }

                if (mergedCandidates.Count == 0 && !timedOut)
                {
                    try
                    {
                        FaviconCandidate logoCandidate = await ExternalLogoDiscoverer
                            .DiscoverAsync(pageUri, cts.Token)
                            .ConfigureAwait(false);
                        if (logoCandidate != null)
                        {
                            mergedCandidates.Add(logoCandidate);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = true;
                    }
                }

                bool needsFallback = mergedCandidates.Count == 0;
                if (needsFallback && !timedOut)
                {
                    int maxFallbackProbes = FaviconDiscoveryPreferences.MaxFallbackRequestsPerLookup;
                    if (LooksLikeBlockedResponse(level1Error))
                    {
                        maxFallbackProbes = Math.Min(maxFallbackProbes, FaviconDiscoveryPreferences.MaxFallbackRequestsWhenBlocked);
                    }

                    List<Uri> bases = new List<Uri> { inputUri, pageUri };
                    try
                    {
                        List<FaviconCandidate> fallbackCandidates = await WellKnownFaviconDiscoverer
                            .DiscoverAsync(bases, maxFallbackProbes, cts.Token)
                            .ConfigureAwait(false);
                        mergedCandidates.AddRange(fallbackCandidates);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = true;
                    }
                }

                List<FaviconCandidate> ranked = mergedCandidates
                    .Where(c => c != null && c.IconUri != null)
                    .GroupBy(c => c.IconUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(c => c.Score).First())
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => c.IconUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (timedOut)
                {
                    discoveryNote = "Stopped due to total discovery timeout (" + (int)FaviconDiscoveryPreferences.TotalDiscoveryTimeout.TotalSeconds + "s).";
                }
                else if (LooksLikeBlockedResponse(level1Error))
                {
                    discoveryNote = "Level 1 appears blocked (401/403/429); fallback probes were limited.";
                }
                else if (mergedCandidates.Count == 0)
                {
                    discoveryNote = "No candidates from Level 1, well-known probes, external favicon services, or logo fallback.";
                }

                return new HtmlFaviconDiscoveryResult
                {
                    PageUri = pageUri,
                    Candidates = ranked,
                    BestCandidate = ranked.Count > 0 ? ranked[0] : null,
                    UsedFallback = needsFallback,
                    Level1Error = level1Error,
                    DiscoveryNote = discoveryNote
                };
            }
        }

        private static bool LooksLikeBlockedResponse(string level1Error)
        {
            return !string.IsNullOrWhiteSpace(level1Error) && BlockedStatusCodePattern.IsMatch(level1Error);
        }
    }
}
