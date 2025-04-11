
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class NativeMethodAttribute : Attribute
{
    public string SymbolDll { get; set; }

    public string SymbolName { get; set; }

    public NativeMethodAttribute(string symbolName, string symbolDll)
    {
        SymbolDll = symbolDll;
        SymbolName = symbolName;
    }


    public NativeMethodAttribute(string symbolName) : this(symbolName, "*")
    {
    }
}