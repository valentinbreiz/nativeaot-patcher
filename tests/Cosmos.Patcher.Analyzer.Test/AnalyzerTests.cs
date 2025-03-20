using System.Collections.Immutable;
using Cosmos.API.Attributes;
using Cosmos.Patcher.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Cosmos.Patcher.Analyzer.Tests;

public class AnalyzerTestsTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference CosmosAPIReference = MetadataReference.CreateFromFile(typeof(Plug).Assembly.Location);

    private static readonly PatcherAnalyzer Analyzer = new();



    [Fact]
    public async Task Test_TypeNotFoundDiagnostic()
    {
        const string code = """

using System;
using Cosmos.API.Attributes;

namespace ConsoleApplication1
{
    [Plug("System.NonExistent", IsOptional = false)]
    public static class Test
    {
        [DllImport("example.dll")]
        public static extern void ExternalMethod();
    }
}
""";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.TypeNotFound.Id && d.GetMessage().Contains("System.NonExistent"));
    }

    [Fact]
    public async Task Test_PlugNotStaticDiagnostic()
    {
        const string code = """

using System;
using Cosmos.API.Attributes;

namespace ConsoleApplication1
{
    [Plug("System.String", IsOptional = true)]
    public class Test
    {
        [DllImport("example.dll")]
        public static extern void ExternalMethod();
    }
}
""";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.PlugNotStatic.Id && d.GetMessage().Contains("Test"));
    }

    [Fact]
    public async Task Test_MethodNeedsPlugDiagnostic()
    {
        const string code = """

using System;
using System.Runtime.CompilerServices;
using Cosmos.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
        [DllImport("example.dll")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }

    [Plug("ConsoleApplication1.TestNativeType", IsOptional = false)]
    public static class Test
    {
    }
}
""";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
    }

    [Fact]
    public async Task Test_AnalyzeAccessedMember()
    {
        const string code = """

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
        [DllImport("example.dll")]
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
}
""";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("ExternalMethod"));
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
    }

    [Fact]
    public async Task Test_MethodNotImplemented()
    {
        const string code = """

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
        [DllImport("example.dll")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }

    [Plug("ConsoleApplication1.TestNativeType", IsOptional = false)]
    public static class Test
    {
    
        public static void NotImplemented(){}
    }
}
""";
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Contains(diagnostics, d => d.Id == DiagnosticMessages.MethodNotImplemented.Id && d.GetMessage().Contains("NotImplemented"));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestCompilation", [syntaxTree], [CorlibReference, CosmosAPIReference]);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(Analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }
    
}
