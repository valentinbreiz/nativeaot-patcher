// This code is licensed under MIT license (see LICENSE for details)

using System.Reflection;
using Liquip.API.Attributes;
using Liquip.API.Enum;
using XSharp.Base.ControlFlow;
using XSharp.X86.Steps;

namespace XSharp.Build.Tasks;

public class XSharpEmitter
{

    public static string Emit(Assembly[] assemblies, TargetPlatform platform)
    {
        return Emit(
            assemblies
                .SelectMany(a => a.GetTypes()).ToArray(),
            platform
            );
    }

    /// <summary>
    /// emit all the X# from the given types
    /// </summary>
    /// <param name="types"></param>
    /// <param name="platform"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static string Emit(Type[] types, TargetPlatform platform)
    {
        if (platform == TargetPlatform.Any) return "";

        var methods = types
            .Where(x => x.IsClass)
            .SelectMany(x => x.GetMethods())
            .Where(t =>
                {
                    if (!t.IsStatic) return false;

                    var attributes = t
                        .GetCustomAttributes<XSharpMethodAttribute>();

                    return attributes.Any(i => i.TargetPlatform == platform);

                }
            )
            .Select(t => (Type: t, Data: t.GetCustomAttributes<XSharpMethodAttribute>().First(i=>i.TargetPlatform == platform)));

        switch (platform)
        {
            case TargetPlatform.x86_64:
                var x86Builder = X86.X86.New();
                foreach (var method in methods)
                {
                    var label = LabelObject.Get(method.Data.Name);
                    x86Builder.Label(label);
                    x86Builder.Group(i =>
                    {
                        var obj = Activator.CreateInstance(method.Type.DeclaringType);
                        method.Type.Invoke(obj, new object?[] { x86Builder });
                        x86Builder.Return(); // just to be safe
                    });
                }
                return x86Builder.Build();
                break;
            case TargetPlatform.Arm:
            default:
                throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
        }
    }

}
