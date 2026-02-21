using Cosmos.Kernel;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Cpu;
using Cosmos.Kernel.HAL.Pci;

namespace Internal.Runtime.CompilerHelpers
{
    /// <summary>
    /// This class is responsible for initializing the library and its dependencies. It is called by the runtime before any managed code is executed.
    /// </summary>
    public class LibraryInitializer
    {
        /// <summary>
        /// Initialize HAL, interrupts, PCI, and platform-specific hardware. This method is called by the runtime before any managed code is executed.
        /// </summary>
        public static void InitializeLibrary()
        {
            // Get the platform initializer (registered by HAL.X64 or HAL.ARM64 module initializer)
            var initializer = PlatformHAL.Initializer;
            if (initializer == null)
            {
                Serial.WriteString("[KERNEL] ERROR: No platform initializer registered!\n");
                Serial.WriteString("[KERNEL] Make sure Cosmos.Kernel.HAL.X64 or HAL.ARM64 is referenced.\n");
                while (true) { }
            }

            // Display architecture
            Serial.WriteString("[KERNEL]   - Architecture: ");
            Serial.WriteString(initializer.PlatformName);
            Serial.WriteString("\n");

            // Initialize platform-specific HAL
            Serial.WriteString("[KERNEL]   - Initializing HAL...\n");
            PlatformHAL.Initialize(initializer);

            // Initialize interrupts
            Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");
            InterruptManager.Initialize(initializer.CreateInterruptController());

            // Initialize PCI
            Serial.WriteString("[KERNEL]   - Initializing PCI...\n");
            PciManager.Setup();

            // Initialize platform-specific hardware (ACPI, APIC, GIC, timers, etc.)
            Serial.WriteString("[KERNEL]   - Initializing platform hardware...\n");
            initializer.InitializeHardware();
        }
    }
}
