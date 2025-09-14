using System.Xml.Linq;

namespace Cosmos.Build.Analyzer.Patcher.CodeFixes.Models;

public readonly record struct ProjectInfo(IEnumerable<string> PlugReferences)
{
        public static ProjectInfo From(XDocument csproj) => new(
           csproj.Descendants("ItemGroup")
                    .Where(x => x.Name == "PlugReference")
                    .Select(x => x.Attribute("Include")!.Value)
            );

}
