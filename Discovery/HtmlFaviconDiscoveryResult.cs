using System;
using System.Collections.Generic;

namespace FaviconExtractor
{
    internal sealed class HtmlFaviconDiscoveryResult
    {
        public Uri PageUri { get; set; }
        public List<FaviconCandidate> Candidates { get; set; }
        public FaviconCandidate BestCandidate { get; set; }
    }
}
