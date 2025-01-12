using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Liquip.Patcher.Analzyer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PlugAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "JJK0001";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticMessages.SupportedDiagnostics;

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.acom/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            context.RegisterSyntaxNodeAction(AnalyzePlugAttribute, SyntaxKind.Attribute);

        }


        // Analyze the PlugAttribute
        private void AnalyzePlugAttribute(SyntaxNodeAnalysisContext context)
        {
            var attribute = context.Node as AttributeSyntax;
            if (attribute.Name.ToString() != "PlugAttribute") return;

            // Check if the TargetName is set
            if (!GetAttributeValue<string>(attribute, "TargetName", out var targetName)) return;

            // Get the type from the TargetName
            var statement = targetName.Trim().Split(',');
            var assemblyName = statement.Last();
            var typeName = statement.First();

            // Check if the type exists
            var type = Type.GetType(typeName);
            type ??= Type.GetType(typeName + ", " + assemblyName);

            if (type == null && !GetAttributeValue<bool>(attribute, "IsOptional"))
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticMessages.TypeNotFound, attribute.GetLocation(), typeName));
        }

        private static T GetAttributeValue<T>(AttributeSyntax attribute, string name)
        {
            var argument = attribute.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals?.Name.Identifier.Text == name);
            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is T value)
                return value;
            else if (typeof(T) == typeof(Type) && argument.Expression is TypeOfExpressionSyntax typeOf && typeOf.Type != null)
                return (T)(object)Type.GetType(typeOf.Type.ToString());
            else
                return default;
        }

        private static bool GetAttributeValue<T>(AttributeSyntax attribute, string name, out T value)
        {
            value = GetAttributeValue<T>(attribute, name);
            return value != null;
        }
    }
}
