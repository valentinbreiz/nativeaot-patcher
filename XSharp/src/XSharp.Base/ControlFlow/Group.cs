using System.Text;
using XSharp.Base.Interfaces;

namespace XSharp.Base.ControlFlow;

public class GroupBase: IXSharp, IStep
{
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

    public List<IStep> Steps { get; }
}
