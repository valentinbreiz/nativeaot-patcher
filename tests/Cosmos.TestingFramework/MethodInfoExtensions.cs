
using System.Reflection;
using Microsoft.Testing.Platform.Extensions.Messages;

namespace Cosmos.TestingFramework
{
    internal static class MethodInfoExtensions
    {
        extension(MethodInfo method)
        {
            public string GetUid() => $"{method.DeclaringType!.FullName}.{method.Name}";
            public TestMethodIdentifierProperty GetTestMethodIdentifierProperty() => new(
                    method.DeclaringType!.Assembly!.FullName!,
                    method.DeclaringType!.Namespace!,
                    method.DeclaringType.Name!,
                    method.Name,
                    method.GetGenericArguments().Length,
                    method.GetParameters().Select(x => x.ParameterType.FullName).ToArray()!,
                    method.ReturnType.FullName!);
        }
    }
}