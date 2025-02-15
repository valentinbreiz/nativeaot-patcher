using XSharp.Base.ControlFlow;
using XSharp.X86;
using XSharp.X86.Steps;

namespace XSharp.Tests;

public class LogicTests
{

    [Test]
    public void Test()
    {
        var xSharpBuilder = X86.X86.New();

        var breakLabel = LabelObject.Get("test__BreakLabel");

        xSharpBuilder
            .Group(i =>
            {
                i.If(JumpCondition.Equal, _true =>
                {
                    _true.Raw("; this is true");
                }, _false =>
                {
                    _false.Raw("; this is false");
                });

                i.If(JumpCondition.Carry, _true =>
                {
                    _true.Raw("; this is true");
                }, _false =>
                {
                    _false.Raw("; this is false");
                    _false.Jump(breakLabel);
                });

            })
            .Label(breakLabel)
            .Return();

        var xSharpString = xSharpBuilder.Build();

        Assert.That(xSharpString, Is.EqualTo(
                "Add:\nadd EAX, 10\nret\nAddX:\nadd EAX, RBX\nret\n"
            )
        );

    }

}
