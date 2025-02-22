using XSharp.Base.Interfaces;

namespace XSharp.Base.ControlFlow;

public static class GroupStepEx
{
    public static T Group<T>(this T builder, Action<T> action)
        where T : IXSharp
    {
        action(builder);
        return builder;
    }
}
