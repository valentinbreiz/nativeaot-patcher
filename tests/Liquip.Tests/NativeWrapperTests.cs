using Liquip.NativeWrapper;
using Xunit;

namespace NativeLibrary.Tests.Tests;

public class NativeWrapperTests
{
    [Fact]
    public void Add_ShouldReturnCorrectResult()
    {
        // Act
        int resultNative = TestClass.NativeAdd(2, 3);
        int resultManaged = TestClass.ManagedAdd(2, 3);

        // Assert
        Assert.Equal(5, resultNative);
        Assert.Equal(5, resultManaged);
    }
}
