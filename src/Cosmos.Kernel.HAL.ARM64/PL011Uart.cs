using System.Runtime.InteropServices;

namespace Cosmos.Kernel.HAL.ARM64;

/// <summary>
/// PL011 UART driver for ARM64 (used in QEMU virt machine and many ARM boards)
/// </summary>
public static partial class PL011Uart
{
    // QEMU virt machine PL011 UART base address
    private const ulong UART0_BASE = 0x09000000;

    // PL011 Register offsets
    private const ulong UARTDR = 0x00;     // Data Register
    private const ulong UARTFR = 0x18;     // Flag Register
    private const ulong UARTIBRD = 0x24;   // Integer Baud Rate Divisor
    private const ulong UARTFBRD = 0x28;   // Fractional Baud Rate Divisor
    private const ulong UARTLCR_H = 0x2C;  // Line Control Register
    private const ulong UARTCR = 0x30;     // Control Register
    private const ulong UARTIMSC = 0x38;   // Interrupt Mask Set/Clear

    // Flag Register bits
    private const byte UART_FR_TXFF = 1 << 5;  // Transmit FIFO full
    private const byte UART_FR_BUSY = 1 << 3;  // UART busy

    [LibraryImport("*", EntryPoint = "_native_arm64_mmio_read_byte")]
    [SuppressGCTransition]
    private static partial byte NativeReadByte(ulong address);

    [LibraryImport("*", EntryPoint = "_native_arm64_mmio_read_dword")]
    [SuppressGCTransition]
    private static partial uint NativeReadDWord(ulong address);

    [LibraryImport("*", EntryPoint = "_native_arm64_mmio_write_byte")]
    [SuppressGCTransition]
    private static partial void NativeWriteByte(ulong address, byte value);

    [LibraryImport("*", EntryPoint = "_native_arm64_mmio_write_dword")]
    [SuppressGCTransition]
    private static partial void NativeWriteDWord(ulong address, uint value);

    /// <summary>
    /// Initialize the PL011 UART
    /// </summary>
    public static void Initialize()
    {
        // Disable UART
        NativeWriteDWord(UART0_BASE + UARTCR, 0);

        // Clear all interrupts
        NativeWriteDWord(UART0_BASE + UARTIMSC, 0);

        // Set baud rate to 115200
        // Assuming UART clock is 24MHz (QEMU default)
        // Baud rate divisor = UART_CLK / (16 * baud_rate)
        // = 24000000 / (16 * 115200) = 13.02
        // Integer part: 13, Fractional part: 0.02 * 64 = 1
        NativeWriteDWord(UART0_BASE + UARTIBRD, 13);
        NativeWriteDWord(UART0_BASE + UARTFBRD, 1);

        // Enable FIFO, 8-bit data, no parity, 1 stop bit
        NativeWriteDWord(UART0_BASE + UARTLCR_H, (1 << 4) | (3 << 5));

        // Enable UART, TX, and RX
        NativeWriteDWord(UART0_BASE + UARTCR, (1 << 0) | (1 << 8) | (1 << 9));
    }

    /// <summary>
    /// Check if transmit FIFO is full
    /// </summary>
    private static bool IsTransmitFull()
    {
        return (NativeReadDWord(UART0_BASE + UARTFR) & UART_FR_TXFF) != 0;
    }

    /// <summary>
    /// Wait for transmit buffer to be ready
    /// </summary>
    private static void WaitForTransmitReady()
    {
        while (IsTransmitFull()) { }
    }

    /// <summary>
    /// Write a single byte to the UART
    /// </summary>
    public static void WriteByte(byte value)
    {
        WaitForTransmitReady();
        NativeWriteByte(UART0_BASE + UARTDR, value);
    }

    /// <summary>
    /// Write a string to the UART
    /// </summary>
    public static unsafe void WriteString(string str)
    {
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                WriteByte((byte)ptr[i]);
            }
        }
    }
}
