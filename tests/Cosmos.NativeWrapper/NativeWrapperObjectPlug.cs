using Cosmos.API.Attributes;

namespace Cosmos.NativeWrapper;

[Plug(typeof(NativeWrapperObject))]
public class NativeWrapperObjectPlug
{
    [PlugMethod]
    public static void Ctor(object aThis) => Console.WriteLine("Plugged ctor");

    [PlugMethod]
    public static void Speak(object aThis) => Console.WriteLine("bz bz plugged hello");

    [PlugMethod]
    public static int InstanceMethod(object aThis, int value) => value * 2;

    [NativeMethod("_native_")]
    public static extern void NativeMethod();
}
