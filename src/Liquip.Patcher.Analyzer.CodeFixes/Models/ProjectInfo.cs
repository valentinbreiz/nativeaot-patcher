using System.Xml.Linq;

namespace Liquip.Patcher.Analyzer.CodeFixes.Models;

public readonly record struct ProjectInfo(IEnumerable<string> PlugReferences)
{
    public readonly IEnumerable<string> PlugReferences = PlugReferences;

    public static ProjectInfo From(XDocument csproj) => new(
        csproj.Descendants("ItemGroup")
            .Elements("PlugsReference")
            .Select(x => x.Attribute("Include")!.Value)
            .Where(x => x != null)
    );
}
