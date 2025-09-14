using Cosmos.Patcher;
using Cosmos.Tests.NativeWrapper;

namespace Cosmos.Tests.Scanner;

public class PlugScannerTests_FindPluggedAssemblies
{
    [Fact]
    public void FindPluggedAssemblies_ShouldReturnMatchingAssemblies()
    {
        // Arrange
        string plugPath = typeof(MockPlug).Assembly.Location;
        string targetAssembly = typeof(MockTarget).Assembly.Location;
        string unrelatedAssembly = typeof(string).Assembly.Location;

        PlugScanner scanner = new();

        // Act
        List<string> result = [.. scanner.FindPluggedAssemblies(new[] { plugPath }, new[] { targetAssembly, unrelatedAssembly })];

        // Assert
        Assert.Contains(targetAssembly, result);
        Assert.DoesNotContain(unrelatedAssembly, result);
    }
}
