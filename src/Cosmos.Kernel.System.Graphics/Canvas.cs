namespace Cosmos.Kernel.System.Graphics
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
