using System.ComponentModel;
using Liquip.API.Attributes;
using Liquip.API.Enum;
using XSharp.Build.Tasks;
using XSharp.X86.Interfaces;
using XSharp.X86.Steps.Maths;

namespace XSharp.Tests;

public class EmitTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test()
    {
        var xSharpString = XSharpEmitter.Emit([typeof(EmitTests)], TargetPlatform.x86_64);

        Assert.That(xSharpString, Is.EqualTo(
            "Add:\nadd EAX, 10\nret\nAddX:\nadd EAX, RBX\nret\n"
            )
        );

    }

    [XSharpMethod(Name = "Add", TargetPlatform = TargetPlatform.x86_64)]
    public static void Add(IX86 builder)
    {
        builder.Add(X86.X86.EAX, 10);
    }

    [XSharpMethod(Name = "AddX", TargetPlatform = TargetPlatform.x86_64)]
    public static void AddX(IX86 builder)
    {
        builder.Add(X86.X86.EAX, X86.X86.RBX);
    }

}
