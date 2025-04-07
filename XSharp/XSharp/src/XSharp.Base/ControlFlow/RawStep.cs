using XSharp.Base.Interfaces;

namespace XSharp.Base.ControlFlow;

public class RawStep : IStep
{
    private readonly string _label;

    internal RawStep(string label) => _label = label;

    public string Build() => _label;
}

public static class RawStepEx
{
    public static T Raw<T>(this T xs, string raw)
        where T : IXSharp
    {
        xs.Steps.Add(new RawStep(raw));
        return xs;
    }
}
