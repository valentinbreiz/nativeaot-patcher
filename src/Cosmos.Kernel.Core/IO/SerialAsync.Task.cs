// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class SerialAsync
{
    private static Task RunAsTask(Action<KernelAsyncCallback> invokeWithCallback)
    {
        var tcs = new TaskCompletionSource();
        invokeWithCallback(ex =>
        {
            if (ex is not null)
            {
                tcs.SetException(ex);
            }
            else
            {
                tcs.SetResult();
            }
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

    public static Task ReadAsync(byte[] buffer) =>
        RunAsTask(cb => ReadAsync(buffer, cb));

    public static Task<string> ReadAsync()
    {
        var tcs = new TaskCompletionSource<string>();
        ReadAsync((ex, s) =>
        {
            if (ex is not null)
            {
                tcs.SetException(ex);
            }
            else
            {
                tcs.SetResult(s);
            }
        });
        return tcs.Task;
    }
}
