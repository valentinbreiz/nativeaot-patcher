using Cosmos.Kernel;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.Logging;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.Core.Scheduler.Stride;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.HAL.Pci;

namespace Internal.Runtime.CompilerHelpers
{
    /// <summary>
    /// This class is responsible for initializing the library and its dependencies. It is called by the runtime before any managed code is executed.
    /// </summary>
    [Logger(Category = "KERNEL")]
    public partial class LibraryInitializer
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
                Log.Error("No platform initializer registered!");
                Log.Error("Make sure Cosmos.Kernel.HAL.X64 or HAL.ARM64 is referenced.");
                while (true) { }
            }

            Log.Info("  - Architecture: " + initializer.PlatformName);

            Log.Info("  - Initializing HAL...");
            PlatformHAL.Initialize(initializer);

            // Initialize interrupts (skipped if CosmosEnableInterrupts=false)
            if (InterruptManager.IsEnabled)
            {
                Log.Info("  - Initializing interrupts...");
                InterruptManager.Initialize(initializer.CreateInterruptController());

                if (CosmosFeatures.PCIEnabled)
                {
                    // Initialize PCI (requires interrupts for MSI/MSI-X)
                    Log.Info("  - Initializing PCI...");
                    ulong ecamBase = AcpiMcfg.GetEcamBase();
                    initializer.PreparePciMapping(ecamBase);
                    PciDevice.SetEcamBase(ecamBase);
                    PciManager.Setup();
                }

                // Initialize platform-specific hardware (ACPI, APIC, GIC, timers, etc.)
                Log.Info("  - Initializing platform hardware...");
                initializer.InitializeHardware();
            }
        }
    }
}
