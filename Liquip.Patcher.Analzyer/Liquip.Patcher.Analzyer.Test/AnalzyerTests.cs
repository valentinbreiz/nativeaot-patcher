using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Liquip.Patcher.Analzyer;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using Liquip.API.Attributes;

namespace AnalyzerTest;

public class AnalyzerTestsTest
{
    [Fact]
    public async Task Test_TypeNotFoundDiagnostic()
    {
        string code = @"
using System;
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    [Plug(""System.NonExistent"", IsOptional = false)]
    public static class Test
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();
    }
}";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == "NAOT0001" && d.GetMessage().Contains("System.NonExistent"));
    }

    [Fact]
    public async Task Test_PlugNotStaticDiagnostic()
    {
        string code = @"
using System;
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    [Plug(""System.String"", IsOptional = true)]
    public class Test
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();
    }
}";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == "NAOT0003" && d.GetMessage().Contains("Test"));
    }

    [Fact]
    public async Task Test_MethodNeedsPlugDiagnostic()
    {
        string code = @"
using System;
using System.Runtime.CompilerServices;
using Liquip.API.Attributes;

namespace ConsoleApplication1;

    public class TestNativeType
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();
    }

    [Plug(""ConsoleApplication1.TestNativeType"", IsOptional = false)]
    public static class Test
    {

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }
";
        Console.WriteLine(code);
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        foreach (var diagnostic in diagnostics)
        {
            Console.WriteLine(diagnostic.GetMessage());
        }
        Assert.Contains(diagnostics, d => d.Id == "NAOT0002" && d.GetMessage().Contains("ExternalMethod"));
    }



    private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);
        CSharpCompilation compilation = CSharpCompilation.Create("TestCompilation")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddReferences(MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        PatcherAnalyzer analyzer = new();
        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}

