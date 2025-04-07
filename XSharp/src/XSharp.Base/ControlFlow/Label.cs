namespace XSharp.Base.ControlFlow;

/// <summary>
/// a label
/// </summary>
public class LabelObject
{
    private readonly string _label;

    internal LabelObject(string label) => _label = label;

    /// <summary>
    /// if its scoped to the current method
    /// </summary>
    public bool IsScoped => _label.StartsWith('.');

    /// <summary>
    /// gets a label from a string
    /// </summary>
    /// <param name="label"></param>
    /// <returns></returns>
    public static LabelObject Get(string label) => new(label);

    /// <summary>
    /// make a new label with a random name
    /// </summary>
    /// <returns></returns>
    public static LabelObject New() => new($@"XSharp{Guid.NewGuid()}XSharp");

    public override string ToString() => _label;

    public override int GetHashCode() => _label.GetHashCode();
}
