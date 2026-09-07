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
        public void NormalizeToPng_WithSmallPng_Produces64x64PngWithoutForcedUpscale()
        {
            byte[] pngBytes = CreatePngBytes(16, 16, Color.Red);

            byte[] normalized = IconNormalizer.NormalizeToPng(pngBytes, "image/png", "https://example.test/icon.png");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(64, bitmap.Width);
                Assert.AreEqual(64, bitmap.Height);

                Assert.AreEqual(0, bitmap.GetPixel(0, 0).A, "Corner should remain transparent.");
                Assert.AreEqual(0, bitmap.GetPixel(20, 20).A, "Area outside centered 16x16 icon should remain transparent.");
                Assert.IsTrue(bitmap.GetPixel(32, 32).A > 0, "Center should contain icon pixels.");
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
                Assert.AreEqual(64, bitmap.Width);
                Assert.AreEqual(64, bitmap.Height);
            }
        }

        [TestMethod]
        public void NormalizeToPng_WithSvg_Produces64x64Png()
        {
            const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16' viewBox='0 0 32 16'><rect x='0' y='0' width='32' height='16' fill='#0000ff'/></svg>";
            byte[] svgBytes = Encoding.UTF8.GetBytes(svg);

            byte[] normalized = IconNormalizer.NormalizeToPng(svgBytes, "image/svg+xml", "https://example.test/favicon.svg");
            AssertPngSignature(normalized);

            using (Bitmap bitmap = LoadBitmap(normalized))
            {
                Assert.AreEqual(64, bitmap.Width);
                Assert.AreEqual(64, bitmap.Height);
                Assert.IsTrue(bitmap.GetPixel(32, 32).B > 0, "Center should contain rendered SVG pixels.");
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
    }
}
