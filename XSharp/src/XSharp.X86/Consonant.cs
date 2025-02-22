using XSharp.X86.Interfaces;

namespace XSharp.X86;

public class Constant : IConstant
{
    private string _value;

    private Constant(string value) => _value = value;

    public string Emit() => _value;


    public static Constant From(byte v) => new(Convert.ToHexString([v]));
    public static Constant From(short v) => new(v.ToString());
    public static Constant From(int v) => new(v.ToString());
    public static Constant From(long v) => new(v.ToString());
    public static Constant From(ushort v) => new(v.ToString());
    public static Constant From(uint v) => new(v.ToString());
    public static Constant From(ulong v) => new(v.ToString());
}
