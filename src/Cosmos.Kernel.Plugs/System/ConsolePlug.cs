using System.Text;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.System.Keyboard;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(Console))]
public class ConsolePlug
{
    // Track the start position for current input line (for proper backspace/delete handling)
    private static int _inputStartX;
    private static int _inputStartY;

    private static void ThrowIfKeyboardDisabled()
    {
        if (!CosmosFeatures.KeyboardEnabled)
            throw new InvalidOperationException("Console input requires keyboard support. Set CosmosEnableKeyboard=true in your csproj to enable it.");
    }

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

    [PlugMember]
    public static void Clear() => KernelConsole.Clear();

    [PlugMember]
    public static ConsoleColor get_ForegroundColor()
    {
        // Return white as default - we don't track the reverse mapping
        return ConsoleColor.White;
    }

    [PlugMember]
    public static void set_ForegroundColor(ConsoleColor value)
    {
        KernelConsole.SetForegroundColor(value);
    }

    [PlugMember]
    public static ConsoleColor get_BackgroundColor()
    {
        // Return black as default - we don't track the reverse mapping
        return ConsoleColor.Black;
    }

    [PlugMember]
    public static void set_BackgroundColor(ConsoleColor value)
    {
        KernelConsole.SetBackgroundColor(value);
    }

    [PlugMember]
    public static void ResetColor()
    {
        KernelConsole.ResetColors();
    }

    [PlugMember]
    public static int get_CursorLeft()
    {
        return KernelConsole.CursorX;
    }

    [PlugMember]
    public static void set_CursorLeft(int value)
    {
        KernelConsole.CursorX = value;
    }

    [PlugMember]
    public static int get_CursorTop()
    {
        return KernelConsole.CursorY;
    }

    [PlugMember]
    public static void set_CursorTop(int value)
    {
        KernelConsole.CursorY = value;
    }

    [PlugMember]
    public static void SetCursorPosition(int left, int top)
    {
        KernelConsole.SetCursorPosition(left, top);
    }

    [PlugMember]
    public static bool get_CursorVisible()
    {
        return KernelConsole.CursorVisible;
    }

    [PlugMember]
    public static void set_CursorVisible(bool value)
    {
        KernelConsole.CursorVisible = value;
    }

    [PlugMember]
    public static int get_WindowWidth()
    {
        return KernelConsole.Cols;
    }

    [PlugMember]
    public static int get_WindowHeight()
    {
        return KernelConsole.Rows;
    }

    [PlugMember]
    public static int get_BufferWidth()
    {
        return KernelConsole.Cols;
    }

    [PlugMember]
    public static int get_BufferHeight()
    {
        return KernelConsole.Rows;
    }

    [PlugMember]
    public static bool get_KeyAvailable()
    {
        ThrowIfKeyboardDisabled();
        return KeyboardManager.KeyAvailable;
    }

    [PlugMember]
    public static ConsoleKeyInfo ReadKey() => ReadKey(false);

    [PlugMember]
    public static ConsoleKeyInfo ReadKey(bool intercept)
    {
        ThrowIfKeyboardDisabled();

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
        ThrowIfKeyboardDisabled();

        var sb = new StringBuilder();

        // Track cursor position within input string
        int cursorPos = 0;

        // Save the starting position for this input
        _inputStartX = KernelConsole.CursorX;
        _inputStartY = KernelConsole.CursorY;

        while (true)
        {
            var keyEvent = KeyboardManager.ReadKey();

            switch (keyEvent.Key)
            {
                case ConsoleKeyEx.Enter:
                    KernelConsole.WriteLine();
                    return sb.ToString();

                case ConsoleKeyEx.Backspace:
                    if (cursorPos > 0)
                    {
                        // Remove character from string at cursor position
                        sb.Remove(cursorPos - 1, 1);
                        cursorPos--;

                        // Move cursor back
                        KernelConsole.MoveCursorLeft();

                        // If we're not at the end, shift remaining chars left
                        if (cursorPos < sb.Length)
                        {
                            // Save current position
                            int savedX = KernelConsole.CursorX;
                            int savedY = KernelConsole.CursorY;

                            // Redraw remaining characters
                            for (int i = cursorPos; i < sb.Length; i++)
                            {
                                KernelConsole.Write(sb[i]);
                            }
                            // Clear the last position (now empty)
                            KernelConsole.Write(' ');

                            // Restore cursor position
                            KernelConsole.SetCursorPosition(savedX, savedY);
                        }
                        else
                        {
                            // Simple case: at end of string
                            KernelConsole.Write(' ');
                            KernelConsole.MoveCursorLeft();
                        }
                    }
                    break;

                case ConsoleKeyEx.Delete:
                    if (cursorPos < sb.Length)
                    {
                        // Remove character at cursor position
                        sb.Remove(cursorPos, 1);

                        // Save current position
                        int savedX = KernelConsole.CursorX;
                        int savedY = KernelConsole.CursorY;

                        // Redraw remaining characters
                        for (int i = cursorPos; i < sb.Length; i++)
                        {
                            KernelConsole.Write(sb[i]);
                        }
                        // Clear the last position (now empty)
                        KernelConsole.Write(' ');

                        // Restore cursor position
                        KernelConsole.SetCursorPosition(savedX, savedY);
                    }
                    break;

                case ConsoleKeyEx.LeftArrow:
                    if (cursorPos > 0)
                    {
                        cursorPos--;
                        KernelConsole.MoveCursorLeft();
                    }
                    break;

                case ConsoleKeyEx.RightArrow:
                    if (cursorPos < sb.Length)
                    {
                        cursorPos++;
                        KernelConsole.MoveCursorRight();
                    }
                    break;

                case ConsoleKeyEx.Home:
                    // Move cursor to start of input
                    while (cursorPos > 0)
                    {
                        cursorPos--;
                        KernelConsole.MoveCursorLeft();
                    }
                    break;

                case ConsoleKeyEx.End:
                    // Move cursor to end of input
                    while (cursorPos < sb.Length)
                    {
                        cursorPos++;
                        KernelConsole.MoveCursorRight();
                    }
                    break;

                default:
                    if (keyEvent.KeyChar != '\0')
                    {
                        if (cursorPos < sb.Length)
                        {
                            // Insert character in the middle
                            sb.Insert(cursorPos, keyEvent.KeyChar);
                            cursorPos++;

                            // Save current position after typing the new char
                            int afterTyping = KernelConsole.CursorX + 1;
                            int savedY = KernelConsole.CursorY;

                            // Redraw from current position
                            for (int i = cursorPos - 1; i < sb.Length; i++)
                            {
                                KernelConsole.Write(sb[i]);
                            }

                            // Move cursor to correct position
                            KernelConsole.SetCursorPosition(afterTyping, savedY);
                        }
                        else
                        {
                            // Append character at end
                            sb.Append(keyEvent.KeyChar);
                            cursorPos++;
                            KernelConsole.Write(keyEvent.KeyChar);
                        }
                    }
                    break;
            }
        }
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
}
