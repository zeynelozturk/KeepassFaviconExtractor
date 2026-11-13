using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FaviconExtractor
{
    internal sealed class PreparedIcon
    {
        public FaviconCandidate Candidate { get; private set; }
        public byte[] PngBytes { get; private set; }
        public Size? DecodedSize { get; private set; }
        public string ContentKey { get; private set; }

        public PreparedIcon(FaviconCandidate candidate, byte[] pngBytes, Size? decodedSize)
        {
            Candidate = candidate;
            PngBytes = pngBytes;
            DecodedSize = decodedSize;
            ContentKey = GetContentKey(pngBytes);
        }

        internal static string GetContentKey(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
    }

    // No KeePass objects or UI resources cross this boundary.
    internal static class CandidatePreparation
    {
        public static Task<byte[]> DownloadAsync(Uri uri, CancellationToken token, bool strictSafety = false)
        {
            return FaviconImageDownloader.DownloadAsync(uri, token, strictSafety);
        }

        public static PreparedIcon Normalize(FaviconCandidate candidate, byte[] sourceBytes, CancellationToken token)
        {
            Size? decodedSize;
            byte[] png;
            try
            {
                png = IconNormalizer.NormalizeToPng(sourceBytes, candidate.TypeAttribute,
                    candidate.IconUri.AbsoluteUri, token, out decodedSize);
            }
            catch (Exception ex) { throw new InvalidOperationException("Normalize failed: " + ex.Message, ex); }
            token.ThrowIfCancellationRequested();
            return new PreparedIcon(candidate, png, decodedSize);
        }

        public static async Task<PreparedIcon> PrepareAsync(FaviconCandidate candidate, CancellationToken token)
        {
            byte[] bytes = await DownloadAsync(candidate.IconUri, token).ConfigureAwait(false);
            return await Task.Run(() => Normalize(candidate, bytes, token), token).ConfigureAwait(false);
        }
    }
}
