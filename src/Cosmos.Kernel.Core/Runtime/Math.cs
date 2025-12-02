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

        // Handle special floating-point values
        if (double.IsNaN(x)) return double.NaN;
        if (double.IsPositiveInfinity(x)) return double.PositiveInfinity;
        if (double.IsNegativeInfinity(x)) return double.NegativeInfinity;

        // For positive numbers, truncate and add 1
        if (x > 0)
            return (long)x + 1;

        // For negative numbers, truncation already acts like ceiling
        return (long)x;
    }

    [RuntimeExport("ceilf")]
    internal static float ceilf(float x)
    {
        return (float)ceil(x);
    }
}
