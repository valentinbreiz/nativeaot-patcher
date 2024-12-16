using Liquip.NativeWrapper;
using Xunit;

namespace NativeLibrary.Tests.Tests
{
    public class NativeWrapperTests
    {
        [Fact]
        public void Add_ShouldReturnCorrectResult()
        {
            // Act
            var resultNative = TestClass.NativeAdd(2, 3);
            var resultManaged = TestClass.ManagedAdd(2, 3);

            // Assert
            Assert.Equal(5, resultNative);
            Assert.Equal(5, resultManaged);
        }
    }
}
