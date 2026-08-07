using System.Diagnostics.CodeAnalysis;
using System.Text;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

public static partial class SerialAsync
{
    private static void RunAsync(Action work, KernelAsyncCallback callback)
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


    public static void ComWriteAsync(byte value, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.ComWrite(value), callback);

    public static void WriteStringAsync(string str, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteString(str), callback);

    public static void WriteNumberAsync(ulong number, bool hex, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(uint number, bool hex, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(int number, bool hex, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteNumberAsync(long number, bool hex, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteNumber(number, hex), callback);

    public static void WriteHexAsync(ulong number, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteHex(number), callback);

    public static void WriteHexAsync(uint number, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteHex(number), callback);

    public static void WriteHexWithPrefixAsync(ulong number, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), callback);

    public static void WriteHexWithPrefixAsync(uint number, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.WriteHexWithPrefix(number), callback);

    public static void WriteAsync(object?[] args, KernelAsyncCallback callback) =>
        RunAsync(() => Serial.Write(args), callback);

    public static void ReadAsync(byte[] buffer, KernelAsyncCallback callback)
    {
        int index = 0;
        ReadHandler handler = null!;
        handler = b =>
        {
            try
            {
                buffer[index++] = b;
                if (index == buffer.Length)
                {
                    ReadEvent -= handler;
                    callback(null);
                }
            }
            catch (Exception e)
            {
                ReadEvent -= handler;
                callback(e);
            }
        };

        ReadEvent += handler;
    }

    public static void ReadAsync(KernelAsyncCallback<string> callback)
    {
        int index = 0;

        byte[] buffer = new byte[256];
        ReadHandler handler = null!;
        handler = b =>
        {
            try
            {
                buffer[index++] = b;

                if (b == '\n' || b == '\r')
                {
                    ReadEvent -= handler;
                    callback(null, Encoding.UTF8.GetString(buffer, 0, index));
                }

                if (index == buffer.Length - 1)
                {
                    byte[] t = new byte[buffer.Length + 256];
                    Array.Copy(buffer, 0, t, 0, t.Length);
                    buffer = t;
                }
            }
            catch (Exception e)
            {
                ReadEvent -= handler;
                callback(e, string.Empty);
            }
        };

        ReadEvent += handler;
    }

    public static void StartThread()
    {
        if (!CosmosFeatures.UARTEnabled)
        {
            return;
        }
        if (s_sendThread is null)
        {
            s_sendThread = new Thread(SendProcess);
            s_sendThread.Start();
        }

        if (s_receiveThread is null)
        {
            s_receiveThread = new Thread(ReceiveProcess);
            s_receiveThread.Start();
        }
    }

    // Declare the delegate (if using non-generic pattern).
    public delegate void ReadHandler(byte value);

    // Declare the event.
    public static event ReadHandler ReadEvent;

    private static readonly Lock s_queueLock = new();
    private static Thread? s_sendThread;
    private static Thread? s_receiveThread;
    private static readonly Queue<Action> s_queue = new(200);

    private static void Enqueue(Action callback)
    {
        if (!CosmosFeatures.UARTEnabled)
        {
            return;
        }

        if (s_sendThread is null || !CosmosFeatures.InterruptsEnabled)
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
    private static void ReceiveProcess()
    {
        while (true)
        {
#if ARCH_ARM64
            while ((Native.MMIO.Read8(Serial.PL011_BASE + Serial.PL011_FR) & Serial.FR_RXFF) == 0)
            {
                ReadEvent.Invoke(Native.MMIO.Read8(Serial.PL011_BASE + Serial.PL011_DR));
            }
#else
            if ((Native.IO.Read8(Serial.COM1_BASE + Serial.REG_LSR) & Serial.LSR_RX_EMPTY) == 0)
            {
                ReadEvent.Invoke(Native.IO.Read8(Serial.COM1_BASE));
            }
#endif
        }
    }

    [DoesNotReturn]
    private static void SendProcess()
    {
        while (true)
        {
            Action? item = null;
            lock (s_queueLock)
            {
                if (s_queue.Count > 0)
                {
                    item = s_queue.Dequeue();
                }
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
