using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Cosmos.TestingFramework
{
    internal sealed class TestKernel
    {
        public Type DeclaringType { get; }
        public IReadOnlyList<MethodInfo> Methods { get; }

        public TestKernel(Type declaringType, IEnumerable<MethodInfo> methods)
        {
            DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
            Methods = methods?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(methods));
        }

        public static string GetUid(MethodInfo method)
        {
            ArgumentNullException.ThrowIfNull(method);
            return $"{method.DeclaringType!.FullName}.{method.Name}";
        }
    }
}
