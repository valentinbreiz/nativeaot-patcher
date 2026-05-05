
using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(Environment))]
public static class EnvironmentPlug
{
    [PlugMember]
    public static string? GetEnvironmentVariableCore(string variable)
    {
        return null;
    }
}
