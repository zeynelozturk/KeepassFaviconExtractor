using System;
using System.Drawing;

namespace FaviconExtractor
{
    internal sealed class FaviconCandidate
    {
        public string Source { get; set; }
        public string RelAttribute { get; set; }
        public string TypeAttribute { get; set; }
        public string SizesAttribute { get; set; }
        public Uri IconUri { get; set; }
        public Size? BestSize { get; set; }
        public int Score { get; set; }
    }
}
