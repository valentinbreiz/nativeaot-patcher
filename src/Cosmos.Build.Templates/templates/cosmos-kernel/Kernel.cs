using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;

internal static partial class Program
{
    /// <summary>
    /// Unmanaged entry point called by the bootloader.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    private static void KernelMain() => Main();

    /// <summary>
    /// Kernel main entry point.
    /// </summary>
    private static void Main()
    {
        Serial.WriteString("Hello from KernelName!\n");
        Console.WriteLine("CosmosOS Kernel Started");

        // Your kernel code goes here

        // Halt the system
        while (true) ;
    }
}
