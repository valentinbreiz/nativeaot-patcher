﻿using Cosmos.API.Attributes;

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

    [PlugMember] public string InstanceField = "Plugged Hello World";

    [PlugMember] public string InstanceProperty { get; set; } = "Plugged Goodbye World";

    private string _instanceBackingField = "Plugged Backing Field";

    [PlugMember]
    public string InstanceBackingFieldProperty
    {
        get => _instanceBackingField;
        set => _instanceBackingField = value;
    }
}
