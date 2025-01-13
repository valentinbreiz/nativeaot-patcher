using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Liquip.Patcher.Analzyer;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using Liquip.API.Attributes;

namespace AnalyzerTest;

public class AnalyzerTests
{
    [Fact]
    public async Task Test_Diagnostics()
    {
        // Load the code you want to analyze
        string code = @"
using System;
using Liquip.API.Attributes;

namespace ConsoleApplication1;


    [Plug(""System.NonExistent"", IsOptional = false)]
    public  class Test
    {
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod()
        {
            Console.WriteLine(""This method has the Native flag."");
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void InternalCallMethod();
    }";

        // Get the diagnostics
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(code);
        Assert.Collection(diagnostics,
        diagnostic =>
        {
            Assert.Equal(DiagnosticMessages.TypeNotFound.Id, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("The specified type 'System.NonExistent' could not be located", diagnostic.GetMessage());
        },
        diagnostic =>
        {
            Assert.Equal(DiagnosticMessages.ClassNotStatic.Id, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("The plug impl class 'Test' should be static", diagnostic.GetMessage());
        },
            diagnostic =>
            {
                Assert.Equal(DiagnosticMessages.MethodNeedsPlug.Id, diagnostic.Id);
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Equal("The method 'ExternalMethod' requires a plug", diagnostic.GetMessage());
            },
            diagnostic =>
            {
                Assert.Equal(DiagnosticMessages.MethodNeedsPlug.Id, diagnostic.Id);
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Equal("The method 'NativeMethod' requires a plug", diagnostic.GetMessage());
            },
            diagnostic =>
            {
                Assert.Equal(DiagnosticMessages.MethodNeedsPlug.Id, diagnostic.Id);
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Equal("The method 'InternalCallMethod' requires a plug", diagnostic.GetMessage());
            });
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
