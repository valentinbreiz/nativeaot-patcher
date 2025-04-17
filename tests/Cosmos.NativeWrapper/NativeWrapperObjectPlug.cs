using Cosmos.API.Attributes;
using Cosmos.API.Enum;

namespace Cosmos.NativeWrapper;

[Plug(typeof(NativeWrapperObject))]
public class NativeWrapperObjectPlug
{
    [PlugMember]
    public static void Ctor(object aThis) => Console.WriteLine("Plugged ctor");

    [PlugMember]
    public static void Speak(object aThis) => Console.WriteLine("bz bz plugged hello");

    [PlugMember]
    public static int InstanceMethod(object aThis, int value) => value * 2;

    [PlugMember] public string _hello = "Plugged Hello";
}
