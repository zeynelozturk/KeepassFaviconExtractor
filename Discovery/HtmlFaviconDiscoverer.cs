using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FaviconExtractor
{
    internal static class HtmlFaviconDiscoverer
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<HtmlFaviconDiscoveryResult> DiscoverAsync(string inputUrl)
        {
            Uri pageUri;
            if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out pageUri) ||
                (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("The entry URL must be an absolute HTTP/HTTPS URL.");
            }

            HttpResponseMessage response = await HttpClient.GetAsync(pageUri).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            Uri finalPageUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                ? response.RequestMessage.RequestUri
                : pageUri;

            string html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);

            Uri baseUri = ResolveBaseUri(document, finalPageUri);
            List<FaviconCandidate> candidates = ExtractCandidates(document, baseUri)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.IconUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new HtmlFaviconDiscoveryResult
            {
                PageUri = finalPageUri,
                Candidates = candidates,
                BestCandidate = candidates.Count > 0 ? candidates[0] : null
            };
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = FaviconDiscoveryPreferences.HtmlRequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FaviconExtractor/0.1");
            return client;
        }

        private static Uri ResolveBaseUri(HtmlAgilityPack.HtmlDocument document, Uri pageUri)
        {
            HtmlNode baseNode = document.DocumentNode.SelectSingleNode("//base[@href]");
            if (baseNode == null)
            {
                return pageUri;
            }

            string href = baseNode.GetAttributeValue("href", string.Empty);
            Uri resolved;
            if (TryResolveIconUri(pageUri, href, out resolved))
            {
                return resolved;
            }

            return pageUri;
        }

        private static IEnumerable<FaviconCandidate> ExtractCandidates(HtmlAgilityPack.HtmlDocument document, Uri baseUri)
        {
            HtmlNodeCollection linkNodes = document.DocumentNode.SelectNodes("//link[@href]");
            if (linkNodes == null)
            {
                yield break;
            }

            foreach (HtmlNode node in linkNodes)
            {
                string rel = node.GetAttributeValue("rel", string.Empty).Trim();
                if (!IsIconRelatedRel(rel))
                {
                    continue;
                }

                string href = node.GetAttributeValue("href", string.Empty).Trim();
                Uri iconUri;
                if (string.IsNullOrEmpty(href) || !TryResolveIconUri(baseUri, href, out iconUri))
                {
                    continue;
                }

                string type = node.GetAttributeValue("type", string.Empty).Trim();
                string sizes = node.GetAttributeValue("sizes", string.Empty).Trim();
                Size? bestSize = ParseBestSize(sizes);

                yield return new FaviconCandidate
                {
                    RelAttribute = rel,
                    TypeAttribute = type,
                    SizesAttribute = sizes,
                    IconUri = iconUri,
                    BestSize = bestSize,
                    Score = FaviconScorer.Score(rel, type, bestSize)
                };
            }
        }

        private static bool IsIconRelatedRel(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel))
            {
                return false;
            }

            string normalized = " " + rel.ToLowerInvariant().Replace('\t', ' ') + " ";
            if (normalized.Contains(" icon "))
            {
                return true;
            }

            return normalized.Contains(" apple-touch-icon ")
                || normalized.Contains(" apple-touch-icon-precomposed ")
                || normalized.Contains(" mask-icon ");
        }

        private static bool TryResolveIconUri(Uri baseUri, string href, out Uri resolved)
        {
            resolved = null;

            if (string.IsNullOrWhiteSpace(href))
            {
                return false;
            }

            href = href.Trim();

            if (href.StartsWith("//", StringComparison.Ordinal))
            {
                string absolute = baseUri.Scheme + ":" + href;
                return Uri.TryCreate(absolute, UriKind.Absolute, out resolved);
            }

            if (Uri.TryCreate(href, UriKind.Absolute, out resolved))
            {
                return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps;
            }

            if (Uri.TryCreate(baseUri, href, out resolved))
            {
                return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }

        private static Size? ParseBestSize(string sizesAttribute)
        {
            if (string.IsNullOrWhiteSpace(sizesAttribute))
            {
                return null;
            }

            string[] tokens = sizesAttribute.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Size? best = null;

            foreach (string token in tokens)
            {
                if (string.Equals(token, "any", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] parts = token.ToLowerInvariant().Split('x');
                int width;
                int height;
                if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height))
                {
                    continue;
                }

                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                Size current = new Size(width, height);
                if (!best.HasValue || (current.Width * current.Height) > (best.Value.Width * best.Value.Height))
                {
                    best = current;
                }
            }

            return best;
        }
    }
}
