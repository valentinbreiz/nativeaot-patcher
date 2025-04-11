
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PlugMethodAttribute : Attribute
{
    public Type TargetClass { get; set; }
    public string TargetMethodName { get; set; }


    public PlugMethodAttribute(Type targetClass, string targetMethodName)
    {
        TargetClass = targetClass;
        TargetMethodName = targetMethodName;
    }

    public PlugMethodAttribute(Type targetClass) : this(targetClass, string.Empty)
    {
    }
    public PlugMethodAttribute(string targetMethodName) : this(null, targetMethodName)
    {
    }
    public PlugMethodAttribute() : this(null, string.Empty)
    {
    }
}