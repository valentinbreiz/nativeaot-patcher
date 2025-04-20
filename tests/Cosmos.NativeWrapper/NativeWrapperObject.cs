namespace Cosmos.NativeWrapper;

public class NativeWrapperObject
{
    public NativeWrapperObject() => Console.WriteLine("Base ctor");

    public void Speak() => Console.WriteLine(InstanceField);

    public int InstanceMethod(int value) => value + 1;

    public string InstanceField = "Hello World";

    public string InstanceProperty { get; set; } = "Goodbye World";

    private string _instanceBackingField = "Backing Field";

    public string InstanceBackingFieldProperty
    {
        get => _instanceBackingField;
        set => _instanceBackingField = value;
    }
}
