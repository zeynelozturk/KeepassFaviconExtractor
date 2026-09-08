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
        public void NormalizeToPng_WithNonSquareSvg_PadsToSquareWithoutTargetResize()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16' viewBox='0 0 32 16'><rect x='0' y='0' width='32' height='16' fill='#0000ff'/></svg>";
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);

            byte[] normalized = IconNormalizer.NormalizeToPng(svgBytes, "image/svg+xml", "https://example.test/favicon.svg");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(32, bitmap.Width);
                Assert.AreEqual(32, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(16, 16).B > 0, "Center should contain rendered SVG pixels.");
            }
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
    }
}
