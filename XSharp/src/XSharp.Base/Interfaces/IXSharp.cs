using XSharp.Base.ControlFlow;

namespace XSharp.Base.Interfaces;

public interface IXSharp: IFluentInterface
{
    public List<IStep> Steps { get; }
    public string Build();
    public LabelObject GetNext();
}



