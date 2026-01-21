using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(Monitor))]
public static class MonitorPlug
{
    [PlugMember]
    public static void Enter(object obj)
    {
        //TODO: Implement Monitor.Enter
    }

    [PlugMember]
    public static void Exit(object obj)
    {
        //TODO: Implement Monitor.Exit
    }
}
