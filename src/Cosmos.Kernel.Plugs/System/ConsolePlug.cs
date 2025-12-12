using System.Text;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Graphics;
#if ARCH_X64
using Cosmos.Kernel.Services.Keyboard;
#endif

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(Console))]
public class ConsolePlug
{
    [PlugMember]
    public static void Write(string value) => KernelConsole.Write(value);

    [PlugMember]
    public static void WriteLine(string value) => KernelConsole.WriteLine(value);

    [PlugMember]
    public static void Write(char value) => KernelConsole.Write(value);

    [PlugMember]
    public static void WriteLine(char value) => KernelConsole.WriteLine(value);

    [PlugMember]
    public static void WriteLine() => KernelConsole.WriteLine();

#if ARCH_X64
    [PlugMember]
    public static bool get_KeyAvailable() => KeyboardManager.KeyAvailable;

    [PlugMember]
    public static ConsoleKeyInfo ReadKey() => ReadKey(false);

    [PlugMember]
    public static ConsoleKeyInfo ReadKey(bool intercept)
    {
        var keyEvent = KeyboardManager.ReadKey();

        if (!intercept && keyEvent.KeyChar != '\0')
        {
            KernelConsole.Write(keyEvent.KeyChar);
        }

        return ToConsoleKeyInfo(keyEvent);
    }

    [PlugMember]
    public static string? ReadLine()
    {
        var sb = new StringBuilder();

        while (true)
        {
            var keyEvent = KeyboardManager.ReadKey();

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                KernelConsole.WriteLine();
                break;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                    // Move cursor back, print space, move cursor back again
                    KernelConsole.Write('\b');
                    KernelConsole.Write(' ');
                    KernelConsole.Write('\b');
                }
            }
            else if (keyEvent.KeyChar != '\0')
            {
                sb.Append(keyEvent.KeyChar);
                KernelConsole.Write(keyEvent.KeyChar);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a KeyEvent to ConsoleKeyInfo.
    /// </summary>
    private static ConsoleKeyInfo ToConsoleKeyInfo(KeyEvent keyEvent)
    {
        bool shift = (keyEvent.Modifiers & ConsoleModifiers.Shift) != 0;
        bool alt = (keyEvent.Modifiers & ConsoleModifiers.Alt) != 0;
        bool control = (keyEvent.Modifiers & ConsoleModifiers.Control) != 0;

        // Map ConsoleKeyEx to ConsoleKey
        ConsoleKey consoleKey = MapToConsoleKey(keyEvent.Key);

        return new ConsoleKeyInfo(keyEvent.KeyChar, consoleKey, shift, alt, control);
    }

    /// <summary>
    /// Maps ConsoleKeyEx to System.ConsoleKey.
    /// </summary>
    private static ConsoleKey MapToConsoleKey(ConsoleKeyEx key)
    {
        return key switch
        {
            ConsoleKeyEx.Backspace => ConsoleKey.Backspace,
            ConsoleKeyEx.Tab => ConsoleKey.Tab,
            ConsoleKeyEx.Enter => ConsoleKey.Enter,
            ConsoleKeyEx.Escape => ConsoleKey.Escape,
            ConsoleKeyEx.Spacebar => ConsoleKey.Spacebar,
            ConsoleKeyEx.Delete => ConsoleKey.Delete,
            ConsoleKeyEx.D0 => ConsoleKey.D0,
            ConsoleKeyEx.D1 => ConsoleKey.D1,
            ConsoleKeyEx.D2 => ConsoleKey.D2,
            ConsoleKeyEx.D3 => ConsoleKey.D3,
            ConsoleKeyEx.D4 => ConsoleKey.D4,
            ConsoleKeyEx.D5 => ConsoleKey.D5,
            ConsoleKeyEx.D6 => ConsoleKey.D6,
            ConsoleKeyEx.D7 => ConsoleKey.D7,
            ConsoleKeyEx.D8 => ConsoleKey.D8,
            ConsoleKeyEx.D9 => ConsoleKey.D9,
            ConsoleKeyEx.A => ConsoleKey.A,
            ConsoleKeyEx.B => ConsoleKey.B,
            ConsoleKeyEx.C => ConsoleKey.C,
            ConsoleKeyEx.D => ConsoleKey.D,
            ConsoleKeyEx.E => ConsoleKey.E,
            ConsoleKeyEx.F => ConsoleKey.F,
            ConsoleKeyEx.G => ConsoleKey.G,
            ConsoleKeyEx.H => ConsoleKey.H,
            ConsoleKeyEx.I => ConsoleKey.I,
            ConsoleKeyEx.J => ConsoleKey.J,
            ConsoleKeyEx.K => ConsoleKey.K,
            ConsoleKeyEx.L => ConsoleKey.L,
            ConsoleKeyEx.M => ConsoleKey.M,
            ConsoleKeyEx.N => ConsoleKey.N,
            ConsoleKeyEx.O => ConsoleKey.O,
            ConsoleKeyEx.P => ConsoleKey.P,
            ConsoleKeyEx.Q => ConsoleKey.Q,
            ConsoleKeyEx.R => ConsoleKey.R,
            ConsoleKeyEx.S => ConsoleKey.S,
            ConsoleKeyEx.T => ConsoleKey.T,
            ConsoleKeyEx.U => ConsoleKey.U,
            ConsoleKeyEx.V => ConsoleKey.V,
            ConsoleKeyEx.W => ConsoleKey.W,
            ConsoleKeyEx.X => ConsoleKey.X,
            ConsoleKeyEx.Y => ConsoleKey.Y,
            ConsoleKeyEx.Z => ConsoleKey.Z,
            ConsoleKeyEx.F1 => ConsoleKey.F1,
            ConsoleKeyEx.F2 => ConsoleKey.F2,
            ConsoleKeyEx.F3 => ConsoleKey.F3,
            ConsoleKeyEx.F4 => ConsoleKey.F4,
            ConsoleKeyEx.F5 => ConsoleKey.F5,
            ConsoleKeyEx.F6 => ConsoleKey.F6,
            ConsoleKeyEx.F7 => ConsoleKey.F7,
            ConsoleKeyEx.F8 => ConsoleKey.F8,
            ConsoleKeyEx.F9 => ConsoleKey.F9,
            ConsoleKeyEx.F10 => ConsoleKey.F10,
            ConsoleKeyEx.F11 => ConsoleKey.F11,
            ConsoleKeyEx.F12 => ConsoleKey.F12,
            ConsoleKeyEx.UpArrow => ConsoleKey.UpArrow,
            ConsoleKeyEx.DownArrow => ConsoleKey.DownArrow,
            ConsoleKeyEx.LeftArrow => ConsoleKey.LeftArrow,
            ConsoleKeyEx.RightArrow => ConsoleKey.RightArrow,
            ConsoleKeyEx.Home => ConsoleKey.Home,
            ConsoleKeyEx.End => ConsoleKey.End,
            ConsoleKeyEx.PageUp => ConsoleKey.PageUp,
            ConsoleKeyEx.PageDown => ConsoleKey.PageDown,
            ConsoleKeyEx.Insert => ConsoleKey.Insert,
            _ => ConsoleKey.NoName
        };
    }
#endif
}
