namespace Cosmos.Kernel.System
{
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
}
