using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Liquip.API.Attributes;

namespace ConsoleApplication1;

class TestType
{
    [DllImport("user32.dll")]
    public static extern void ExternalMethod();

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void NativeMethod();

    public static extern void InternalCallMethod();
}

[Plug(typeof(TestType), IsOptional = false)]
public class Test
{

}

class Program
{
    static void Main()
    {
        Console.WriteLine(nameof(TestType));
    }
}