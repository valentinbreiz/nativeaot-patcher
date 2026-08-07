namespace Cosmos.TestingFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class GeneratedTestKernelAttribute(params Type[] testClassTypes) : Attribute
    {
        public Type[] TestClassTypes { get; } = testClassTypes;
    }
}
