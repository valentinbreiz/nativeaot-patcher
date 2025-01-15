using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Liquip.API.Attributes;

namespace Liquip.Patcher.Analzyer.TestProject;

    class Program
    {
        static async Task Main(string[] args)
        {
            await TestAnalyzer();
        }

        static async Task TestAnalyzer()
        {
            string code = @"
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Liquip.API.Attributes;

namespace ConsoleApplication1
{
    public static class TestNativeType
    {
       public static int _field;
        [DllImport(""example.dll"")]
        public static extern void ExternalMethod();

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void NativeMethod();
    }

    [Plug(""ConsoleApplication1.TestNativeType"", IsOptional = false)]
    public static class Test
    {
        public static void Method()
        {

        }
    }
}";

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);
            CSharpCompilation compilation = CSharpCompilation.Create("TestCompilation")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(PlugAttribute).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);

            PatcherAnalyzer analyzer = new();
            CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
            ImmutableArray<Diagnostic> diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

            foreach (Diagnostic diagnostic in diagnostics)
            {
                Console.WriteLine($"Diagnostic ID: {diagnostic.Id}");
                Console.WriteLine($"Message: {diagnostic.GetMessage()}");
                Console.WriteLine($"Location: {diagnostic.Location}");
                Console.WriteLine();
            }
        }
    }
    
