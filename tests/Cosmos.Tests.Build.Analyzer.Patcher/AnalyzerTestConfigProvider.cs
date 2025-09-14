using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

class AnalyzerTestConfigOptionsProvider(
    AnalyzerConfigOptions global,
    ImmutableDictionary<string, AnalyzerConfigOptions>? perTree = null) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _global = global;
    private readonly ImmutableDictionary<string, AnalyzerConfigOptions> _perTree = perTree ?? ImmutableDictionary<string, AnalyzerConfigOptions>.Empty;

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        _perTree.TryGetValue(tree.FilePath, out AnalyzerConfigOptions? opts) ? opts : Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        Empty;

    private static AnalyzerConfigOptions Empty { get; } = new AnalyzerTestConfigOptions();
}

class AnalyzerTestConfigOptions(params (string key, string value)[] pairs) : AnalyzerConfigOptions
{
    private readonly ImmutableDictionary<string, string> _options = pairs.ToImmutableDictionary(p => p.key, p => p.value);

    public override bool TryGetValue(string key, out string value) =>
        _options.TryGetValue(key, out value!);
}