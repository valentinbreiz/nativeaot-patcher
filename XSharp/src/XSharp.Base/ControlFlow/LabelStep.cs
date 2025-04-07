using XSharp.Base.Interfaces;

namespace XSharp.Base.ControlFlow;

public class LabelStep : IStep
{
    private readonly LabelObject _label;

    internal LabelStep(LabelObject label) => _label = label;

    public string Build() => _label + ":";
}

public static class LabelStepEx
{
    public static T Label<T>(this T xs, LabelObject label)
        where T : IXSharp
    {
        xs.Steps.Add(new LabelStep(label));
        return xs;
    }

    public static T Label<T>(this T xs, string name, out LabelObject label)
        where T : IXSharp
    {
        label = LabelObject.Get(name);
        return xs.Label(label);
    }

    public static T Label<T>(this T xs, out LabelObject label)
        where T : IXSharp
    {
        label = LabelObject.New();
        return xs.Label(label);
    }
}
