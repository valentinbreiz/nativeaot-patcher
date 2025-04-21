A **plug** is a mechanism that replaces existing methods, variables, or types with custom implementations to enable platform-specific functionality ([what is a plug?](https://cosmosos.github.io/articles/Kernel/Plugs.html)).

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
