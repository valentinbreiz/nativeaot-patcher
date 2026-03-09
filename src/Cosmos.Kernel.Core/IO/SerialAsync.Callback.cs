// This code is licensed under MIT license (see LICENSE for details)

using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class SerialAsync
{
    private static void RunAsync(Action work, KernelAsyncCallback<Exception> callback)
    {
        Enqueue(() =>
        {
            try
            {
                work();
                callback(null);
            }
            catch (Exception e)
            {
                callback(e);
            }
        });
    }


    public static void ComWriteAsync(byte value, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.ComWrite(value), callback);

    public static void WriteStringAsync(string str, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteString(str), callback);

    public static void WriteNumberAsync(ulong number, bool hex, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(uint number, bool hex, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(int number, bool hex, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(long number, bool hex, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteHexAsync(ulong number, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteHex(number), callback);

    public static void WriteHexAsync(uint number, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteHex(number), callback);

    public static void WriteHexWithPrefixAsync(ulong number, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), callback);

    public static void WriteHexWithPrefixAsync(uint number, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), callback);

    public static void WriteAsync(object?[] args, KernelAsyncCallback<Exception> callback) =>
        RunAsync(() => Serial.Write(args), callback);

    public static void StartThread()
    {

        if (s_thread is not null)
            return;
        s_thread = new Thread(Process);
        s_thread.Start();

    }

    private static readonly object s_queueLock = new();
    private static Thread? s_thread;
    private static readonly Queue<Action> s_queue = new(200);

    private static void Enqueue(Action callback)
    {
        if (s_thread is null || !CosmosFeatures.InterruptsEnabled)
        {
            callback();
            return;
        }
        lock (s_queueLock)
        {
            s_queue.Enqueue(callback);
        }
    }

    [DoesNotReturn]
    private static void Process()
    {
        while (true)
        {
            Action? item = null;
            lock (s_queueLock)
            {
                if (s_queue.Count > 0)
                    item = s_queue.Dequeue();
            }
            if (item is not null)
            {
                try
                {
                    item();
                }
                catch
                {
                    // Ignore so the worker loop keeps running
                }
            }
        }
    }
}
