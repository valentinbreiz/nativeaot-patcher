using System.IO;
using System.Linq;
using Liquip.XSharp.SourceGenerator.Tests.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Liquip.XSharp.SourceGenerator.Tests;

public class SampleSourceGeneratorTests
{
    private const string DddRegistryText = @"User
Document
Customer";

    [Fact]
    public void GenerateClassesBasedOnDDDRegistry()
    {
        // Create an instance of the source generator.
        SampleSourceGenerator? generator = new();

        // Source generators should be tested using 'GeneratorDriver'.
        var driver = CSharpGeneratorDriver.Create([generator],
            [
                // Add the additional file separately from the compilation.
                new TestAdditionalFile("./DDD.UbiquitousLanguageRegistry.txt", DddRegistryText)
            ]);

        // To run generators, we can use an empty compilation.
        CSharpCompilation? compilation = CSharpCompilation.Create(nameof(SampleSourceGeneratorTests));

        // Run generators. Don't forget to use the new compilation rather than the previous one.
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation? newCompilation, out _);

        // Retrieve all files in the compilation.
        string[]? generatedFiles = newCompilation.SyntaxTrees
            .Select(t => Path.GetFileName(t.FilePath))
            .ToArray();

        // In this case, it is enough to check the file name.
        Assert.Equal(new [] { "User.g.cs", "Document.g.cs", "Customer.g.cs" }, generatedFiles);
    }
}
