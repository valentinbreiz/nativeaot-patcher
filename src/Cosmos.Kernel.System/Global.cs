using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Graphics;

namespace Cosmos.Kernel.System;

/// <summary>
/// Global system state and initialization for Cosmos.
/// </summary>
public static class Global
{
    /// <summary>
    /// The registered kernel instance that will be started.
    /// </summary>
    private static Kernel? _kernel;

    /// <summary>
    /// Gets or sets the current kernel instance.
    /// </summary>
    public static Kernel? CurrentKernel
    {
        get => _kernel;
        set => _kernel = value;
    }

    /// <summary>
    /// Registers a kernel instance to be started by the boot infrastructure.
    /// Called automatically by the generated entry point.
    /// </summary>
    /// <param name="kernel">The kernel instance to register.</param>
    public static void RegisterKernel(Kernel kernel)
    {
        Serial.WriteString("[Global] Registering kernel\n");
        _kernel = kernel;
    }

    /// <summary>
    /// Initializes the system. Called by the Kernel base class during OnBoot().
    /// Override OnBoot() in your kernel to customize initialization.
    /// </summary>
    public static void Init()
    {
        Serial.WriteString("[Global] Init() called\n");

        // Initialize graphics console (framebuffer + font)
        Serial.WriteString("[Global] Initializing KernelConsole...\n");
        if (KernelConsole.Initialize())
        {
            Serial.WriteString("[Global] KernelConsole initialized: ");
            Serial.WriteNumber((ulong)KernelConsole.Cols);
            Serial.WriteString("x");
            Serial.WriteNumber((ulong)KernelConsole.Rows);
            Serial.WriteString(" chars\n");
        }
        else
        {
            Serial.WriteString("[Global] WARNING: KernelConsole initialization failed!\n");
        }
    }

    /// <summary>
    /// Starts the registered kernel. Called by the CosmosEntryPoint.
    /// </summary>
    public static void StartKernel()
    {
        Serial.WriteString("[Global] StartKernel called\n");

        if (_kernel == null)
        {
            Serial.WriteString("[Global] ERROR: No kernel registered!\n");
            Serial.WriteString("[Global] Check CosmosKernelClass property in your .csproj\n");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: No kernel registered!");
            Console.WriteLine("Set <CosmosKernelClass> in your .csproj to your kernel's full type name.");
            Console.ResetColor();

            // Halt
            while (true) { }
            return;
        }

        Serial.WriteString("[Global] Starting kernel...\n");
        _kernel.Start();

        // If kernel.Start() returns, halt the system
        Serial.WriteString("[Global] Kernel.Start() returned, halting...\n");
        while (true) { }
    }
}
