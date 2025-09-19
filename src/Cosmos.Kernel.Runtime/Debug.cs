

using System.Diagnostics;

namespace Cosmos.Kernel.Runtime;

/// <summary>
/// Little Serial Implementation, this doesn't have ComInit as it is defined elsewhere and called on '__Initialize_Kernel'
/// </summary>
internal static class Debug
{
    private static readonly ushort COM1 = 0x3F8;

    //[Conditional("DEBUG")]
    private static void WaitForTransmitBufferEmpty()
    {
        while ((Native.IO.Read8((ushort)(COM1 + 5)) & 0x20) == 0) ;
    }

    //[Conditional("DEBUG")]
    public static void ComWrite(byte value)
    {
        // Wait for the transmit buffer to be empty
        WaitForTransmitBufferEmpty();
        // Write the byte to the COM port
        Native.IO.Write8(COM1, value);
    }

    //[Conditional("DEBUG")]
    public static unsafe void WriteString(string str)
    {
        fixed (char* ptr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                ComWrite((byte)ptr[i]);
            }
        }
    }
}
