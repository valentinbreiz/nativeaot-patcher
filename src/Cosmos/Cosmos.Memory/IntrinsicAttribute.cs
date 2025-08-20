// This code is licensed under MIT license (see LICENSE for details)

namespace System.Runtime.CompilerServices;

// This attribute is only for use in a Class Library
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Interface, Inherited = false)]
internal sealed class IntrinsicAttribute : Attribute
{
}
