// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.TCP;
using NUnit.Framework;

namespace Cosmos.Kernel.Tests.System.Network.IPv4.TCP;

public class TcpTest
{
    private Tcp target = null!;

    [SetUp]
    public void Setup()
    {
        target = Tcp.CreateConnection(0, 0, new Address(1, 2, 3, 4), new Address(1, 2, 3, 4));
    }

    [TestFixture]
    public class AppendToData : TcpTest
    {
        [Test]
        public void WhenBothData_AndOtherAreEmpty_DataIsEmpty()
        {
            target.AppendToData([]);

            Assert.That(target.Data.Length, Is.EqualTo(0));
        }

        [Test]
        public void WhenDataIsNotEmpty_AndOtherIsEmpty_DataDoesNotChange()
        {
            target.AppendToData([0,1]);

            target.AppendToData([]);

            Assert.That(target.Data.ToArray(), Is.EquivalentTo([0, 1]));
        }

        [Test]
        public void WhenDataIsNotEmpty_AndOtherIsNotEmpty_OtherIsAppendedToData()
        {
            target.AppendToData([0,1]);

            target.AppendToData([2, 3]);

            Assert.That(target.Data.ToArray(), Is.EquivalentTo([0, 1, 2, 3]));
        }
    }

    [TestFixture]
    public class AdvanceDataOffset : TcpTest
    {
        [Test]
        public void WhenAdvancingByZero_NoChangesAreMade()
        {
            target.AppendToData([0,1]);

            target.AdvanceDataOffset(0);

            Assert.That(target.Data.ToArray(), Is.EquivalentTo([0, 1]));
        }

        [Test]
        public void WhenAdvancingByOneAndLengthIsTwo_OnlyLastElementRemains()
        {
            target.AppendToData([0,1]);

            target.AdvanceDataOffset(1);

            Assert.That(target.Data.ToArray(), Is.EquivalentTo([1]));
        }
        [Test]
        public void WhenAdvancingByTwoAndLengthIsTwo_DataLengthIsZero()
        {
            target.AppendToData([0,1]);

            target.AdvanceDataOffset(2);

            Assert.That(target.Data.Length, Is.Zero);
        }
    }
}
