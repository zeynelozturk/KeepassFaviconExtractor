using System;
using System.Drawing;

namespace FaviconExtractor
{
    internal static class FaviconScorer
    {
        public static int Score(string rel, string type, Size? bestSize)
        {
            int score = 0;
            score += ScoreRel(rel);
            score += ScoreType(type);
            score += ScoreSize(bestSize);
            return score;
        }

        public static int ApplyFallbackPenalty(int score)
        {
            return score - FaviconDiscoveryPreferences.FallbackScorePenalty;
        }

        public static int ApplyExternalServicePenalty(int score)
        {
            return score - FaviconDiscoveryPreferences.ExternalServiceScorePenalty;
        }

        public static int ApplyLogoPenalty(int score)
        {
            return score - FaviconDiscoveryPreferences.LogoScorePenalty;
        }

        private static int ScoreRel(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel))
            {
                return 0;
            }

            string normalized = " " + rel.ToLowerInvariant().Replace('\t', ' ') + " ";

            if (normalized.Contains(" icon "))
            {
                if (normalized.Contains(" shortcut "))
                {
                    return 350;
                }

                return 400;
            }

            if (normalized.Contains(" apple-touch-icon "))
            {
                return 200;
            }

            if (normalized.Contains(" apple-touch-icon-precomposed "))
            {
                return 180;
            }

            if (normalized.Contains(" mask-icon "))
            {
                return 140;
            }

            return 100;
        }

        private static int ScoreType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return 0;
            }

            string normalized = type.ToLowerInvariant();

            if (normalized.Contains("png")) return 30;
            if (normalized.Contains("svg")) return 25;
            if (normalized.Contains("icon") || normalized.Contains("ico")) return 20;
            if (normalized.Contains("jpeg") || normalized.Contains("jpg")) return 10;
            if (normalized.Contains("webp")) return 12;
            if (normalized.Contains("gif")) return 8;

            return 2;
        }

        private static int ScoreSize(Size? bestSize)
        {
            if (!bestSize.HasValue)
            {
                return 0;
            }

            int maxSide = Math.Max(bestSize.Value.Width, bestSize.Value.Height);

            int[] preferred = FaviconDiscoveryPreferences.PreferredIconSizes;
            for (int i = 0; i < preferred.Length; ++i)
            {
                if (maxSide == preferred[i])
                {
                    return (preferred.Length - i) * 100;
                }
            }

            return Math.Min(maxSide, 256);
        }
    }
}
