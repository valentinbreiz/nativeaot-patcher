using System;
using System.Runtime.InteropServices;

namespace Cosmos.Kernel.System.Conversion
{
    public static unsafe class Base64
    {
        public static byte* Decode(char* Encoded, uint Length)
        {
            // Determine padding based on '=' characters
            int Padding = 0;
            if (Encoded[Length - 1] == '=')
            {
                Padding++;
                if (Encoded[Length - 2] == '=')
                {
                    Padding++;
                }
            }

            // Calculate the length of the decoded data
            int DecodedLength = (int)((Length * 3) / 4 - Padding);
            byte* Decoded = (byte*)MemoryOp.Alloc((uint)DecodedLength);

            // Base64 decoding table for A-Z, a-z, 0-9, +, /
            byte* Base64Table = (byte*)MemoryOp.Alloc(128);
            for (int i = 0; i < 26; i++) Base64Table['A' + i] = (byte)(i); // A-Z -> 0-25
            for (int i = 0; i < 26; i++) Base64Table['a' + i] = (byte)(26 + i); // a-z -> 26-51
            for (int i = 0; i < 10; i++) Base64Table['0' + i] = (byte)(52 + i); // 0-9 -> 52-61
            Base64Table['+'] = 62; // '+' -> 62
            Base64Table['/'] = 63; // '/' -> 63

            int DecodedIndex = 0;
            for (int i = 0; i < Length; i += 4)
            {
                byte b0 = Base64Table[Encoded[i]];
                byte b1 = Base64Table[Encoded[i + 1]];
                byte b2 = Base64Table[Encoded[i + 2]];
                byte b3 = Base64Table[Encoded[i + 3]];

                Decoded[DecodedIndex++] = (byte)((b0 << 2) | (b1 >> 4)); // First byte
                if (i + 2 < Length - Padding) // Make sure we're not out of bounds
                {
                    Decoded[DecodedIndex++] = (byte)((b1 << 4) | (b2 >> 2)); // Second byte
                }
                if (i + 3 < Length - Padding) // Make sure we're not out of bounds
                {
                    Decoded[DecodedIndex++] = (byte)((b2 << 6) | b3); // Third byte
                }
            }

            return Decoded;
        }
    }
}
