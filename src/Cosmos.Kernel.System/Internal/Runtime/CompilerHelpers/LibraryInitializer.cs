using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timer;

namespace Internal.Runtime.CompilerHelpers
{
    public class LibraryInitializer
    {
        public static void InitializeLibrary()
        {
            // Initialize heap for memory allocations
            Serial.WriteString("[LibINIT]\n");

            // Initialize Timer Manager and register platform timer
            Serial.WriteString("[KERNEL]   - Initializing timer manager...\n");
            TimerManager.Initialize();

            var initializer = PlatformHAL.Initializer;
            if (initializer is not null)
            {
                var timer = initializer.CreateTimer();
                TimerManager.RegisterTimer(timer);

                using (InternalCpu.DisableInterruptsScope())
                {

                    // Initialize Keyboard Manager and register platform keyboards
                    if (KeyboardManager.IsEnabled)
                    {
                        Serial.WriteString("[KERNEL]   - Initializing keyboard manager...\n");
                        KeyboardManager.Initialize();
                        var keyboards = initializer.GetKeyboardDevices();
                        foreach (var keyboard in keyboards)
                        {
                            KeyboardManager.RegisterKeyboard(keyboard);
                        }
                    }

                    // Initialize Network Manager and register platform network device
                    if (NetworkManager.IsEnabled)
                    {
                        Serial.WriteString("[KERNEL]   - Initializing network manager...\n");
                        NetworkManager.Initialize();
                        var networkDevice = initializer.GetNetworkDevice();
                        if (networkDevice != null)
                        {
                            NetworkManager.RegisterDevice(networkDevice);
                        }
                    }
                }
            }
        }
    }
}
