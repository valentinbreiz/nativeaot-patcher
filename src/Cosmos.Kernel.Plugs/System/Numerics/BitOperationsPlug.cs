using Cosmos.Build.API.Attributes;

namespace Cosmos.Kernel.Plugs.System.Numerics;

/// <summary>
/// Plug for System.Numerics.BitOperations to fix ARM64 NativeAOT codegen bug.
/// The ARM64 intrinsic path uses ArmBase.LeadingZeroCount which causes hangs.
/// This provides simple software fallback implementations.
/// </summary>
[Plug("System.Numerics.BitOperations")]
public static class BitOperationsPlug
{
    /// <summary>
    /// Returns the integer (floor) log of the specified value, base 2.
    /// Software fallback to avoid ARM64 ArmBase.LeadingZeroCount bug.
    /// </summary>
    [PlugMember(nameof(Log2))]
    public static int Log2(uint value)
    {
        // Log2(0) is undefined, but we return 0 to match CoreLib behavior
        if (value == 0)
            return 0;

        // Simple software implementation: count bits until we reach the highest set bit
        int result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }
        return result;
    }

    /// <summary>
    /// Returns the integer (floor) log of the specified value, base 2.
    /// Software fallback to avoid ARM64 ArmBase.LeadingZeroCount bug.
    /// </summary>
    [PlugMember(nameof(Log2))]
    public static int Log2(ulong value)
    {
        // Log2(0) is undefined, but we return 0 to match CoreLib behavior
        if (value == 0)
            return 0;

        // Simple software implementation: count bits until we reach the highest set bit
        int result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }
        return result;
    }
}
