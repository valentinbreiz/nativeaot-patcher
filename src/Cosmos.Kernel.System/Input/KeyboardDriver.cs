using System.Runtime.InteropServices;
using Cosmos.Kernel.Runtime;
using Cosmos.Kernel.System.Interrupts;

namespace Cosmos.Kernel.System.Input;

public static unsafe class KeyboardDriver
{
    private const ushort DataPort = 0x60;

    private static readonly char[] ScancodeMap = new char[128]
    {
        '\0','\x1B','1','2','3','4','5','6','7','8','9','0','-','=', '\b','\t',
        'q','w','e','r','t','y','u','i','o','p','[',']','\n','\0','a','s','d','f','g','h','j','k','l',';','\'','`',
        '\0','\\','z','x','c','v','b','n','m',',','.','/','\0','*','\0',' ','\0'
    };

    [UnmanagedCallersOnly]
    public static void KeyboardIsr()
    {
        byte sc = Native.IO.Read8(DataPort);
        char c = '\0';
        if (sc < ScancodeMap.Length)
        {
            c = ScancodeMap[sc];
        }
        if (c != '\0')
        {
            KernelKeyboard.AddChar(c);
        }
        InterruptManager.SendEoi(1);
    }
}
