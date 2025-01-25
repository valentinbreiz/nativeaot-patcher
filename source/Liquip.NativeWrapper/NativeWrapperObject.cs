namespace Liquip.NativeWrapper;

public class NativeWrapperObject
{
    private string _hello = "Hello world!";

    public NativeWrapperObject() => Console.WriteLine("Base ctor");

    public void Speak() => Console.WriteLine(_hello);

    public int InstanceMethod(int value) => value + 1;
}
