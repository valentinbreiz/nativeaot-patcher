namespace System.Runtime.CompilerServices;

// Internal so it never becomes ambiguous with the identical per-arch HAL
// definitions in assemblies that reference this one; ILC matches the
// attribute by name, not by assembly.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
internal class EagerStaticClassConstructionAttribute : Attribute
{
}
