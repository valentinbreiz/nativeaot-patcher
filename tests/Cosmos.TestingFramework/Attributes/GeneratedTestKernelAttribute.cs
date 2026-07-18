namespace Cosmos.TestingFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class GeneratedTestKernelAttribute(Type testClassType) : Attribute
    {
        public Type TestClassType { get; } = testClassType;
    }
}
