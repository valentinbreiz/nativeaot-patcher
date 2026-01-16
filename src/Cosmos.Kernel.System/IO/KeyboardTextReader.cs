using System.Text;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.System.Keyboard;

namespace Cosmos.Kernel.System.IO;

public sealed class KeyboardTextReader : TextReader
{
    public override int Read()
    {
        if (KeyboardManager.TryReadKey(out KeyEvent? result))
        {
            return result.KeyChar;
        }
        else
        {
            return -1;
        }
    }

    public override int Peek()
    {
        return KeyboardManager.Peek().KeyChar;
    }

    public override string? ReadLine()
    {
        var sb = new StringBuilder();

        // Track cursor position within input string
        int cursorPos = 0;

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
}
