using Cosmos.Kernel.System.Graphics;

namespace Cosmos.Kernel.HAL;

public static unsafe class Screen
{
    public static void Init(void* address, uint width, uint height, uint pitch)
    {
        Canvas.Address = (uint*)address;
        Canvas.Width = width;
        Canvas.Height = height;
        Canvas.Pitch = pitch;
        Canvas.ClearScreen(Color.Black);
    }
}
