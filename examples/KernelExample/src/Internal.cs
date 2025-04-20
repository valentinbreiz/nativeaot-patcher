using System.Runtime;
using System.Runtime.CompilerServices;

namespace EarlyBird.Internal
{

    public static class Native
    {
        public static class IO
        {
            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_write_byte")]
            public static extern void Write8(ushort Port, byte Value);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_write_word")]
            public static extern void Write16(ushort Port, ushort Value);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_write_dword")]
            public static extern void Write32(ushort Port, uint Value);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_read_byte")]
            public static extern byte Read8(ushort Port);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_read_word")]
            public static extern ushort Read16(ushort Port);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "_native_io_read_dword")]
            public static extern uint Read32(ushort Port);
        }
    }

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
