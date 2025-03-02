using System.Xml.Linq;

namespace Liquip.Patcher.Analyzer.CodeFixes.Models;

public readonly record struct ProjectInfo(IEnumerable<string> PlugReferences)
{
        public static ProjectInfo From(XDocument csproj) => new(
            PlugReferences: csproj.Descendants("ItemGroup")
            .Elements("PlugsReference")
            .Select(x => x.Attribute("Include")!.Value)
            .Where(x => x != null)
            );

}
