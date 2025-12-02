using System.Runtime;

namespace Cosmos.Kernel.Core.Runtime;

internal static class Math
{
    [RuntimeExport("ceil")]
    internal static double ceil(double x)
    {
        // If the value is already an integer, return it directly
        if (x == (long)x)
            return x;

        // For positive numbers, truncate and add 1
        if (x > 0)
            return (long)x + 1;

        // For negative numbers, truncation already acts like ceiling
        return (long)x;
    }

    [RuntimeExport("ceilf")]
    internal static float ceilf(float x)
    {
        // If the value is already an integer, return it directly
        if (x == (int)x)
            return x;

        // For positive numbers, truncate and add 1
        if (x > 0)
            return (int)x + 1;

        // For negative numbers, truncation already acts like ceiling
        return (int)x;
    }
}