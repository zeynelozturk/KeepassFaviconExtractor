using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FaviconExtractor
{
    internal static class HtmlFaviconDiscoverer
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<HtmlFaviconDiscoveryResult> DiscoverAsync(string inputUrl, CancellationToken cancellationToken)
        {
            Uri pageUri;
            if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out pageUri) ||
                (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("The entry URL must be an absolute HTTP/HTTPS URL.");
            }

            string html;
            Uri finalPageUri;
            using (HttpResponseMessage response = await HttpClient.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                finalPageUri = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                    ? response.RequestMessage.RequestUri
                    : pageUri;

                if (FaviconDiscoveryPreferences.EnforcePrivateAddressBlocking
                    && await NetworkSafety.IsPrivateOrLoopbackUriAsync(finalPageUri, FaviconDiscoveryPreferences.FallbackProbeTimeout, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Final HTML URL resolves to a private or loopback address.");
                }

                html = await ReadResponseTextWithLimitAsync(response.Content, FaviconDiscoveryPreferences.MaxHtmlReadBytes, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

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

        private static async Task<string> ReadResponseTextWithLimitAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
        {
            if (content == null)
            {
                return string.Empty;
            }

            if (content.Headers != null && content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > maxBytes)
            {
                throw new InvalidOperationException("HTML response is larger than configured limit.");
            }

            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int totalRead = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    totalRead += read;
                    if (totalRead > maxBytes)
                    {
                        throw new InvalidOperationException("HTML response is larger than configured limit.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                Encoding encoding = GetEncoding(content);
                return encoding.GetString(buffer.ToArray());
            }
        }

        private static Encoding GetEncoding(HttpContent content)
        {
            string charSet = content != null
                && content.Headers != null
                && content.Headers.ContentType != null
                ? content.Headers.ContentType.CharSet
                : null;

            if (!string.IsNullOrWhiteSpace(charSet))
            {
                try
                {
                    return Encoding.GetEncoding(charSet.Trim('"'));
                }
                catch
                {
                }
            }

            return Encoding.UTF8;
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
                    Source = "html-link",
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
