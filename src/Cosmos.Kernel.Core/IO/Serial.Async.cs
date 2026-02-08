using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.Async;

namespace Cosmos.Kernel.Core.IO;

/// <summary>
/// Multi-architecture serial port driver.
/// - x86-64: 16550 UART via port I/O (COM1 at 0x3F8)
/// - ARM64: PL011 UART via MMIO (QEMU virt at 0x09000000)
/// </summary>
public static partial class Serial
{

    /// <summary>
    /// Write a single byte to the serial port.
    /// Waits for transmit buffer to be ready before writing.
    /// </summary>
    public static void ComWriteAsync(byte value, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                RealComWrite(value);
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);
        });

    }

    public static unsafe void WriteStringAsync(string str, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                fixed (char* ptr = str)
                {
                    for (int i = 0; i < str.Length; i++)
                    {
                        RealComWrite((byte)ptr[i]);
                    }
                }
            }
            catch (Exception e)
            {
                callback(e);
                return;
            }

            callback(null);
        });

    }

    public static unsafe void WriteNumberAsync(ulong number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                if (number == 0)
                {
                    ComWrite((byte)'0');
                    return;
                }

                const int maxDigits = 20; // Enough for 64-bit numbers
                byte* buffer = stackalloc byte[maxDigits];
                int index = 0;
                ulong baseValue = hex ? 16u : 10u;

                while (number > 0)
                {
                    ulong digit = number % baseValue;
                    if (hex && digit >= 10)
                    {
                        buffer[index] = (byte)('A' + (digit - 10));
                    }
                    else
                    {
                        buffer[index] = (byte)('0' + digit);
                    }
                    number /= baseValue;
                    index++;
                }

                // Write digits in reverse order
                for (int i = index - 1; i >= 0; i--)
                {
                    RealComWrite(buffer[i]);
                }
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
        WriteNumberAsync((ulong)number, hex, callback);
    }

    public static void WriteNumberAsync(int number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                if (number < 0)
                {
                    RealComWrite((byte)'-');
                    WriteNumberAsync((ulong)(-number), hex, callback);
                }
                else
                {
                    WriteNumberAsync((ulong)number, hex, callback);
                }
            }
            catch (Exception e)
            {
                callback(e);
            }

        });

    }

    public static void WriteNumberAsync(long number, bool hex, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                if (number < 0)
                {
                    RealComWrite((byte)'-');
                    WriteNumberAsync((ulong)(-number), hex, callback);
                }
                else
                {
                    WriteNumberAsync((ulong)number, hex, callback);
                }
            }
            catch (Exception e)
            {
                callback(e);
            }

        });
    }

    public static void WriteHexAsync(ulong number, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteNumberAsync(number, true, callback);
            }
            catch (Exception e)
            {
                callback(e);
            }

        });

    }

    public static void WriteHexAsync(uint number, KernelAsyncCallback<Exception> callback)
    {
        Queue(() =>
        {
            try
            {
                WriteNumberAsync((ulong)number, true, callback);
            }
            catch (Exception e)
            {
                callback(e);
            }

        });
    }

    public static void WriteHexWithPrefixAsync(ulong number, KernelAsyncCallback<Exception> callback)
    {
        int count = 2;
        KernelAsyncCallback<Exception> cbWrapper = (Exception? e) =>
        {
            count--;
            if (e is not null)
            {
                callback(e);
            }

            if (count <= 0)
            {
                callback(null);
            }
        };

        Queue(() =>
        {
            try
            {
                WriteStringAsync("0x", cbWrapper);
                WriteNumberAsync(number, true, cbWrapper);
            }
            catch (Exception e)
            {
                callback(e);
            }

        });

    }

    public static void WriteHexWithPrefixAsync(uint number, KernelAsyncCallback<Exception> callback)
    {
        int count = 2;
        KernelAsyncCallback<Exception> cbWrapper = (Exception? e) =>
        {
            count--;
            if (e is not null)
            {
                callback(e);
            }

            if (count <= 0)
            {
                callback(null);
            }
        };

        Queue(() =>
        {
            try
            {
                WriteStringAsync("0x", cbWrapper);
                WriteNumberAsync((ulong)number, true, cbWrapper);
            }
            catch (Exception e)
            {
                callback(e);
            }

        });
    }

    public static void WriteAsync(object?[] args, KernelAsyncCallback<Exception> callback)
    {
        int count = args.Length;
        KernelAsyncCallback<Exception> cbWrapper = (e) =>
        {
            Interlocked.Decrement(ref count);
            if (e is not null)
            {
                callback(e);
            }

            if (count <= 0)
            {
                callback(null);
            }
        };

        Queue(() =>
        {
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case null:
                            WriteStringAsync(NULL, cbWrapper);
                            break;
                        case string s:
                            WriteStringAsync(s, cbWrapper);
                            break;
                        case char c:
                            WriteStringAsync(c.ToString(), cbWrapper);
                            break;
                        case short @short:
                            WriteNumberAsync(@short, false, cbWrapper);
                            break;
                        case ushort @ushort:
                            WriteNumberAsync(@ushort, false, cbWrapper);
                            break;
                        case int @int:
                            WriteNumberAsync(@int, false, cbWrapper);
                            break;
                        case uint @uint:
                            WriteNumberAsync(@uint, false, cbWrapper);
                            break;
                        case long @long:
                            WriteNumberAsync(@long, false, cbWrapper);
                            break;
                        case ulong @ulong:
                            WriteNumberAsync(@ulong, false, cbWrapper);
                            break;
                        case bool @bool:
                            WriteStringAsync(@bool ? TRUE : FALSE, cbWrapper);
                            break;
                        case byte @byte:
                            WriteNumberAsync((ulong)@byte, true, cbWrapper);
                            break;
                        case byte[] @byteArray:
                            for (int j = 0; j < @byteArray.Length; j++)
                            {
                                Interlocked.Increment(ref count);
                                WriteNumberAsync((ulong)@byteArray[j], true, cbWrapper);
                            }
                            break;
                        default:
                            WriteStringAsync(args[i].ToString(), cbWrapper);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                callback(e);
            }

        });


    }

}
