using Cosmos.Build.API.Attributes;
using SysThread = System.Threading.Thread;

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(SysThread))]
public static class ThreadPlug
{
    // Thread.CreateThread is deliberately NOT plugged: the upstream body reads
    // the constructor's maxStackSize from the private StartHelper (unreachable
    // from a plug — UnsafeAccessor rejects byref returns of inaccessible field
    // types) and hands the resolved size to SystemNative_CreateThread, exported
    // by Core's libSystemNative. RhGetDefaultStackSize /
    // RhGetThreadEntryPointAddress in Cosmos.Kernel.Core.Runtime.Thread
    // complete its dependencies.

    // TODO: Implement RhYield
    [PlugMember]
    public static bool Yield() => true;
}
