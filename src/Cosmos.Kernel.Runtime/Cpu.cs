using System.Runtime;

namespace Cosmos.Kernel.Runtime;

public static class Cpu
{
    /// <summary>
    /// replace this with some thing better
    /// </summary>
    private static long TickCount64 = 0;

    [RuntimeExport("RhpGetTickCount64")]
    public static unsafe long RhpGetTickCount64()
    {
        return TickCount64++;
    }

}
