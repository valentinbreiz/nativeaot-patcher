using System.Collections.Immutable;
using Liquip.API.Attributes;
using Liquip.Patcher.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Liquip.Patcher.Analyzer.Tests;

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
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.TypeNotFound.Id && d.GetMessage().Contains("System.NonExistent"));
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
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.PlugNotStatic.Id && d.GetMessage().Contains("Test"));
    }

    [Fact]
    public async Task Test_MethodNeedsPlugDiagnostic()
    {
        string code = @"
using System;
using System.Runtime.CompilerServices;
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }

    [Plug(""ConsoleApplication1.TestNativeType"", IsOptional = false)]
    public static class Test
    {
    }
}";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
    }

    [Fact]
    public async Task Test_AnalyzeAccessedMember()
    {
        string code = @"
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }

    public class Test
    {
        public void TestMethod()
        {
            TestNativeType.ExternalMethod();
            TestNativeType.NativeMethod();
        }
    }
}";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("ExternalMethod"));
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);
        CSharpCompilation compilation = CSharpCompilation.Create("TestCompilation")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddReferences(MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        PatcherAnalyzer analyzer = new();
        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
