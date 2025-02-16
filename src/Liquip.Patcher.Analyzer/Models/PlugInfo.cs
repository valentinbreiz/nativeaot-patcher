using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Liquip.Patcher.Analyzer.Models;

public record PlugInfo(bool NeedsValidation, bool IsExternal, ClassDeclarationSyntax Plug);
