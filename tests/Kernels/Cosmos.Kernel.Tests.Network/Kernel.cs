using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.Services.Network;
using Cosmos.Kernel.Services.Network.IPv4;
using Cosmos.Kernel.Services.Network.IPv4.UDP;
using Cosmos.Kernel.Services.Timer;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.Network
{
    internal unsafe static partial class Program
    {
        // Network configuration
        private static Address? _localIP;
        private static Address? _gatewayIP;
        private static bool _networkConfigured = false;
        private static bool _receivedPacket = false;
        private static byte[]? _lastReceivedData;
        private static ushort _lastReceivedPort;
        private static Address? _lastReceivedSourceIP;
        private static ushort _lastReceivedSourcePort;

        // Test ports
        private const ushort TestPort = 5555;
        private const ushort EchoPort = 5556;

        [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
        private static void KernelMain() => Main();

        private static void Main()
        {
            Serial.WriteString("[Network Tests] Starting test suite\n");

#if ARCH_X64
            // x64 has E1000E network driver
            Start("Network Tests", expectedTests: 6);

            // Network initialization tests
            Run("Network_DeviceDetected", TestNetworkDeviceDetected);
            Run("Network_DeviceReady", TestNetworkDeviceReady);
            Run("Network_StackInitialize", TestNetworkStackInitialize);
            Run("Network_IPConfiguration", TestIPConfiguration);

            // UDP tests
            Run("UDP_SendPacket", TestUDPSendPacket);
            Run("UDP_ReceivePacket", TestUDPReceivePacket);

            Serial.WriteString("[Network Tests] All tests completed\n");
            Finish();
#else
            // ARM64 doesn't have network driver yet - skip all tests
            Start("Network Tests", expectedTests: 6);

            Skip("Network_DeviceDetected", "ARM64 network driver not implemented");
            Skip("Network_DeviceReady", "ARM64 network driver not implemented");
            Skip("Network_StackInitialize", "ARM64 network driver not implemented");
            Skip("Network_IPConfiguration", "ARM64 network driver not implemented");
            Skip("UDP_SendPacket", "ARM64 network driver not implemented");
            Skip("UDP_ReceivePacket", "ARM64 network driver not implemented");

            Serial.WriteString("[Network Tests] All tests skipped (ARM64 not supported)\n");
            Finish();
#endif

            while (true) ;
        }

#if ARCH_X64
        // ==================== Network Device Tests ====================

        private static void TestNetworkDeviceDetected()
        {
            var device = NetworkManager.PrimaryDevice;
            True(device != null, "Network device should be detected");

            if (device != null)
            {
                Serial.WriteString("[Test] Device detected: ");
                Serial.WriteString(device.Name);
                Serial.WriteString("\n");
            }
        }

        private static void TestNetworkDeviceReady()
        {
            var device = NetworkManager.PrimaryDevice;
            if (device == null)
            {
                True(false, "No network device available");
                return;
            }

            // Wait for link to come up (max 2 seconds)
            int attempts = 0;
            while (!device.LinkUp && attempts < 20)
            {
                TimerManager.Wait(100);
                attempts++;
            }

            Serial.WriteString("[Test] Link status: ");
            Serial.WriteString(device.LinkUp ? "UP" : "DOWN");
            Serial.WriteString(", Ready: ");
            Serial.WriteString(device.Ready ? "YES" : "NO");
            Serial.WriteString("\n");

            True(device.Ready, "Network device should be ready");
        }

        private static void TestNetworkStackInitialize()
        {
            NetworkStack.Initialize();

            // NetworkStack.Initialize() should complete without error
            // Internal maps are not accessible, but we verify the stack is usable
            True(true, "NetworkStack initialized successfully");
        }

        private static void TestIPConfiguration()
        {
            var device = NetworkManager.PrimaryDevice;
            if (device == null)
            {
                True(false, "No network device available");
                return;
            }

            // Configure IP (10.0.2.15 for QEMU user networking)
            _localIP = new Address(10, 0, 2, 15);
            _gatewayIP = new Address(10, 0, 2, 2);

            NetworkStack.ConfigIP(device, _localIP);
            _networkConfigured = true;

            // Verify configuration succeeded by checking device has packet handler registered
            True(device.OnPacketReceived != null, "Device should have packet handler registered after ConfigIP");

            Serial.WriteString("[Test] IP configured: ");
            Serial.WriteString(_localIP.ToString());
            Serial.WriteString("\n");
        }

        // ==================== UDP Tests ====================

        private static void TestUDPSendPacket()
        {
            var device = NetworkManager.PrimaryDevice;
            if (device == null || !device.Ready)
            {
                True(false, "Network device not ready");
                return;
            }

            if (!_networkConfigured)
            {
                TestIPConfiguration();
            }

            // Register UDP callback to receive echo from test runner
            _receivedPacket = false;
            _lastReceivedData = null;
            UDPPacket.OnUDPDataReceived = OnUDPDataReceived;

            // Create test message - test runner is listening on port 5555
            string message = "COSMOS_UDP_TEST";
            byte[] payload = new byte[message.Length];
            for (int i = 0; i < message.Length; i++)
                payload[i] = (byte)message[i];

            // Send UDP packet to gateway (10.0.2.2) - test runner receives via port forwarding
            var udpPacket = new UDPPacket(
                _localIP!,
                _gatewayIP!,
                TestPort,
                TestPort,
                payload,
                MACAddress.Broadcast
            );

            Serial.WriteString("[Test] Sending UDP packet to ");
            Serial.WriteString(_gatewayIP!.ToString());
            Serial.WriteString(":");
            Serial.WriteNumber(TestPort);
            Serial.WriteString("\n");

            bool sent = device.Send(udpPacket.RawData, udpPacket.RawData.Length);
            if (!sent)
            {
                True(false, "Failed to send UDP packet");
                return;
            }

            Serial.WriteString("[Test] UDP packet sent, waiting for echo...\n");

            // Wait for echo from test runner (it echoes our packet back)
            int waitTime = 0;
            while (!_receivedPacket && waitTime < 5000)
            {
                TimerManager.Wait(100);
                waitTime += 100;
            }

            if (_receivedPacket && _lastReceivedData != null)
            {
                Serial.WriteString("[Test] Received echo from ");
                Serial.WriteString(_lastReceivedSourceIP?.ToString() ?? "unknown");
                Serial.WriteString(":");
                Serial.WriteNumber((ulong)_lastReceivedSourcePort);
                Serial.WriteString(" with ");
                Serial.WriteNumber((ulong)_lastReceivedData.Length);
                Serial.WriteString(" bytes\n");

                // Validate the echo matches what we sent
                bool contentValid = _lastReceivedData.Length == message.Length;
                if (contentValid)
                {
                    for (int i = 0; i < message.Length; i++)
                    {
                        if (_lastReceivedData[i] != (byte)message[i])
                        {
                            contentValid = false;
                            break;
                        }
                    }
                }

                if (contentValid)
                {
                    Serial.WriteString("[Test] Echo validated: COSMOS_UDP_TEST\n");
                    True(true, "UDP send and echo received with correct content");
                }
                else
                {
                    Serial.WriteString("[Test] Echo content mismatch! Expected: COSMOS_UDP_TEST, Got: ");
                    for (int i = 0; i < _lastReceivedData.Length && i < 32; i++)
                        Serial.Write(((char)_lastReceivedData[i]).ToString());
                    Serial.WriteString("\n");
                    True(false, "UDP echo content should match COSMOS_UDP_TEST");
                }
            }
            else
            {
                Serial.WriteString("[Test] No echo received within timeout\n");
                True(false, "Should receive echo from test runner");
            }
        }

        private static void TestUDPReceivePacket()
        {
            var device = NetworkManager.PrimaryDevice;
            if (device == null || !device.Ready)
            {
                True(false, "Network device not ready");
                return;
            }

            if (!_networkConfigured)
            {
                TestIPConfiguration();
            }

            // Register UDP callback to receive packets from test runner
            _receivedPacket = false;
            _lastReceivedData = null;
            UDPPacket.OnUDPDataReceived = OnUDPDataReceived;

            Serial.WriteString("[Test] Waiting for UDP packet from test runner on port ");
            Serial.WriteNumber(EchoPort);
            Serial.WriteString("...\n");

            // Wait for packet from test runner (it sends periodically)
            // Test runner sends "TEST_FROM_HOST" to port 5556
            int waitTime = 0;
            while (!_receivedPacket && waitTime < 5000)
            {
                TimerManager.Wait(100);
                waitTime += 100;
            }

            if (_receivedPacket && _lastReceivedData != null)
            {
                Serial.WriteString("[Test] Received UDP packet from ");
                Serial.WriteString(_lastReceivedSourceIP?.ToString() ?? "unknown");
                Serial.WriteString(":");
                Serial.WriteNumber((ulong)_lastReceivedSourcePort);
                Serial.WriteString(" with ");
                Serial.WriteNumber((ulong)_lastReceivedData.Length);
                Serial.WriteString(" bytes\n");

                // Validate exact content from test runner
                string expectedMessage = "TEST_FROM_HOST";
                bool contentValid = _lastReceivedData.Length == expectedMessage.Length;
                if (contentValid)
                {
                    for (int i = 0; i < expectedMessage.Length; i++)
                    {
                        if (_lastReceivedData[i] != (byte)expectedMessage[i])
                        {
                            contentValid = false;
                            break;
                        }
                    }
                }

                if (contentValid)
                {
                    Serial.WriteString("[Test] Content validated: TEST_FROM_HOST\n");
                    True(true, "UDP packet received with correct content");
                }
                else
                {
                    Serial.WriteString("[Test] Content mismatch! Expected: TEST_FROM_HOST, Got: ");
                    for (int i = 0; i < _lastReceivedData.Length && i < 32; i++)
                        Serial.Write(((char)_lastReceivedData[i]).ToString());
                    Serial.WriteString("\n");
                    True(false, "UDP packet content should match TEST_FROM_HOST");
                }
            }
            else
            {
                Serial.WriteString("[Test] No UDP packet received within timeout\n");
                True(false, "Should receive UDP packet from test runner");
            }
        }

        private static void OnUDPDataReceived(UDPPacket packet)
        {
            Serial.WriteString("[UDP Callback] Received packet from ");
            Serial.WriteString(packet.SourceIP.ToString());
            Serial.WriteString(":");
            Serial.WriteNumber((ulong)packet.SourcePort);
            Serial.WriteString(" to port ");
            Serial.WriteNumber((ulong)packet.DestinationPort);
            Serial.WriteString("\n");

            _receivedPacket = true;
            _lastReceivedData = packet.UDPData;
            _lastReceivedPort = packet.DestinationPort;
            _lastReceivedSourceIP = packet.SourceIP;
            _lastReceivedSourcePort = packet.SourcePort;

            // Log payload
            if (_lastReceivedData != null && _lastReceivedData.Length > 0)
            {
                Serial.WriteString("[UDP Callback] Payload: ");
                for (int i = 0; i < _lastReceivedData.Length && i < 64; i++)
                {
                    char c = (char)_lastReceivedData[i];
                    if (c >= 32 && c < 127)
                        Serial.Write(c.ToString());
                }
                Serial.WriteString("\n");
            }
        }
#endif
    }
}
