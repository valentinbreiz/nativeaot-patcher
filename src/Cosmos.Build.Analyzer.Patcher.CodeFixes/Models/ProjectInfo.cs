using System.Xml.Linq;

namespace Cosmos.Patcher.Analyzer.CodeFixes.Models;

public readonly record struct ProjectInfo(IEnumerable<string> PlugReferences)
{
    public static ProjectInfo From(XDocument csproj) => new(
        PlugReferences: csproj.Descendants("ItemGroup")
                .Where(x => x.Name == "PlugReference")
                .Select(x => x.Attribute("Include")!.Value)
        );

}
