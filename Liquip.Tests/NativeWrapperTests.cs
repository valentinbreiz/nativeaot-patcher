using Liquip.NativeLibrary.Tests.PlugSample;
using Xunit;

namespace NativeLibrary.Tests.Tests
{
    public class NativeWrapperTests
    {
        [Fact]
        public void Add_ShouldReturnCorrectResult()
        {
            // Act
            var resultNative = NativeWrapper.NativeAdd(2, 3);
            var resultManaged = NativeWrapper.ManagedAdd(2, 3);

            // Assert
            Assert.Equal(5, resultNative);
            Assert.Equal(5, resultManaged);
        }
    }
}
