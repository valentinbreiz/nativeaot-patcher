using System.Runtime;

namespace Cosmos.Kernel.Runtime;

public class Thread
{
    private static object[][] threadData = new object[][] {};
    [RuntimeExport("RhGetThreadStaticStorage")]
    static ref object[][] RhGetThreadStaticStorage()
    {
        return ref threadData;
    }
}
