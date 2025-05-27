using System;

namespace Cosmos.Kernel.System.IO
{
    public static class Serial
    {
        private static readonly ushort COM1 = 0x3F8;

        private static void WaitForTransmitBufferEmpty()
        {
            while ((Native.IO.Read8((ushort)(COM1 + 5)) & 0x20) == 0) ;
        }

        public static void ComWrite(byte value)
        {
            // Wait for the transmit buffer to be empty
            WaitForTransmitBufferEmpty();
            // Write the byte to the COM port
            Native.IO.Write8(COM1, value);
        }

        public static void ComInit()
        {
            Native.IO.Write8((ushort)(COM1 + 1), 0x00);
            Native.IO.Write8((ushort)(COM1 + 3), 0x80);
            Native.IO.Write8(COM1, 0x01);
            Native.IO.Write8((ushort)(COM1 + 1), 0x00);
            Native.IO.Write8((ushort)(COM1 + 3), 0x03);
            Native.IO.Write8((ushort)(COM1 + 2), 0xC7);
        }

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
}
