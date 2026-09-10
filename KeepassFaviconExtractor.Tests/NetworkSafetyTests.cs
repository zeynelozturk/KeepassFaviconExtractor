using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FaviconExtractor
{
    [TestClass]
    public class NetworkSafetyTests
    {
        [TestMethod]
        public void IsPrivateOrLoopbackAddress_LoopbackIpv4_ReturnsTrue()
        {
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("127.0.0.1")));
        }

        [TestMethod]
        public void IsPrivateOrLoopbackAddress_PrivateIpv4_ReturnsTrue()
        {
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("192.168.1.20")));
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("10.12.0.8")));
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("172.20.5.5")));
        }

        [TestMethod]
        public void IsPrivateOrLoopbackAddress_LinkLocalIpv4_ReturnsTrue()
        {
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("169.254.10.10")));
        }

        [TestMethod]
        public void IsPrivateOrLoopbackAddress_PublicIpv4_ReturnsFalse()
        {
            Assert.IsFalse(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("8.8.8.8")));
        }

        [TestMethod]
        public void IsPrivateOrLoopbackAddress_LoopbackAndUniqueLocalIpv6_ReturnsTrue()
        {
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("::1")));
            Assert.IsTrue(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("fd12:3456:789a::1")));
        }

        [TestMethod]
        public void IsPrivateOrLoopbackAddress_GlobalIpv6_ReturnsFalse()
        {
            Assert.IsFalse(NetworkSafety.IsPrivateOrLoopbackAddress(IPAddress.Parse("2001:4860:4860::8888")));
        }

        [TestMethod]
        public async Task ComputeResponseSha256HexWithLimitAsync_SmallPayload_ReturnsExpectedHash()
        {
            byte[] payload = Encoding.UTF8.GetBytes("hello-security");
            using (ByteArrayContent content = new ByteArrayContent(payload))
            {
                string hash = await ExternalFaviconServiceDiscoverer
                    .ComputeResponseSha256HexWithLimitAsync(content, 1024, CancellationToken.None)
                    .ConfigureAwait(false);

                string expected = ComputeSha256Hex(payload);
                Assert.AreEqual(expected, hash);
            }
        }

        [TestMethod]
        public async Task ComputeResponseSha256HexWithLimitAsync_OversizedPayload_ReturnsNull()
        {
            byte[] payload = new byte[4096];
            using (ByteArrayContent content = new ByteArrayContent(payload))
            {
                string hash = await ExternalFaviconServiceDiscoverer
                    .ComputeResponseSha256HexWithLimitAsync(content, 512, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.IsNull(hash);
            }
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
