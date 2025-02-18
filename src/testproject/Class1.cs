using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Liquip.API.Attributes;

namespace testproject;



public class Class1
{
    [DllImport("d.dll")]
    public static extern void testproject();

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void InternalBufferOverflowException();


}

[Plug(typeof(string))]
public static class StringImpl
{
    public static void Ctor(string aThis, string t, string tg)
    {

    }

}





