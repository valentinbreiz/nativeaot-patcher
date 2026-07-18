using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Extensions;

namespace Cosmos.TestingFramework
{
    public static class TestingPlatformBuilderHook
    {
        public static void AddExtensions(ITestApplicationBuilder builder, string[] _)
        {
            builder.AddCosmosTestFramework();
        }
    }
}
