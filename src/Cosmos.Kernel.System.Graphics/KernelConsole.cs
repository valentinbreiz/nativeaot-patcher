using System.Runtime.CompilerServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.IO;

namespace Cosmos.Kernel.System.Graphics;

public static class KernelConsole
{
    private static int _cursorX;
    private static int _cursorY;
    private static int CharWidth => PCScreenFont.CharWidth;
    private static int CharHeight => PCScreenFont.CharHeight;
    private const int LineSpacing = 0;
    private static bool _isInitialized = false;

    /// <summary>
    /// Gets whether graphics console is available and initialized.
    /// </summary>
    public static unsafe bool IsAvailable => _isInitialized && Canvas.Address != null;

    /// <summary>
    /// Gets whether the graphics console has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes the graphics framebuffer and canvas.
    /// Safe to call multiple times - will only initialize once.
    /// Should be called early in kernel initialization, after memory is available.
    /// </summary>
    /// <returns>True if graphics were initialized successfully, false if no framebuffer available.</returns>
    public static unsafe bool Initialize()
    {
        // Already initialized - idempotent
        if (_isInitialized)
            return Canvas.Address != null;

        _isInitialized = true;

        // Initialize framebuffer if available from bootloader
        if (Limine.Framebuffer.Response != null && Limine.Framebuffer.Response->FramebufferCount > 0)
        {
            LimineFramebuffer* fb = Limine.Framebuffer.Response->Framebuffers[0];
            Canvas.Address = (uint*)fb->Address;
            Canvas.Width = (uint)fb->Width;
            Canvas.Height = (uint)fb->Height;
            Canvas.Pitch = (uint)fb->Pitch;
            Canvas.ClearScreen(Color.Black);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Module initializer to ensure graphics console is initialized at module load time.
    /// </summary>
    [ModuleInitializer]
    internal static void Init()
    {
        // Ensure initialized at module load time
        Initialize();
    }

    public static void Write(string text)
    {
        if (!IsAvailable)
            return;

        foreach (char c in text)
        {
            Write(c);
        }
    }

    public static void Write(char c)
    {
        if (!IsAvailable)
            return;

        if (c == '\n')
        {
            NewLine();
            return;
        }

        Canvas.DrawChar(c, _cursorX, _cursorY, Color.White);
        _cursorX += CharWidth;
        if (_cursorX + CharWidth > Canvas.Width)
        {
            NewLine();
        }
    }

    public static void WriteLine(string text)
    {
        Write(text);
        NewLine();
    }

    public static void WriteLine(char c)
    {
        Write(c);
        NewLine();
    }

    public static void WriteLine()
    {
        NewLine();
    }

    private static void NewLine()
    {
        _cursorX = 0;
        _cursorY += CharHeight + LineSpacing;
        if (_cursorY + CharHeight > Canvas.Height)
        {
            Scroll();
        }
    }

    private static unsafe void Scroll()
    {
        int lineHeight = CharHeight + LineSpacing;
        int lineSize = lineHeight * (int)Canvas.Pitch;
        int screenSize = (int)(Canvas.Pitch * Canvas.Height);
        MemoryOp.MemMove((byte*)Canvas.Address, (byte*)Canvas.Address + lineSize, screenSize - lineSize);
        MemoryOp.MemSet((uint*)((byte*)Canvas.Address + screenSize - lineSize), Color.Black, (int)((Canvas.Pitch / 4) * lineHeight));
        _cursorY = (int)Canvas.Height - CharHeight - LineSpacing;
    }
}
