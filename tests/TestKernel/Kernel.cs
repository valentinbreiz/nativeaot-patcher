using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Sys = Cosmos.Kernel.System;

namespace TestKernel
{
    /// <summary>
    /// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
    /// </summary>
    public class Kernel : Sys.Kernel
    {
        protected override void BeforeRun()
        {
            Serial.WriteString("Hello Cosmos!\n");
            Console.WriteLine("Cosmos booted successfully!");
            Console.WriteLine("Type a command to get it executed.");
        }

        protected override void Run()
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            switch (input.ToLower())
            {
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  help     - Show this help message");
                    Console.WriteLine("  clear    - Clear the screen");
                    Console.WriteLine("  halt     - Halt the system");
                    break;

                case "clear":
                    Console.Clear();
                    break;

                case "halt":
                    Console.WriteLine("Halting system...");
                    Stop();
                    break;

                default:
                    Console.WriteLine($"\"{input}\" is not a command");
                    break;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = nameof(RhGetThreadEntryPointAddress))]
        public static void RhGetThreadEntryPointAddress()
        {

        }
        [UnmanagedCallersOnly(EntryPoint = nameof(RhGetDefaultStackSize))]
        public static void RhGetDefaultStackSize()
        {

        }

        [UnmanagedCallersOnly(EntryPoint = nameof(RhDebugBreak))]
        public static void RhDebugBreak()
        {

        }
    }
}
