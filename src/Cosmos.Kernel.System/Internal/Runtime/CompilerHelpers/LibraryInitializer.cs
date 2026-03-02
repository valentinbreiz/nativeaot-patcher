using Cosmos.Kernel.Core;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.Core.Runtime;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timer;

namespace Internal.Runtime.CompilerHelpers
{
    /// <summary>
    /// This class is responsible for initializing the library and its dependencies. It is called by the runtime before any managed code is executed.
    /// </summary>
    public class LibraryInitializer
    {
        /// <summary>
        /// Initialize all enabled services provided by Cosmos.Kernel.System, such as TimerManager, KeyboardManager, and NetworkManager. This method is called by the runtime before any managed code is executed.
        /// </summary>
        public static void InitializeLibrary()
        {
            var initializer = PlatformHAL.Initializer;
            if (initializer is not null)
            {
                // Initialize Timer Manager (skipped if CosmosEnableTimer=false)
                if (CosmosFeatures.TimerEnabled)
                {
                    Serial.WriteString("[KERNEL]   - Initializing timer manager...\n");
                    TimerManager.Initialize();
                    var timer = initializer.CreateTimer();
                    TimerManager.RegisterTimer(timer);
                }

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

                    // Initialize Mouse Manager and register mouse
                    if (MouseManager.IsEnabled)
                    {
                        Serial.WriteString("[KERNEL]   - Initializing mouse manager...\n");
                        MouseManager.Initialize();
                        var mice = initializer.GetMouseDevices();
                        foreach (var mouse in mice)
                        {
                            MouseManager.RegisterMouse(mouse);
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
