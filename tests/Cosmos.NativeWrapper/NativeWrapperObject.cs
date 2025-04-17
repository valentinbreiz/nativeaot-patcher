using Cosmos.API.Attributes;

namespace Cosmos.NativeWrapper;

public class NativeWrapperObject
{
    public string _hello = "Hello world!";

    public NativeWrapperObject() => Console.WriteLine("Base ctor");

    public void Speak() => Console.WriteLine(_hello);

    public int InstanceMethod(int value) => value + 1;
}
