// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.IPv4;
using NUnit.Framework;

namespace Cosmos.Kernel.Tests.System.Network.IPv4;

[TestFixture]
public class Address4Test
{
    public class Parse : Address4Test
    {
        [Test]
        public void GivenCorrectSampleInDecimal_ReturnsCorrectAddress4()
        {
            const string source = "12.34.56.78";

            var actual = Address4.Parse(source, AddressParsingStyle.Dec);

            Assert.That(actual, Is.EqualTo(new Address4(12, 34, 56, 78)));
        }
        [Test]
        public void GivenCorrectSampleInHex_ReturnsCorrectAddress4()
        {
            const string source = "12.34.56.A0";

            var actual = Address4.Parse(source, AddressParsingStyle.Hex);

            Assert.That(actual, Is.EqualTo(new Address4(0x12, 0x34, 0x56, 0xA0)));
        }

        [TestCase("12.34.56")]
        [TestCase("12.34.56.78.99")]
        public void GivenInvalidDecSamples_ReturnsNull(string source)
        {
            var actual = Address4.Parse(source, AddressParsingStyle.Dec);

            Assert.That(actual, Is.Null);
        }
    }

    public class ToBytes : Address4Test
    {
        [Test]
        public void GivenSampleAddress_ToBytesIsCorrect()
        {
            var source = new Address4(0x12, 0x34, 0x56, 0xA0);

            var actual = source.ToBytes();

            Assert.That(actual, Is.EqualTo(new byte[] { 0x12, 0x34, 0x56, 0xA0 }));
        }
    }

    public new class ToString : Address4Test
    {
        [TestCase(0x123456A0u, ExpectedResult = "18.52.86.160")]
        public string GivenSampleAddress_ReturnsStringRepresentation(uint ip)
        {
            return new Address4(ip).ToString();
        }
    }

    public class CompareTo : Address4Test
    {
        [TestCase(0x123456A0u, 0x123456A0u, ExpectedResult = 0)]
        [TestCase(0x123456A1u, 0x123456A0u, ExpectedResult = 1)]
        [TestCase(0x123456A2u, 0x123456A0u, ExpectedResult = 1)]
        [TestCase(0x123456A0u, 0x123456A1u, ExpectedResult = -1)]
        [TestCase(0x123456A0u, 0x123456A2u, ExpectedResult = -1)]
        public int GivenSampleData_ReturnsCompareResult(uint a, uint b)
        {
            var addressA = new Address4(a);
            var addressB = new Address4(b);

            return addressA.CompareTo(addressB);
        }
    }
}
