using System.Collections.Immutable;
using Liquip.API.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Liquip.Patcher.Analyzer.Tests;

    public class AnalyzerTests
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
            var diagnostics = await GetDiagnosticsAsync(code);
            AssertDiagnostic(diagnostics, DiagnosticMessages.TypeNotFound.Id, "System.NonExistent");
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
            var diagnostics = await GetDiagnosticsAsync(code);
            AssertDiagnostic(diagnostics, DiagnosticMessages.PlugNotStatic.Id, "Test");
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
            var diagnostics = await GetDiagnosticsAsync(code);
            AssertDiagnostic(diagnostics, DiagnosticMessages.MethodNeedsPlug.Id, "ExternalMethod");
            AssertDiagnostic(diagnostics, DiagnosticMessages.MethodNeedsPlug.Id, "NativeMethod");
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
            var diagnostics = await GetDiagnosticsAsync(code);
            AssertDiagnostic(diagnostics, DiagnosticMessages.MethodNeedsPlug.Id, "ExternalMethod");
            AssertDiagnostic(diagnostics, DiagnosticMessages.MethodNeedsPlug.Id, "NativeMethod");
        }

        [Fact]
        public async Task Test_NewDiagnosticScenario()
        {
            // Add a new test case to handle scenarios introduced in the update
            string code = @"
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    [Plug(""System.NonExistent"", IsOptional = true)]
    public static class Test
    {
        public static void SomeMethod() { }
    }
}";
            var diagnostics = await GetDiagnosticsAsync(code);
            AssertDiagnostic(diagnostics, "NewDiagnosticId", "Specific message relevant to new diagnostic");
        }

        private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
        {
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

            var compilation = CSharpCompilation.Create("TestCompilation")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);

            var analyzer = new PatcherAnalyzer();
            var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static void AssertDiagnostic(ImmutableArray<Diagnostic> diagnostics, string diagnosticId, string messageFragment)
        {
            Assert.Contains(diagnostics, d =>
                d.Id == diagnosticId &&
                d.GetMessage().Contains(messageFragment, StringComparison.OrdinalIgnoreCase));
        }
    }

