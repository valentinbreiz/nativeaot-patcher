using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.Internal.StackTraceMetadata;

[Plug("Internal.StackTraceMetadata.StackTraceMetadata")]
public static class StackTraceMetadataPlug
{
    [PlugMember]
    public static bool StackTraceHiddenMetadataPresent()
    {
        // Return false - we don't use StackTraceHiddenAttribute metadata in Cosmos
        return false;
    }
}
