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
                "je Auto_1__true\nAuto_1__false:\n; this is false\njum Auto_1__end\nAuto_1__true:\n\n; this is true\nAuto_1__end:\n\njc Auto_2__true\nAuto_2__false:\n; this is false\njum test__BreakLabel\njum Auto_2__end\nAuto_2__true:\n\n; this is true\nAuto_2__end:\n\ntest__BreakLabel:\nret\n"
            )
        );

    }

}