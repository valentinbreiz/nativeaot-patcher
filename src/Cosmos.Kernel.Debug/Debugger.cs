using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Cosmos.Kernel.Debug;

public static class Debugger
{

    /// <summary>
    /// Triggers a Bochs breakpoint.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoBochsBreak([CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends the pointer of the given object to any connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void SendPtr(object obj, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends the pointer of the given object to any connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void SendPtr(IntPtr ptr, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 32-bit unsigned integer to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(uint number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 32-bit signed integer to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(int number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 64-bit unsigned integer to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(ulong number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 64-bit signed integer to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(long number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 32-bit floating-point number to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(float number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a 64-bit floating-point number to connected debugging hosts.
    /// </summary>
    [Conditional("COSMOSDEBUG")]
    public static void DoSendNumber(double number, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!) { }

    /// <summary>
    /// Sends a kernel panic error code to connected debugging hosts.
    /// </summary>
    [DoesNotReturn]
    [Conditional("COSMOSDEBUG")]
    public static void SendKernelPanic(uint id, [CallerMemberName] string callingMethod = null!, [CallerLineNumber] int callingLineNumber = 0, [CallerFilePath] string callingFilePath = null!)
    {
        while (true)
        {
            break; // remove this later
        }
        // ReSharper disable once FunctionNeverReturns
    }

}
