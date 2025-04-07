// This code is licensed under MIT license (see LICENSE for details)

using System.Text;
using XSharp.Base.ControlFlow;
using XSharp.Base.Interfaces;
using XSharp.X86.Interfaces;

namespace XSharp.X86.Steps.Nasm;

public class NasmBase : INasm, IStep
{

    internal NasmBase(LabelObject labelObject)
    {
        _labelObject = labelObject;
    }

    private uint LabelCounter { get; set; }

    private readonly object _lock = new();
    private readonly LabelObject _labelObject;

    public LabelObject GetNext()
    {
        lock (_lock)
        {
            LabelCounter++;
            return LabelObject.Get($"{_labelObject}_{LabelCounter}");
        }
    }

    public List<IStep> Steps { get; } = new();

    /// <summary>
    /// build all the steps
    /// </summary>
    /// <returns></returns>
    public string Build()
    {
        StringBuilder sb = new();
        foreach (IStep step in Steps)
        {
            sb.AppendLine(step.Build());
        }

        return sb.ToString();
    }
}


public static class NasmEx
{
    public static IX86 Nasm(this IX86 nasm, Action<INasm> action)
    {
        var builder = new NasmBase(nasm.GetNext());
        action(builder);
        nasm.Steps.Add(builder);
        return nasm;
    }
}
