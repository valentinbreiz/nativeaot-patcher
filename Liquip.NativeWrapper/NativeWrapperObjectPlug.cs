using Liquip.API.Attributes;

namespace Liquip.NativeWrapper;

[Plug(typeof(NativeWrapperObject))]
public class NativeWrapperObjectPlug
{
    public static void Ctor(object aThis) => Console.WriteLine("Plugged ctor");

    public static void Speak(object aThis) => Console.WriteLine("bz bz plugged hello");

    public static int InstanceMethod(object aThis, int value) => value * 2;
}
