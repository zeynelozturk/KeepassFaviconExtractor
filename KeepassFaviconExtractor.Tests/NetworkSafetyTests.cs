using System;
using System.Net;
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

    }
}
