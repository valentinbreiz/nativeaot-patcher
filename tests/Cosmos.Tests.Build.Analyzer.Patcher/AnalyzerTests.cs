using System.Collections.Immutable;
using Cosmos.Build.Analyzer.Patcher;
using Cosmos.Build.API.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Cosmos.Tests.Build.Analyzer.Patcher;

public class AnalyzerTests
{
    private static readonly MetadataReference s_corlibReference =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private static readonly MetadataReference s_plugAttributeReference =
        MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location);

    private static readonly MetadataReference s_platformSpecificAttributeReference =
        MetadataReference.CreateFromFile(typeof(PlatformSpecificAttribute).Assembly.Location);


    [Fact]
    public async Task Test_TypeNotFoundDiagnostic()
    {
        const string code = """
                            using System;
                            using Cosmos.Build.API.Attributes;

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
    public async Task Test_AnalyzeAccessedMember()
    {
        const string code = """

                                    using System.Runtime.CompilerServices;
                                    using System.Runtime.InteropServices;
                                    using Cosmos.Build.API.Attributes;
                                    using Cosmos.Build.API.Enum;

                                   
                                    namespace ConsoleApplication1
                                    {

                                        public static class TestNativeType
                                        {
                                            [DllImport("example.dll")]
                                            public static extern void ExternalMethod();

                                            [MethodImpl(MethodImplOptions.InternalCall)]
                                            public static extern void NativeMethod();

                                            [PlatformSpecific(PlatformArchitecture.ARM64)]
                                            public static void Arm64OnlyMethod() { }
                                        }

                                        public class Test
                                        {
                                            public void TestMethod()
                                            {
                                                TestNativeType.ExternalMethod();
                                                TestNativeType.NativeMethod();
                                                TestNativeType.Arm64OnlyMethod();
                                            }
                                        }
                                    }
                            """;

        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);

        Console.WriteLine("Diagnostics:");
        foreach (var diag in diagnostics)
        {
            Console.WriteLine(diag.ToString());
        }

        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MemberNeedsPlug.Id && d.GetMessage().Contains("ExternalMethod"));
        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MemberNeedsPlug.Id && d.GetMessage().Contains("NativeMethod"));
        Assert.Contains(diagnostics,
            d => d.Id == DiagnosticMessages.MemberCanNotBeUsed.Id && d.GetMessage().Contains("Arm64OnlyMethod"));
    }

    [Fact]
    public async Task Test_MethodNotImplemented()
    {
        const string code = """
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using Cosmos.Build.API.Attributes;

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
        using Cosmos.Build.API.Attributes;

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
        using Cosmos.Build.API.Attributes;

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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

        CSharpCompilation compilation = CSharpCompilation.Create("TestCompilation")
            .AddReferences(s_corlibReference, s_plugAttributeReference, s_platformSpecificAttributeReference)
            .AddSyntaxTrees(syntaxTree);

        PatcherAnalyzer analyzer = new();
        CompilationWithAnalyzers compilationWithAnalyzers =
            compilation.WithAnalyzers([analyzer], new AnalyzerOptions([], new AnalyzerTestConfigOptionsProvider(new AnalyzerTestConfigOptions(("build_property.CosmosArch", "X64")))));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }


}
