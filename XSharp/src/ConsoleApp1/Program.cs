using XSharp.Base.ControlFlow;
using XSharp.X86;
using XSharp.X86.Steps;
using XSharp.X86.Steps.Maths;



var builder = X86.New()
    .Label("__main", out var main)
    .Add(X86.ECX, X86.EAX)
    .Group(builder =>
    {
        for (int i = 0; i < 3; i++)
        {
            builder.Add(X86.ECX, i);
        }
    }).Jump(main);

Console.WriteLine(builder.Build());
