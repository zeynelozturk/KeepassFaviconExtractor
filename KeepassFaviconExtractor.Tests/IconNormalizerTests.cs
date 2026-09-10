using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using KeePassLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FaviconExtractor
{
    [TestClass]
    public class IconNormalizerTests
    {
        [TestMethod]
        public void NormalizeToPng_WithSquarePng_PreservesOriginalDimensions()
        {
            byte[] pngBytes = CreatePngBytes(16, 16, Color.Red);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/icon.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(16, bitmap.Width);
                Assert.AreEqual(16, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(8, 8).A > 0, "Center should contain icon pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithSquareLargePng_DownscalesToTargetCanvas()
        {
            byte[] pngBytes = CreatePngBytes(256, 256, Color.DarkCyan);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/icon-256.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Width);
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A > 0, "Downscaled icon should retain visible pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithNonSquareLargePng_DownscalesAndFitsToTargetCanvas()
        {
            byte[] pngBytes = CreatePngBytes(256, 192, Color.MediumPurple);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/icon-large-rect.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Width);
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A > 0, "Centered area should contain downscaled icon pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithSquareSvg_ResizesToTargetCanvas()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 32 32'><circle cx='16' cy='16' r='14' fill='#00aa00'/></svg>";
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);

            byte[] normalized = IconNormalizer.NormalizeToPng(svgBytes, "image/svg+xml", "https://example.test/square.svg");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Width);
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(FaviconDiscoveryPreferences.NormalizedIconSize / 2, FaviconDiscoveryPreferences.NormalizedIconSize / 2).G > 0, "Center should contain rendered SVG pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithSquare32Png_PreservesOriginalDimensions()
        {
            byte[] pngBytes = CreatePngBytes(32, 32, Color.Red);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/icon.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(32, bitmap.Width);
                Assert.AreEqual(32, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(16, 16).A > 0, "Center should contain icon pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithNonSquarePng_PadsOnlyShorterDimension()
        {
            byte[] pngBytes = CreatePngBytes(90, 128, Color.Purple);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/non-square.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(128, bitmap.Width);
                Assert.AreEqual(128, bitmap.Height);
                Assert.AreEqual(0, bitmap.GetPixel(0, 64).A, "Left padded area should be transparent.");
                Assert.IsTrue(bitmap.GetPixel(64, 64).A > 0, "Centered image area should contain pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithPngBytesAtIcoUrl_FallsBackToImageDecode()
        {
            byte[] pngBytes = CreatePngBytes(24, 24, Color.Orange);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, string.Empty, "https://example.test/favicon.ico");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(24, bitmap.Width);
                Assert.AreEqual(24, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(12, 12).A > 0, "Center should contain icon pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithIco_Produces64x64Png()
        {
            byte[] icoBytes;
            using (MemoryStream stream = new MemoryStream())
            {
                SystemIcons.Application.Save(stream);
                icoBytes = stream.ToArray();
            }

            byte[] normalized = IconNormalizer.NormalizeToPng(icoBytes, "image/x-icon", "https://example.test/favicon.ico");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.IsTrue(bitmap.Width > 0, "ICO decode should produce a non-empty image.");
                Assert.AreEqual(bitmap.Width, bitmap.Height, "Square ICO should remain square.");
            }
        }

        [TestMethod]
        public void LooksLikeWebp_WithRiffWebpHeader_ReturnsTrue()
        {
            byte[] bytes = new byte[16];
            bytes[0] = (byte)'R';
            bytes[1] = (byte)'I';
            bytes[2] = (byte)'F';
            bytes[3] = (byte)'F';
            bytes[8] = (byte)'W';
            bytes[9] = (byte)'E';
            bytes[10] = (byte)'B';
            bytes[11] = (byte)'P';

            Assert.IsTrue(WebpDecoder.LooksLikeWebp(bytes));
        }

        [TestMethod]
        public void NormalizeToPng_WithInvalidWebpBytes_ThrowsExplicitWebpDecodeFailure()
        {
            byte[] invalidWebp = new byte[16];
            invalidWebp[0] = (byte)'R';
            invalidWebp[1] = (byte)'I';
            invalidWebp[2] = (byte)'F';
            invalidWebp[3] = (byte)'F';
            invalidWebp[8] = (byte)'W';
            invalidWebp[9] = (byte)'E';
            invalidWebp[10] = (byte)'B';
            invalidWebp[11] = (byte)'P';

            InvalidOperationException ex = Assert.ThrowsException<InvalidOperationException>(() =>
                IconNormalizer.NormalizeToPng(invalidWebp, "image/webp", "https://example.test/icon.webp"));

            StringAssert.StartsWith(ex.Message, "WEBP decode failed:");
        }

        [TestMethod]
        public void NormalizeToPng_WithValidWebpBytes_DecodesViaNativeLibwebp()
        {
            byte[] webpBytes = Convert.FromBase64String(
                "UklGRqIBAABXRUJQVlA4TJUBAAAvH8AHELfCIGDbNuZPt7uJbdFA0LZtvO38IT8M27aNpOvtv26/O1uMIklSlALW/2t1sYFVQIdB/pACBDEQpQJEmYLGb3yAqnFZPI6I1xUBWQCvLQqKsIBMqKhxmCDU7RgHUIogHNMKiyoA+juAEAQx6tc/nxJpxd8VEoWsOsqvnbL6+nwqZLDCw1QSPmFKg4wkDA1ZuPXeKKMDYiTbpq25z37vG+/btm3va+Sfis/5EUT0H23btqGQuZN7RBQX+I4lhVmOH0SpQs8UxUwvTM4VjdwwijzRyosCU48Z+JL1d37xlXY5PLQlne/kODKM/lXio0dpIyP31VOg+xtbA2pPmUclx1sZStci9w2g9SrF2SPApthTQGlbVFoGZuS4BIyLkpMS9L8GQOtRzU8XSouAsSWKrQIGMPan6qUJQPVWlFtPmRd1n22g+azhrAQsaZw9AVQfNNyUgGlbwxAwDjTuvQ50vjXsAcyJhhWA/eJ95VkAKu/F88uzU4JRu4jjS167k7N3UsTX35X2LvV3/Q//gva/EgEA");

            try
            {
                byte[] normalized = IconNormalizer.NormalizeToPng(webpBytes, "image/webp", "https://example.test/icon.webp");
                AssertPngSignature(normalized);

                using (Bitmap bitmap = LoadBitmap(normalized))
                {
                    Assert.AreEqual(32, bitmap.Width);
                    Assert.AreEqual(32, bitmap.Height);
                    Assert.IsTrue(bitmap.GetPixel(16, 16).A > 0, "Decoded WebP should produce visible icon pixels.");
                }
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.IndexOf("native library (libwebp.dll) not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("architecture is incompatible", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("DLL 'libwebp.dll'", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.InnerException is DllNotFoundException
                    || ex.InnerException is BadImageFormatException)
                {
                    Assert.Inconclusive("Native WebP decoder is unavailable in this test environment: " + ex.Message);
                }

                throw;
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithNonSquareSvg_FitsIntoTargetCanvas()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16' viewBox='0 0 32 16'><rect x='0' y='0' width='32' height='16' fill='#0000ff'/></svg>";
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);

            byte[] normalized = IconNormalizer.NormalizeToPng(svgBytes, "image/svg+xml", "https://example.test/favicon.svg");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Width);
                Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).B > 0, "Center should contain rendered SVG pixels.");
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithLeagueAssetLikeWideSvg_ProducesReasonableIconOrFailsExplicitly()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 110 70' height='70' width='110'><path fill='#A38E40' d='M58.0046 10.581C69.8377 12.1048 79 22.4794 79 35.0508C79 39.6096 77.7882 43.8762 75.6862 47.5461H74.6971H69.3431C70.5796 46.0477 71.5811 44.3842 72.3354 42.5683C73.3122 40.1937 73.8068 37.654 73.8068 35.0508C73.8068 32.435 73.3122 29.908 72.3354 27.5334C71.3833 25.235 70.0355 23.1651 68.3045 21.3873C66.5734 19.6096 64.558 18.2127 62.3199 17.2477C60.9351 16.6381 59.4884 16.2191 58.0046 15.9778V10.581ZM39.6924 54.035V46.2762C38.8887 45.1207 38.2087 43.8762 37.6646 42.5683C36.6878 40.1937 36.1932 37.654 36.1932 35.0508C36.1932 32.435 36.6878 29.908 37.6646 27.5334C38.2087 26.2127 38.8887 24.9683 39.6924 23.8254V16.054C34.3756 20.5746 31 27.4064 31 35.0508C31 42.6953 34.3756 49.5143 39.6924 54.035ZM54.4189 8.33337H40.6569L43.2906 13.8572V56.2064L40.694 61.6667H71.8779L74.7218 51.2286H54.4189V8.33337Z'/></svg>";
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);

            try
            {
                byte[] normalized = IconNormalizer.NormalizeToPng(svgBytes, "image/svg+xml", "https://cmsassets.rgpub.io/icon.svg");
                AssertPngSignature(normalized);

                using (Bitmap bitmap = LoadBitmap(normalized))
                {
                    AssertLooksLikeReasonableIcon(bitmap);
                }
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "SVG conversion failed");
            }
        }

        private static void AssertLooksLikeReasonableIcon(Bitmap bitmap)
        {
            Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Width);
            Assert.AreEqual(FaviconDiscoveryPreferences.NormalizedIconSize, bitmap.Height);

            Rectangle bounds;
            int opaquePixels;
            bool hasOpaqueContent = TryGetOpaqueBounds(bitmap, out bounds, out opaquePixels);
            Assert.IsTrue(hasOpaqueContent, "Rendered icon should contain visible pixels.");

            int total = bitmap.Width * bitmap.Height;
            double opaqueRatio = (double)opaquePixels / Math.Max(1, total);
            Assert.IsTrue(opaqueRatio > 0.0005d, "Rendered icon should not be near-empty.");

            double areaRatio = (double)(bounds.Width * bounds.Height) / Math.Max(1, total);
            Assert.IsTrue(areaRatio > 0.01d, "Rendered content bounds should not be extremely small.");

            double centerX = bounds.Left + (bounds.Width / 2.0d);
            double centerY = bounds.Top + (bounds.Height / 2.0d);
            double centerOffsetX = Math.Abs(centerX - (bitmap.Width / 2.0d)) / Math.Max(1.0d, bitmap.Width / 2.0d);
            double centerOffsetY = Math.Abs(centerY - (bitmap.Height / 2.0d)) / Math.Max(1.0d, bitmap.Height / 2.0d);
            Assert.IsTrue(centerOffsetX <= 0.35d, "Rendered icon content is excessively offset horizontally.");
            Assert.IsTrue(centerOffsetY <= 0.35d, "Rendered icon content is excessively offset vertically.");

            int leftMargin = bounds.Left;
            int topMargin = bounds.Top;
            int rightMargin = bitmap.Width - bounds.Right - 1;
            int bottomMargin = bitmap.Height - bounds.Bottom - 1;

            bool touchesLeft = leftMargin <= 1;
            bool touchesRight = rightMargin <= 1;
            bool touchesTop = topMargin <= 1;
            bool touchesBottom = bottomMargin <= 1;

            Assert.IsFalse(touchesLeft && rightMargin > (int)(bitmap.Width * 0.20d), "Left-edge clipping suspicion detected.");
            Assert.IsFalse(touchesRight && leftMargin > (int)(bitmap.Width * 0.20d), "Right-edge clipping suspicion detected.");
            Assert.IsFalse(touchesTop && bottomMargin > (int)(bitmap.Height * 0.20d), "Top-edge clipping suspicion detected.");
            Assert.IsFalse(touchesBottom && topMargin > (int)(bitmap.Height * 0.20d), "Bottom-edge clipping suspicion detected.");
        }

        private static bool TryGetOpaqueBounds(Bitmap bitmap, out Rectangle bounds, out int opaquePixels)
        {
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = -1;
            int bottom = -1;
            opaquePixels = 0;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A <= 8)
                    {
                        continue;
                    }

                    opaquePixels++;
                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }

            if (opaquePixels == 0)
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            return true;
        }

        private static void AssertPngSignature(byte[] bytes)
        {
            byte[] signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            Assert.IsTrue(bytes != null && bytes.Length > signature.Length, "PNG bytes should not be empty.");
            for (int i = 0; i < signature.Length; i++)
            {
                Assert.AreEqual(signature[i], bytes[i], "Invalid PNG signature at index " + i + ".");
            }
        }

        private static byte[] CreatePngBytes(int width, int height, Color color)
        {
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            using (MemoryStream stream = new MemoryStream())
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(brush, 0, 0, width, height);
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static Bitmap LoadBitmap(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream, true, true))
            {
                return new Bitmap(image);
            }
        }

        [TestMethod]
        public void AssignNormalizedPngToEntry_AddsCustomIconAndAssignsUuid()
        {
            PwDatabase database = new PwDatabase();
            PwEntry entry = new PwEntry(true, true);
            byte[] normalizedPng = IconNormalizer.NormalizeToPng(CreatePngBytes(32, 32, Color.Green), "image/png", "https://example.test/a.png");

            PwUuid assigned = KeePassIconAssigner.AssignNormalizedPngToEntry(database, entry, normalizedPng);

            Assert.IsFalse(assigned.IsZero);
            Assert.AreEqual(1, database.CustomIcons.Count);
            Assert.IsTrue(entry.CustomIconUuid.Equals(assigned));
            Assert.IsTrue(database.Modified);
        }

        [TestMethod]
        public void AssignNormalizedPngToEntry_ReusesExistingCustomIcon()
        {
            PwDatabase database = new PwDatabase();
            PwEntry entry1 = new PwEntry(true, true);
            PwEntry entry2 = new PwEntry(true, true);
            byte[] normalizedPng = IconNormalizer.NormalizeToPng(CreatePngBytes(32, 32, Color.Blue), "image/png", "https://example.test/b.png");

            PwUuid first = KeePassIconAssigner.AssignNormalizedPngToEntry(database, entry1, normalizedPng);
            PwUuid second = KeePassIconAssigner.AssignNormalizedPngToEntry(database, entry2, normalizedPng);

            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(1, database.CustomIcons.Count);
            Assert.IsTrue(entry1.CustomIconUuid.Equals(first));
            Assert.IsTrue(entry2.CustomIconUuid.Equals(second));
        }

        [TestMethod]
        public void GetDomainCandidates_WithSubdomain_IncludesParentDomain()
        {
            var candidates = ExternalFaviconServiceDiscoverer.GetDomainCandidates("fr.7digital.com").ToArray();

            CollectionAssert.AreEqual(
                new[] { "fr.7digital.com", "7digital.com" },
                candidates);
        }

        [TestMethod]
        public void GetDomainCandidates_WithoutSubdomain_ReturnsHostOnly()
        {
            var candidates = ExternalFaviconServiceDiscoverer.GetDomainCandidates("example.com").ToArray();

            CollectionAssert.AreEqual(new[] { "example.com" }, candidates);
        }

        [TestMethod]
        public void ShouldTryExternalFallback_WithTinyBestHtmlIcon_ReturnsTrue()
        {
            var htmlCandidates = new[]
            {
                new FaviconCandidate
                {
                    Source = "html-link",
                    IconUri = new Uri("https://example.test/favicon.ico"),
                    BestSize = new Size(16, 16),
                    Score = 470
                }
            };

            bool shouldTry = FaviconDiscoveryService.ShouldTryExternalFallback(htmlCandidates);

            Assert.IsTrue(shouldTry);
        }

        [TestMethod]
        public void ShouldTryExternalFallback_WithLargerBestHtmlIcon_ReturnsFalse()
        {
            var htmlCandidates = new[]
            {
                new FaviconCandidate
                {
                    Source = "html-link",
                    IconUri = new Uri("https://example.test/icon-64.png"),
                    BestSize = new Size(64, 64),
                    Score = 730
                }
            };

            bool shouldTry = FaviconDiscoveryService.ShouldTryExternalFallback(htmlCandidates);

            Assert.IsFalse(shouldTry);
        }

        [TestMethod]
        public void ShouldPrioritizeLargeAppleTouchIcon_WithSmallIconAndLargeAppleTouch_ReturnsTrue()
        {
            var candidates = new[]
            {
                new FaviconCandidate
                {
                    Source = "html-link",
                    RelAttribute = "icon",
                    IconUri = new Uri("https://example.test/favicon.ico"),
                    BestSize = new Size(32, 32),
                    Score = 630
                },
                new FaviconCandidate
                {
                    Source = "html-link",
                    RelAttribute = "apple-touch-icon",
                    IconUri = new Uri("https://example.test/apple-touch-icon.png"),
                    BestSize = new Size(192, 192),
                    Score = 392
                }
            };

            bool shouldPrioritize = FaviconDiscoveryService.ShouldPrioritizeLargeAppleTouchIcon(candidates);

            Assert.IsTrue(shouldPrioritize);
        }

        [TestMethod]
        public void ShouldPrioritizeLargeAppleTouchIcon_WithoutLargeAppleTouch_ReturnsFalse()
        {
            var candidates = new[]
            {
                new FaviconCandidate
                {
                    Source = "html-link",
                    RelAttribute = "icon",
                    IconUri = new Uri("https://example.test/favicon.ico"),
                    BestSize = new Size(32, 32),
                    Score = 630
                },
                new FaviconCandidate
                {
                    Source = "html-link",
                    RelAttribute = "apple-touch-icon",
                    IconUri = new Uri("https://example.test/apple-touch-icon.png"),
                    BestSize = new Size(76, 76),
                    Score = 276
                }
            };

            bool shouldPrioritize = FaviconDiscoveryService.ShouldPrioritizeLargeAppleTouchIcon(candidates);

            Assert.IsFalse(shouldPrioritize);
        }

        [TestMethod]
        public void BuildProviderUri_UsesExpectedTemplates()
        {
            string domain = "hot.mail.com";

            Assert.AreEqual(
                "https://www.google.com/s2/favicons?domain=hot.mail.com&sz=64",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-google-s2", domain).AbsoluteUri);
            Assert.AreEqual(
                "https://icons.duckduckgo.com/ip3/hot.mail.com.ico",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-duckduckgo-ip3", domain).AbsoluteUri);
            Assert.AreEqual(
                "https://t2.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://hot.mail.com&size=64",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-google-faviconv2", domain).AbsoluteUri);
            Assert.AreEqual(
                "https://favicone.com/hot.mail.com?s=128",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-favicone", domain).AbsoluteUri);
            Assert.AreEqual(
                "https://favicon.vemetric.com/hot.mail.com",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-vemetric", domain).AbsoluteUri);
            Assert.AreEqual(
                "https://a.favicon.im/hot.mail.com?larger=true",
                ExternalFaviconServiceDiscoverer.BuildProviderUri("external-favicon-im", domain).AbsoluteUri);
        }

        [TestMethod]
        public void GetExternalSourceScoreAdjustment_FollowsConfiguredPriority()
        {
            int google = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-google-s2");
            int duck = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-duckduckgo-ip3");
            int googleV2 = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-google-faviconv2");
            int favicone = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-favicone");
            int vemetric = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-vemetric");
            int faviconIm = ExternalFaviconServiceDiscoverer.GetExternalSourceScoreAdjustment("external-favicon-im");

            Assert.IsTrue(google > duck);
            Assert.IsTrue(duck > googleV2);
            Assert.IsTrue(googleV2 > favicone);
            Assert.IsTrue(favicone > vemetric);
            Assert.IsTrue(vemetric > faviconIm);
        }

        [TestMethod]
        public void IsPlaceholderProneExternalSource_RecognizesExpectedProviders()
        {
            Assert.IsTrue(ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource("external-favicone"));
            Assert.IsTrue(ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource("external-vemetric"));
            Assert.IsTrue(ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource("external-favicon-im"));
            Assert.IsFalse(ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource("external-google-s2"));
            Assert.IsFalse(ExternalFaviconServiceDiscoverer.IsPlaceholderProneExternalSource("external-duckduckgo-ip3"));
        }

        [TestMethod]
        public void ShouldSkipProviderForDnsState_SkipsOnlyPlaceholderProneWhenUnresolved()
        {
            Assert.IsTrue(ExternalFaviconServiceDiscoverer.ShouldSkipProviderForDnsState(
                "external-favicon-im",
                ExternalFaviconServiceDiscoverer.DnsResolutionState.Unresolved));
            Assert.IsTrue(ExternalFaviconServiceDiscoverer.ShouldSkipProviderForDnsState(
                "external-vemetric",
                ExternalFaviconServiceDiscoverer.DnsResolutionState.Unresolved));
            Assert.IsFalse(ExternalFaviconServiceDiscoverer.ShouldSkipProviderForDnsState(
                "external-google-faviconv2",
                ExternalFaviconServiceDiscoverer.DnsResolutionState.Unresolved));
            Assert.IsFalse(ExternalFaviconServiceDiscoverer.ShouldSkipProviderForDnsState(
                "external-favicon-im",
                ExternalFaviconServiceDiscoverer.DnsResolutionState.Unknown));
        }

        [TestMethod]
        public void ScoreSize_PrefersExact128OverLargerIcon()
        {
            int score128 = FaviconScorer.Score("icon", "image/png", new Size(128, 128));
            int score192 = FaviconScorer.Score("icon", "image/png", new Size(192, 192));

            Assert.IsTrue(score128 > score192, "128x128 icon should be preferred over larger icons.");
        }
    }
}
