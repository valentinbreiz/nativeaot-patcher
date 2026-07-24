using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;

namespace Cosmos.Kernel.Core.Memory.VAS;

/// <summary>
/// Handles CPU page faults / data aborts. For the PoC, unhandled user-space
/// faults terminate the owning process; kernel faults remain fatal.
/// </summary>
public static class PageFaultHandler
{
    /// <summary>
    /// x64 canonical higher-half start (lower half is user space for this PoC).
    /// </summary>
    private const ulong X64UserSpaceLimit = 0x0000800000000000UL;

    /// <summary>
    /// Handles a page fault. Returns true if the fault was handled (e.g. process killed).
    /// Returns false if the caller should panic.
    /// </summary>
    public static bool Handle(PageFaultInfo info)
    {
        AddressSpace? currentSpace = SchedulerManager.GetCpuState(SchedulerManager.GetCurrentCpuId()).CurrentThread?.AddressSpace;

        // Kernel fault: no process owns the faulting context.
        if (currentSpace == null || currentSpace == AddressSpace.KernelSpace)
        {
            return false;
        }

        // Only handle faults in the lower half as user faults for this PoC.
        if (!IsUserAddress(info.FaultAddress))
        {
            return false;
        }

        Serial.WriteString("[PageFault] User fault at 0x");
        Serial.WriteHex(info.FaultAddress);
        Serial.WriteString(" in process address space; terminating process\n");

        ProcessManager.TerminateByAddressSpace(currentSpace, -1);

        // The scheduler will pick another thread on the next tick.
        return true;
    }

    /// <summary>
    /// Determines whether the given virtual address is in the user-space range.
    /// For the PoC this is the x64 lower canonical half.
    /// </summary>
    private static bool IsUserAddress(ulong address)
    {
        return address < X64UserSpaceLimit;
    }
}
