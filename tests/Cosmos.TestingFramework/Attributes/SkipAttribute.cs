namespace Cosmos.TestingFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SkipAttribute : Attribute
    {
        public string Reason { get; private set; }

        public SkipAttribute() : this(string.Empty) { }
        public SkipAttribute(string reason)
        {
            Reason = reason;
        }

    }

}
