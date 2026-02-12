using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Core;

/// <summary>
/// Kernel panic handler for fatal errors.
/// </summary>
public static class Panic
{

    public static bool HasHalted = false;

    /// <summary>
    /// Triggers a kernel panic with the specified message and caller information.
    /// Disables interrupts and halts the CPU.
    /// </summary>
    /// <param name="message">The panic message describing the error.</param>
    /// <param name="caller">The caller method name.</param>
    /// <param name="file">The source file path.</param>
    /// <param name="line">The line number.</param>
    public static void Halt(
        string message,
        [System.Runtime.CompilerServices.CallerMemberName] string caller = "",
        [System.Runtime.CompilerServices.CallerFilePath] string file = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        InternalCpu.DisableInterrupts();
        HasHalted = true;
        Serial.WriteString("\n");
        Serial.WriteString("========================================\n");
        Serial.WriteString("KERNEL PANIC\n");
        Serial.WriteString("========================================\n");
        Serial.WriteString(message);
        Serial.WriteString("\n\n");
        Serial.WriteString("Location:\n");
        Serial.WriteString("  Method: ");
        Serial.WriteString(caller);
        Serial.WriteString("\n  File:   ");
        Serial.WriteString(file);
        Serial.WriteString("\n  Line:   ");
        Serial.WriteNumber((uint)line);
        Serial.WriteString("\n");
        Serial.WriteString("========================================\n");
        Serial.WriteString("System halted.\n");

        HaltCpu();
    }

    private static void HaltCpu()
    {
        // Infinite loop with halt to save power
        while (true)
        {
            InternalCpu.Halt();
        }
    }
}
