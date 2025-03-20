using System.Collections.Immutable;
using Cosmos.API.Attributes;
using Cosmos.Patcher.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Cosmos.Patcher.Analyzer.Tests;

public class AnalyzerTests
{
    private static readonly MetadataReference CorlibReference =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private static readonly MetadataReference PlugAttributeReference =
        MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location);

    private static SyntaxTree ParseCode(string code) => CSharpSyntaxTree.ParseText(code);


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
        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.TypeNotFound.Id && d.GetMessage().Contains("System.NonExistent"));
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
        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.PlugNotStatic.Id && d.GetMessage().Contains("Test"));
    }


    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        SyntaxTree syntaxTree = ParseCode(code);

        CSharpCompilation compilation = CSharpCompilation.Create("TestCompilation")
            .AddReferences(CorlibReference, PlugAttributeReference)
            .AddSyntaxTrees(syntaxTree);

        PatcherAnalyzer analyzer = new();
        CompilationWithAnalyzers compilationWithAnalyzers =
            compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }


    [Fact]
    public async Task Test_AnalyzeAccessedMember()
    {
        const string code = @"
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using Cosmos.API.Attributes;

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

        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("ExternalMethod"));
        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MethodNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
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
                public static void NotImplemented() {}
            }
        }
""";

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MethodNotImplemented.Id && d.GetMessage().Contains("NotImplemented"));
    }

    [Fact]
    public async Task Test_StaticConstructorTooManyParameters()
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
                
                static TestNativeType() => Console.WriteLine("123");
            }

            [Plug("ConsoleApplication1.TestNativeType", IsOptional = false)]
            public static class Test
            {
                public static void CCtor(object aThis, object param) => Console.WriteLine(param);
            }
        }
""";

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.StaticConstructorTooManyParams.Id && d.GetMessage().Contains("CCtor"));
    }

    [Fact]
    public async Task Test_StaticConstructorNotImplemented()
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
                public static void CCtor(object param, object the) => Console.WriteLine(param);
            }
        }
""";

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MethodNotImplemented.Id && d.GetMessage().Contains(".cctor"));
    }
}
