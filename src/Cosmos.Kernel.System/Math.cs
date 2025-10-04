namespace Cosmos.Kernel.System;

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

    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static int Pow(int x, int y)
    {
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), "Exponent must be non-negative.");
        int result = 1;
        for (int i = 0; i < y; i++)
        {
            result *= x;
        }
        return result;
    }

    public static int Sqrt(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Cannot compute square root of a negative number.");
        if (value == 0) return 0;
        int x = value;
        int y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (value / x + x) / 2;
        }
        return x;
    }
}
