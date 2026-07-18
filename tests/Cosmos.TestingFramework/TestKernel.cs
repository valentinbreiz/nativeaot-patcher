using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Cosmos.TestingFramework
{
    internal sealed class TestKernel
    {
        public Type TestClass { get; }
        public Type TestKernelClass { get; }
        public IReadOnlyList<MethodInfo> Methods { get; }

        public TestKernel(Type TestClass, Type TestKernelClass, IEnumerable<MethodInfo> methods)
        {
            this.TestClass = TestClass ?? throw new ArgumentNullException(nameof(TestClass));
            this.TestKernelClass = TestKernelClass;
            Methods = methods?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(methods));
        }

        public static string GetUid(MethodInfo method)
        {
            ArgumentNullException.ThrowIfNull(method);
            return $"{method.DeclaringType!.FullName}.{method.Name}";
        }
    }
}
