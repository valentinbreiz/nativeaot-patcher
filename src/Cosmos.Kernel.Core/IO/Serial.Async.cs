// This code is licensed under MIT license (see LICENSE for details)

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class Serial
{
    public static void ComWriteAsync(byte value, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                ComWrite(value);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);
        });

    }

    public static void WriteStringAsync(string str, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteString(str);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);
        });

    }

    public static void WriteNumberAsync(ulong number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteNumber(number, hex);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);
        });

    }

    public static void WriteNumberAsync(uint number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteNumber(number, hex);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });
    }

    public static void WriteNumberAsync(int number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteNumber(number, hex);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });

    }

    public static void WriteNumberAsync(long number, bool hex, KernelAsyncCallback<Exception> callback)
    {

        Queue(() =>
        {
            try
            {
                WriteNumber(number, hex);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });

    }

    public static void WriteHexAsync(ulong number, KernelAsyncCallback<Exception> callback)
    {

        Queue(() =>
        {
            try
            {
                WriteHex(number);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });

    }

    public static void WriteHexAsync(uint number, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteHex(number);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });
    }

    public static void WriteHexWithPrefixAsync(ulong number, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteHexWithPrefix(number);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });

    }

    public static void WriteHexWithPrefixAsync(uint number, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteHexWithPrefix(number);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });
    }

    public static void WriteAsync(object?[] args, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                Write(args);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);

        });

    }

    private static bool UseThread = false;
    private static Thread? s_thread = null;

    public static void StartThread()
    {
        s_thread = new Thread(Process);
        s_thread.Start();
        UseThread = true;
    }

    private static ConcurrentQueue<Action> s_queue = new ConcurrentQueue<Action>();

    private static void Queue(Action callback)
    {
        if (s_thread is null)
        {
            callback();
        }
        else
        {
            s_queue.Enqueue(callback);
        }
    }

    [DoesNotReturn]
    private static void Process()
    {
        while (true)
        {
            if (s_queue.Count == 0)
            {
                continue;
            }

            try
            {
                s_queue.TryDequeue(out Action? action);
                action?.Invoke();
            }
            catch
            {
                // ignored
            }
        }
    }
}
