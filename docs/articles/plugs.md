A **plug** is a mechanism that replaces existing methods, variables, or types with custom implementations to enable platform-specific functionality ([what is a plug?](https://cosmosos.github.io/articles/Kernel/Plugs.html)).

## Plug-related attributes

### `[Plug]`

Marks a class as the replacement for a target type. The attribute accepts the fully qualified name of the type to replace and optional flags to skip missing targets or substitute base implementations. See the definition in [PlugAttribute.cs](../../src/Cosmos.Build.API/Attributes/PlugAttribute.cs) for details.

**Use when:** creating a plug class that substitutes an existing type.

**Pitfalls:** mismatched `TargetName` can break builds.

### `[PlugMember]`

Indicates that a member should replace a member on the target type. The attribute can be applied to fields, properties, or methods. Its implementation is available in [PlugMemberAttribute.cs](../../src/Cosmos.Build.API/Attributes/PlugMemberAttribute.cs).

**Use when:** you need fine‑grained control over which members of the target type are replaced.

**Pitfalls:** ensure signatures match the target member; otherwise the patcher cannot wire them up correctly.

### `[Expose]`

Adds new private members to the target type so that plugs can use them. The attribute is useful for introducing fields or methods that are not part of the original type.

**Use when:** a plug requires additional private storage or logic.

**Pitfalls:** exposing members that conflict with existing names can cause unpredictable behavior.

### `[FieldAccess]`

Allows a plug method to access private fields of the target type by mapping a parameter to a specific field name. The patcher rewrites the method's IL to reference the requested field (see `ReplaceFieldAccess` in [PlugPatcher.cs](../../src/Cosmos.Patcher/PlugPatcher.cs)).

**Use when:** accessing or modifying an object field inside a plugged method.

**Pitfalls:** incorrect field names or mismatched parameter types result in runtime failures when the patcher attempts to rewrite field accesses.

## Cosmos gen3 plug template

_Feel free to propose changes_

```csharp
using System.IO;

namespace Cosmos.Plugs;

[Plug(typeof(System.IO.FileStream))]
public class FileStream
{
    /* Plug static fields from System.IO.FileStream */
    [PlugMember]
    public static ulong StaticField
    {
         get; set;
    }

    /* Plug instance fields from System.IO.FileStream */
    [PlugMember]
    public static ulong classInstanceField
    {
         get; set;
    }

    /* Add private static fields to plugged class */
    [Expose]
    private static ulong _privateStaticField;

    /* Add private fields to plugged class */
    [Expose]
    private static ulong _privateInstanceField;

    /* Add private static methods to plugged class */
    [Expose]
    private static void PrivateStaticMethod()
    {
    }

    /* Add private objects methods to plugged class */
    [Expose]
    private static void PrivateInstanceMethod(FileStream aThis)
    {
        aThis.WriteByte('a');
    }

    /* Plug non static method with aThis to access fields */
    [PlugMember]
    public static long Seek(FileStream aThis, long offset, SeekOrigin origin)
    {
        PrivateMethod();
        _privateInstanceField = 0;
        aThis.WriteByte('a');
        classStaticField = 0;
        aThis.classInstanceField = 0; //or classInstanceField = 0;
        // ...
    }

    /* Plug static method */
    [PlugMember]
    public static long StaticMethod()
    {

    }

    /* Access private fields from plugged class */
    [PlugMember]
    public static long AccessFieldsMethod(FileStream aThis, [FieldAccess(Name = "InstanceField2")] ulong InstanceField2)
    {
        InstanceField2 = 0;
    }

    /* Assembly plug for System.IO.FileStream.WriteByte */
    [PlugMember, RuntimeImport("asm_writebyte")]
    public static void WriteByte(FileStream aThis, byte b);

}
```
