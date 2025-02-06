
using System.Text;
using XSharp.Base.Interfaces;

namespace XSharp.Base;

public class XSharp : IXSharp
{

    public List<IStep> Steps { get; } = new List<IStep>();

    /// <summary>
    /// build all the steps
    /// </summary>
    /// <returns></returns>
    public string Build()
    {
        var sb = new StringBuilder();
        foreach (var step in Steps)
        {
            sb.AppendLine(step.Build());
        }

        return sb.ToString();

    }

}
