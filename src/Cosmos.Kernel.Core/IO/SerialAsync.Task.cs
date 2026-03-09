// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

/// <summary>Task-based async overloads; implemented via <see cref="SerialAsync"/> callback API.</summary>
public static partial class SerialAsync
{
    /// <summary>Runs work on the serial queue and returns a <see cref="Task"/> that completes when done (or faults with the exception).</summary>
    private static Task RunAsTask(Action<KernelAsyncCallback<Exception>> invokeWithCallback)
    {
        var tcs = new TaskCompletionSource();
        invokeWithCallback(ex =>
        {
            if (ex is not null)
                tcs.SetException(ex);
            else
                tcs.SetResult();
        });
        return tcs.Task;
    }

    public static Task ComWriteAsync(byte value) =>
        RunAsTask(cb => ComWriteAsync(value, cb));

    public static Task WriteStringAsync(string str) =>
        RunAsTask(cb => WriteStringAsync(str, cb));

    public static Task WriteNumberAsync(ulong number, bool hex = false) =>
        RunAsTask(cb => WriteNumberAsync(number, hex, cb));

    public static Task WriteNumberAsync(uint number, bool hex = false) =>
        RunAsTask(cb => WriteNumberAsync(number, hex, cb));

    public static Task WriteNumberAsync(int number, bool hex = false) =>
        RunAsTask(cb => WriteNumberAsync(number, hex, cb));

    public static Task WriteNumberAsync(long number, bool hex = false) =>
        RunAsTask(cb => WriteNumberAsync(number, hex, cb));

    public static Task WriteHexAsync(ulong number) =>
        RunAsTask(cb => WriteHexAsync(number, cb));

    public static Task WriteHexAsync(uint number) =>
        RunAsTask(cb => WriteHexAsync(number, cb));

    public static Task WriteHexWithPrefixAsync(ulong number) =>
        RunAsTask(cb => WriteHexWithPrefixAsync(number, cb));

    public static Task WriteHexWithPrefixAsync(uint number) =>
        RunAsTask(cb => WriteHexWithPrefixAsync(number, cb));

    public static Task WriteAsync(params object?[] args) =>
        RunAsTask(cb => WriteAsync(args, cb));
}
