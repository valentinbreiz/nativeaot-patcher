using System;

namespace Cosmos.Kernel.System.Input;

public static class KernelKeyboard
{
    private const int BufferSize = 128;
    private static readonly char[] _buffer = new char[BufferSize];
    private static int _head;
    private static int _tail;

    public static void AddChar(char c)
    {
        int next = (_tail + 1) & (BufferSize - 1);
        if (next == _head)
        {
            // Buffer full, drop character
            return;
        }
        _buffer[_tail] = c;
        _tail = next;
    }

    public static bool TryReadChar(out char c)
    {
        if (_head == _tail)
        {
            c = default;
            return false;
        }
        c = _buffer[_head];
        _head = (_head + 1) & (BufferSize - 1);
        return true;
    }

    public static char ReadChar()
    {
        while (!TryReadChar(out var c))
        {
            // Busy wait until character available
        }
        return c;
    }
}
