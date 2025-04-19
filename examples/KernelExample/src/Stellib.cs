using EarlyBird.PSF;

namespace EarlyBird
{
    public class Color
    {
        public static uint Red = 0xFF0000;
        public static uint Green = 0x00FF00;
        public static uint Blue = 0x0000FF;
        public static uint White = 0xFFFFFF;
        public static uint Black = 0x000000;
        public static uint Yellow = 0xFFFF00;
        public static uint Cyan = 0x00FFFF;
        public static uint Magenta = 0xFF00FF;
        public static uint Orange = 0xFFA500;
        public static uint Purple = 0x800080;
        public static uint Pink = 0xFFC0CB;
        public static uint Brown = 0xA52A2A;
        public static uint Gray = 0x808080;
        public static uint LightGray = 0xD3D3D3;
        public static uint DarkGray = 0xA9A9A9;
        public static uint LightRed = 0xFF7F7F;
        public static uint LightGreen = 0x7FFF7F;
        public static uint LightBlue = 0x7F7FFF;
        public static uint LightYellow = 0xFFFF7F;
        public static uint LightCyan = 0x7FFFFF;
        public static uint LightMagenta = 0xFF7FFF;
        public static uint LightOrange = 0xFFBF00;
        public static uint LightPurple = 0xBF00FF;
        public static uint LightPink = 0xFFB6C1;
        public static uint LightBrown = 0xD2B48C;
        public static uint Transparent = 0x00000000; // Transparent color
    }

    public static class Math
    {
        public static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }

        public static int Min(int a, int b)
        {
            return a < b ? a : b;
        }

        public static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
    }
    public static unsafe class MemoryOp
    {

        private static ulong HeapBase;
        private static ulong HeapEnd;
        private static ulong FreeListHead;

        public static void InitializeHeap(ulong heapBase, ulong heapSize)
        {
            HeapBase = heapBase;
            HeapEnd = heapBase + heapSize;
            FreeListHead = heapBase;

            // Initialize the free list with a single large block
            *(ulong*)FreeListHead = heapSize; // Block size
            *((ulong*)FreeListHead + 1) = 0; // Next block pointer
        }

        public static void* Alloc(uint size)
        {
            size = (uint)((size + 7) & ~7); // Align size to 8 bytes
            ulong prev = 0;
            ulong current = FreeListHead;

            while (current != 0)
            {
                ulong blockSize = *(ulong*)current;
                ulong next = *((ulong*)current + 1);

                if (blockSize >= size + 16) // Enough space for allocation and metadata
                {
                    ulong remaining = blockSize - size - 16;
                    if (remaining >= 16) // Split block
                    {
                        *(ulong*)(current + 16 + size) = remaining;
                        *((ulong*)(current + 16 + size) + 1) = next;
                        *((ulong*)current + 1) = current + 16 + size;
                    }
                    else // Use entire block
                    {
                        size = (uint)(blockSize) - 16;
                        *((ulong*)current + 1) = next;
                    }

                    if (prev == 0)
                    {
                        FreeListHead = *((ulong*)current + 1);
                    }
                    else
                    {
                        *((ulong*)prev + 1) = *((ulong*)current + 1);
                    }

                    *(ulong*)current = size; // Store allocated size
                    return (void*)(current + 16);
                }

                prev = current;
                current = next;
            }

            return null; // Out of memory
        }

        public static void Free(void* ptr)
        {
            ulong block = (ulong)ptr - 16;
            ulong blockSize = *(ulong*)block;

            *(ulong*)block = blockSize + 16;
            *((ulong*)block + 1) = FreeListHead;
            FreeListHead = block;
        }

        public static void MemSet(byte* dest, byte value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i] = value;
            }
        }
        public static void MemSet(uint* dest, uint value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i] = value;
            }
        }

        public static void MemCopy(uint* dest, uint* src, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i] = src[i];
            }
        }

        public static bool MemCmp(uint* dest, uint* src, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (dest[i] != src[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static void MemMove(uint* dest, uint* src, int count)
        {
            if (dest < src)
            {
                for (int i = 0; i < count; i++)
                {
                    dest[i] = src[i];
                }
            }
            else
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    dest[i] = src[i];
                }
            }
        }
    }

    public static class Graphics
    {
        public unsafe class Canvas
        {
            public static uint* Address;
            public static uint Width;
            public static uint Height;
            public static uint Pitch;

            public static void DrawPixel(uint color, int x, int y)
            {
                if (x >= 0 && x < Width && y >= 0 && y < Height)
                {
                    Address[y * (int)(Pitch / 4) + x] = color;
                }
            }

            public static void DrawLine(uint color, int x1, int y1, int x2, int y2)
            {
                int dx = x2 - x1;
                int dy = y2 - y1;
                int absDx = Math.Abs(dx);
                int absDy = Math.Abs(dy);
                int sx = (dx > 0) ? 1 : -1;
                int sy = (dy > 0) ? 1 : -1;
                int err = absDx - absDy;

                while (true)
                {
                    DrawPixel(color, x1, y1);
                    if (x1 == x2 && y1 == y2) break;
                    int err2 = err * 2;
                    if (err2 > -absDy)
                    {
                        err -= absDy;
                        x1 += sx;
                    }
                    if (err2 < absDx)
                    {
                        err += absDx;
                        y1 += sy;
                    }
                }
            }

            public static void DrawRectangle(uint color, int x, int y, int width, int height)
            {
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        DrawPixel(color, x + i, y + j);
                    }
                }
            }

            public static void DrawCircle(uint color, int x, int y, int radius)
            {
                for (int i = -radius; i <= radius; i++)
                {
                    for (int j = -radius; j <= radius; j++)
                    {
                        if (i * i + j * j <= radius * radius)
                        {
                            int px = x + j;
                            int py = y + i;
                            if (px >= 0 && px < Width && py >= 0 && py < Height)
                            {
                                DrawPixel(color, px, py);
                            }
                        }
                    }
                }
            }

            public static void ClearScreen(uint color)
            {
                MemoryOp.MemSet(Address, color, (int)((Pitch / 4) * Height));
            }

            public static void DrawChar(char c, int x, int y, uint color)
            {
                PCScreenFont.PutChar(c, x, y, color, Color.Transparent);
            }

            public static void DrawString(string text, int x, int y, uint color)
            {
                PCScreenFont.PutString(text, x, y, color, Color.Transparent);
            }
        }
    }
}
