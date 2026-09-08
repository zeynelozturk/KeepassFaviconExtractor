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
        private const int SmallHtmlIconThreshold = 24;
        private const int SmallHtmlIconScorePenalty = 260;
        private const int SmallOrUnknownIconThresholdForAppleTouchPromotion = 32;
        private const int AppleTouchPromotionMinSize = 120;
        private const int LargeAppleTouchScoreBonus = 0;

        public static async Task<HtmlFaviconDiscoveryResult> DiscoverAsync(string inputUrl)
        {
            return await DiscoverAsync(inputUrl, CancellationToken.None).ConfigureAwait(false);
        }

        public static async Task<HtmlFaviconDiscoveryResult> DiscoverAsync(string inputUrl, CancellationToken cancellationToken)
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

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(FaviconDiscoveryPreferences.TotalDiscoveryTimeout))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    level1Result = await HtmlFaviconDiscoverer.DiscoverAsync(inputUrl, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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

                if (ShouldTryExternalFallback(level1Result != null ? level1Result.Candidates : null) && !timedOut)
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
                        cancellationToken.ThrowIfCancellationRequested();
                        timedOut = true;
                    }
                }

                if (ShouldTryLogoFallback(mergedCandidates) && !timedOut)
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
                        cancellationToken.ThrowIfCancellationRequested();
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
                        cancellationToken.ThrowIfCancellationRequested();
                        timedOut = true;
                    }
                }

                List<FaviconCandidate> candidatesForRanking = mergedCandidates
                    .Where(c => c != null && c.IconUri != null)
                    .ToList();

                ApplyHtmlCandidatePreferenceAdjustments(candidatesForRanking);

                List<FaviconCandidate> ranked = candidatesForRanking
                    .Select(ApplySelectionAdjustments)
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

        internal static bool ShouldTryExternalFallback(IReadOnlyList<FaviconCandidate> htmlCandidates)
        {
            if (htmlCandidates == null || htmlCandidates.Count == 0)
            {
                return true;
            }

            FaviconCandidate bestHtml = htmlCandidates
                .Where(c => c != null && c.IconUri != null)
                .OrderByDescending(c => c.Score)
                .FirstOrDefault();

            if (bestHtml == null)
            {
                return true;
            }

            return IsSmallSizedIcon(bestHtml, SmallHtmlIconThreshold);
        }

        private static bool ShouldTryLogoFallback(List<FaviconCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return true;
            }

            foreach (FaviconCandidate candidate in candidates)
            {
                if (candidate != null && candidate.BestSize.HasValue)
                {
                    int maxSide = Math.Max(candidate.BestSize.Value.Width, candidate.BestSize.Value.Height);
                    if (maxSide >= 32)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static FaviconCandidate ApplySelectionAdjustments(FaviconCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            if (IsHtmlCandidate(candidate) && IsSmallSizedIcon(candidate, SmallHtmlIconThreshold))
            {
                candidate.Score -= SmallHtmlIconScorePenalty;
            }

            return candidate;
        }

        internal static bool ShouldPrioritizeLargeAppleTouchIcon(IReadOnlyList<FaviconCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            bool hasSmallOrUnknownHtmlIcon = false;
            bool hasLargeAppleTouch = false;

            foreach (FaviconCandidate candidate in candidates)
            {
                if (!IsHtmlCandidate(candidate))
                {
                    continue;
                }

                if (IsIconRelCandidate(candidate) && (IsSizeUnknown(candidate) || IsSmallSizedIcon(candidate, SmallOrUnknownIconThresholdForAppleTouchPromotion)))
                {
                    hasSmallOrUnknownHtmlIcon = true;
                }

                if (IsAppleTouchCandidate(candidate) && HasAtLeastSize(candidate, AppleTouchPromotionMinSize))
                {
                    hasLargeAppleTouch = true;
                }
            }

            return hasSmallOrUnknownHtmlIcon && hasLargeAppleTouch;
        }

        private static void ApplyHtmlCandidatePreferenceAdjustments(List<FaviconCandidate> candidates)
        {
            if (!ShouldPrioritizeLargeAppleTouchIcon(candidates))
            {
                return;
            }

            foreach (FaviconCandidate candidate in candidates)
            {
                if (IsHtmlCandidate(candidate) && IsAppleTouchCandidate(candidate) && HasAtLeastSize(candidate, AppleTouchPromotionMinSize))
                {
                    candidate.Score += LargeAppleTouchScoreBonus;
                }
            }
        }

        private static bool IsHtmlCandidate(FaviconCandidate candidate)
        {
            return candidate != null &&
                string.Equals(candidate.Source, "html-link", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIconRelCandidate(FaviconCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.RelAttribute))
            {
                return false;
            }

            string normalized = " " + candidate.RelAttribute.ToLowerInvariant().Replace('\t', ' ') + " ";
            return normalized.Contains(" icon ");
        }

        private static bool IsAppleTouchCandidate(FaviconCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.RelAttribute))
            {
                return false;
            }

            string normalized = " " + candidate.RelAttribute.ToLowerInvariant().Replace('\t', ' ') + " ";
            return normalized.Contains(" apple-touch-icon ")
                || normalized.Contains(" apple-touch-icon-precomposed ");
        }

        private static bool IsSizeUnknown(FaviconCandidate candidate)
        {
            return candidate == null || !candidate.BestSize.HasValue;
        }

        private static bool HasAtLeastSize(FaviconCandidate candidate, int minSize)
        {
            if (candidate == null || !candidate.BestSize.HasValue)
            {
                return false;
            }

            int maxSide = Math.Max(candidate.BestSize.Value.Width, candidate.BestSize.Value.Height);
            return maxSide >= minSize;
        }

        private static bool IsSmallSizedIcon(FaviconCandidate candidate, int maxSize)
        {
            if (candidate == null || !candidate.BestSize.HasValue)
            {
                return false;
            }

            int maxSide = Math.Max(candidate.BestSize.Value.Width, candidate.BestSize.Value.Height);
            return maxSide > 0 && maxSide <= maxSize;
        }
    }
}
