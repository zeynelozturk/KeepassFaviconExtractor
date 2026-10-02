using FaviconExtractor.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeepassFaviconExtractor.Tests
{
    [TestClass]
    public class AndroidAppIdentifierTests
    {
        [TestMethod]
        public void TryGetPackage_UrlValueHasPriorityOverAndroidAppField()
        {
            string package;

            bool found = AndroidAppIdentifier.TryGetPackage(
                "androidapp://com.url.app",
                "androidapp://com.field.app",
                out package);

            Assert.IsTrue(found);
            Assert.AreEqual("com.url.app", package);
        }

        [TestMethod]
        public void TryGetPackage_UsesAndroidAppFieldWhenUrlIsEmpty()
        {
            string package;

            bool found = AndroidAppIdentifier.TryGetPackage(
                string.Empty,
                "androidapp://tr.gov.saglik.enabiz",
                out package);

            Assert.IsTrue(found);
            Assert.AreEqual("tr.gov.saglik.enabiz", package);
        }

        [TestMethod]
        public void TryParse_IsCaseInsensitiveAndTrimsValue()
        {
            string package;

            bool found = AndroidAppIdentifier.TryParse("  ANDROIDAPP://com.example.app  ", out package);

            Assert.IsTrue(found);
            Assert.AreEqual("com.example.app", package);
        }

        [TestMethod]
        public void TryGetPackage_DoesNotUseAndroidAppFieldWhenUrlIsOrdinary()
        {
            string package;

            bool found = AndroidAppIdentifier.TryGetPackage(
                "https://example.com",
                "androidapp://com.field.app",
                out package);

            Assert.IsFalse(found);
            Assert.IsNull(package);
        }

        [TestMethod]
        public void TryParse_RejectsMissingPackageOrPathLikeValues()
        {
            string package;

            Assert.IsFalse(AndroidAppIdentifier.TryParse("androidapp://", out package));
            Assert.IsFalse(AndroidAppIdentifier.TryParse("androidapp://com.example/app", out package));
            Assert.IsFalse(AndroidAppIdentifier.TryParse("https://example.com", out package));
        }
    }
}
