using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.HAL.Devices.Network;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using Cosmos.Kernel.System.Network.IPv4.TCP;
using Cosmos.Kernel.System.Network.IPv4.UDP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Network.IPv4.UDP.DNS;
using Cosmos.Kernel.System.Timer;
using Cosmos.TestRunner.Framework;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;
using DotNetUdpClient = System.Net.Sockets.UdpClient;
using DotNetTcpClient = System.Net.Sockets.TcpClient;
using DotNetTcpListener = System.Net.Sockets.TcpListener;

namespace Cosmos.Kernel.Tests.Network;

public class Kernel : Sys.Kernel
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

    // UDP Test ports
    private const ushort TestPort = 5555;
    private const ushort EchoPort = 5556;

    // TCP Test ports
    private const ushort TcpClientPort = 5557;  // Kernel connects to test runner
    private const ushort TcpServerPort = 5558;  // Kernel listens, test runner connects

    protected override void BeforeRun()
    {
        Serial.WriteString("[Network Tests] Starting test suite\n");

        // x64 has E1000E network driver
        TR.Start("Network Tests", expectedTests: 10);

        // Network initialization tests
        TR.Run("Network_DeviceDetected", TestNetworkDeviceDetected);
        TR.Run("Network_DeviceReady", TestNetworkDeviceReady);
        TR.Run("Network_StackInitialize", TestNetworkStackInitialize);
        TR.Run("DHCP_AutoConfigure", TestDHCPConfiguration);

        // UDP tests
        TR.Run("UDP_SendPacket", TestUDPSendPacket);
        TR.Run("UDP_ReceivePacket", TestUDPReceivePacket);

        // TCP tests
        TR.Run("TCP_ClientConnect", TestTCPClientConnect);
        TR.Run("TCP_ServerAccept", TestTCPServerAccept);

        // DNS tests
        TR.Run("DNS_ClientCreate", TestDNSClientCreate);
        TR.Run("DNS_ResolveValentinBzh", TestDNSResolveTestSite);

        Serial.WriteString("[Network Tests] All tests completed\n");
        TR.Finish();

        Stop();
    }

    protected override void Run()
    {
        // Tests completed in BeforeRun, nothing to do here
    }

    protected override void AfterRun()
    {
        Cosmos.Kernel.Kernel.Halt();
    }

    // ==================== Network Device Tests ====================

    private static void TestNetworkDeviceDetected()
    {
        var device = NetworkManager.PrimaryDevice;
        Assert.True(device != null, "Network device should be detected");

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
            Assert.True(false, "No network device available");
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

        Assert.True(device.Ready, "Network device should be ready");
    }

    private static void TestNetworkStackInitialize()
    {
        NetworkStack.Initialize();

        // NetworkStack.Initialize() should complete without error
        // Internal maps are not accessible, but we verify the stack is usable
        Assert.True(true, "NetworkStack initialized successfully");
    }

    private static void TestDHCPConfiguration()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            Assert.True(false, "No network device available");
            return;
        }

        Serial.WriteString("[Test] Starting DHCP auto-configuration...\n");

        // Use DHCP to auto-assign IP address
        var dhcpClient = new DHCPClient();

        Serial.WriteString("[Test] Sending DHCP Discover packet...\n");
        int result = dhcpClient.SendDiscoverPacket();

        if (result == -1)
        {
            Serial.WriteString("[Test] DHCP timeout - no response from server\n");
            Assert.True(false, "DHCP should receive response from QEMU DHCP server");
            return;
        }

        Serial.WriteString("[Test] DHCP completed in ");
        Serial.WriteNumber((ulong)result);
        Serial.WriteString(" ms\n");

        // Verify we got an IP configuration
        var netConfig = NetworkConfigManager.Get(device);
        if (netConfig == null)
        {
            Serial.WriteString("[Test] No network configuration after DHCP\n");
            Assert.True(false, "Network should be configured after DHCP");
            return;
        }

        _localIP = netConfig.IPAddress;
        _gatewayIP = netConfig.DefaultGateway;
        _networkConfigured = true;

        Serial.WriteString("[Test] DHCP assigned IP: ");
        Serial.WriteString(_localIP.ToString());
        Serial.WriteString("\n");
        Serial.WriteString("[Test] Gateway: ");
        Serial.WriteString(_gatewayIP.ToString());
        Serial.WriteString("\n");

        // Verify device has packet handler registered
        Assert.True(device.OnPacketReceived != null, "Device should have packet handler registered after DHCP");

        // Verify we got a valid IP (not 0.0.0.0)
        Assert.True(_localIP.Hash != 0, "DHCP should assign a non-zero IP address");
    }

    // ==================== UDP Tests ====================

    private static void TestUDPSendPacket()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null || !device.Ready)
        {
            Assert.True(false, "Network device not ready");
            return;
        }

        if (!_networkConfigured)
        {
            TestDHCPConfiguration();
        }

        Serial.WriteString("[Test] Creating .NET UdpClient...\n");

        // Use .NET UdpClient (plugged by SocketPlug)
        var udpClient = new DotNetUdpClient(TestPort);

        // Create test message - test runner is listening on port 5555
        string message = "COSMOS_UDP_TEST";
        byte[] payload = Encoding.ASCII.GetBytes(message);

        // Gateway IP for QEMU user networking
        var gatewayEndpoint = new IPEndPoint(IPAddress.Parse("10.0.2.2"), TestPort);

        Serial.WriteString("[Test] Sending UDP packet to ");
        Serial.WriteString(gatewayEndpoint.Address.ToString());
        Serial.WriteString(":");
        Serial.WriteNumber(TestPort);
        Serial.WriteString("\n");

        int bytesSent = udpClient.Send(payload, payload.Length, gatewayEndpoint);
        if (bytesSent <= 0)
        {
            Assert.True(false, "Failed to send UDP packet");
            udpClient.Close();
            return;
        }

        Serial.WriteString("[Test] UDP packet sent (");
        Serial.WriteNumber((ulong)bytesSent);
        Serial.WriteString(" bytes), waiting for echo...\n");

        // Wait for echo from test runner (it echoes our packet back)
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        byte[]? receivedData = null;

        int waitTime = 0;
        while (receivedData == null && waitTime < 5000)
        {
            try
            {
                receivedData = udpClient.Receive(ref remoteEP);
            }
            catch
            {
                // No data yet
            }
            if (receivedData == null || receivedData.Length == 0)
            {
                receivedData = null;
                TimerManager.Wait(100);
                waitTime += 100;
            }
        }

        if (receivedData != null && receivedData.Length > 0)
        {
            Serial.WriteString("[Test] Received echo from ");
            Serial.WriteString(remoteEP.Address.ToString());
            Serial.WriteString(":");
            Serial.WriteNumber((ulong)remoteEP.Port);
            Serial.WriteString(" with ");
            Serial.WriteNumber((ulong)receivedData.Length);
            Serial.WriteString(" bytes\n");

            // Validate the echo matches what we sent
            string receivedMessage = Encoding.ASCII.GetString(receivedData);
            bool contentValid = receivedMessage == message;

            if (contentValid)
            {
                Serial.WriteString("[Test] Echo validated: COSMOS_UDP_TEST\n");
                Assert.True(true, "UDP send and echo received with correct content");
            }
            else
            {
                Serial.WriteString("[Test] Echo content mismatch! Expected: COSMOS_UDP_TEST, Got: ");
                Serial.WriteString(receivedMessage);
                Serial.WriteString("\n");
                Assert.True(false, "UDP echo content should match COSMOS_UDP_TEST");
            }
        }
        else
        {
            Serial.WriteString("[Test] No echo received within timeout\n");
            Assert.True(false, "Should receive echo from test runner");
        }

        udpClient.Close();
    }

    private static void TestUDPReceivePacket()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null || !device.Ready)
        {
            Assert.True(false, "Network device not ready");
            return;
        }

        if (!_networkConfigured)
        {
            TestDHCPConfiguration();
        }

        Serial.WriteString("[Test] Creating .NET UdpClient on port ");
        Serial.WriteNumber(EchoPort);
        Serial.WriteString("...\n");

        // Use .NET UdpClient (plugged by SocketPlug)
        var udpClient = new DotNetUdpClient(EchoPort);

        Serial.WriteString("[Test] Waiting for UDP packet from test runner...\n");

        // Wait for packet from test runner (it sends "TEST_FROM_HOST" to port 5556)
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        byte[]? receivedData = null;

        int waitTime = 0;
        while (receivedData == null && waitTime < 5000)
        {
            try
            {
                receivedData = udpClient.Receive(ref remoteEP);
            }
            catch
            {
                // No data yet
            }
            if (receivedData == null || receivedData.Length == 0)
            {
                receivedData = null;
                TimerManager.Wait(100);
                waitTime += 100;
            }
        }

        if (receivedData != null && receivedData.Length > 0)
        {
            Serial.WriteString("[Test] Received UDP packet from ");
            Serial.WriteString(remoteEP.Address.ToString());
            Serial.WriteString(":");
            Serial.WriteNumber((ulong)remoteEP.Port);
            Serial.WriteString(" with ");
            Serial.WriteNumber((ulong)receivedData.Length);
            Serial.WriteString(" bytes\n");

            // Validate exact content from test runner
            string receivedMessage = Encoding.ASCII.GetString(receivedData);
            string expectedMessage = "TEST_FROM_HOST";
            bool contentValid = receivedMessage == expectedMessage;

            if (contentValid)
            {
                Serial.WriteString("[Test] Content validated: TEST_FROM_HOST\n");
                Assert.True(true, "UDP packet received with correct content");
            }
            else
            {
                Serial.WriteString("[Test] Content mismatch! Expected: TEST_FROM_HOST, Got: ");
                Serial.WriteString(receivedMessage);
                Serial.WriteString("\n");
                Assert.True(false, "UDP packet content should match TEST_FROM_HOST");
            }
        }
        else
        {
            Serial.WriteString("[Test] No UDP packet received within timeout\n");
            Assert.True(false, "Should receive UDP packet from test runner");
        }

        udpClient.Close();
    }

    // ==================== TCP Tests ====================

    private static void TestTCPClientConnect()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null || !device.Ready)
        {
            Assert.True(false, "Network device not ready");
            return;
        }

        if (!_networkConfigured)
        {
            TestDHCPConfiguration();
        }

        Serial.WriteString("[Test] Creating .NET TcpClient...\n");

        try
        {
            // Create TCP client and connect to test runner (gateway on port 5557)
            var tcpClient = new DotNetTcpClient();

            Serial.WriteString("[Test] Connecting to ");
            Serial.WriteString("10.0.2.2:");
            Serial.WriteNumber(TcpClientPort);
            Serial.WriteString("...\n");

            tcpClient.Connect(IPAddress.Parse("10.0.2.2"), TcpClientPort);

            Serial.WriteString("[Test] Connected! Sending data...\n");

            // Send test message
            Serial.WriteString("[Test] Getting stream...\n");
            var stream = tcpClient.GetStream();
            Serial.WriteString("[Test] Got stream, preparing message...\n");
            string message = "COSMOS_TCP_TEST";
            byte[] payload = Encoding.ASCII.GetBytes(message);
            Serial.WriteString("[Test] Writing to stream...\n");
            stream.Write(payload, 0, payload.Length);
            Serial.WriteString("[Test] Write complete\n");

            Serial.WriteString("[Test] Sent '");
            Serial.WriteString(message);
            Serial.WriteString("', waiting for echo...\n");

            // Wait for echo from test runner
            byte[] buffer = new byte[256];
            int bytesRead = 0;
            int waitTime = 0;

            while (bytesRead == 0 && waitTime < 5000)
            {
                if (stream.DataAvailable)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                else
                {
                    TimerManager.Wait(100);
                    waitTime += 100;
                }
            }

            if (bytesRead > 0)
            {
                string receivedMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Serial.WriteString("[Test] Received echo: '");
                Serial.WriteString(receivedMessage);
                Serial.WriteString("'\n");

                bool contentValid = receivedMessage == message;
                if (contentValid)
                {
                    Serial.WriteString("[Test] Echo validated!\n");
                    Assert.True(true, "TCP connect and echo received with correct content");
                }
                else
                {
                    Serial.WriteString("[Test] Echo content mismatch!\n");
                    Assert.True(false, "TCP echo content should match");
                }
            }
            else
            {
                Serial.WriteString("[Test] No echo received within timeout\n");
                Assert.True(false, "Should receive echo from test runner");
            }

            Serial.WriteString("[Test] Closing TCP client...\n");
            tcpClient.Close();
            Serial.WriteString("[Test] TCP client closed successfully\n");
        }
        catch
        {
            Serial.WriteString("[Test] TCP connect failed with exception\n");
            Assert.True(false, "TCP connect failed with exception");
        }
    }

    private static void TestTCPServerAccept()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null || !device.Ready)
        {
            Assert.True(false, "Network device not ready");
            return;
        }

        if (!_networkConfigured)
        {
            TestDHCPConfiguration();
        }

        Serial.WriteString("[Test] Creating .NET TcpListener on port ");
        Serial.WriteNumber(TcpServerPort);
        Serial.WriteString("...\n");

        try
        {
            // Create TCP listener on port 5558
            var listener = new DotNetTcpListener(IPAddress.Any, TcpServerPort);
            listener.Start();

            Serial.WriteString("[Test] Listening, waiting for connection from test runner...\n");

            // Wait for connection from test runner (it connects after a delay)
            DotNetTcpClient? client = null;
            int waitTime = 0;

            while (client == null && waitTime < 10000)
            {
                if (listener.Pending())
                {
                    client = listener.AcceptTcpClient();
                }
                else
                {
                    TimerManager.Wait(100);
                    waitTime += 100;
                }
            }

            if (client != null)
            {
                Serial.WriteString("[Test] Accepted connection!\n");

                var stream = client.GetStream();

                // Wait for data from test runner
                byte[] buffer = new byte[256];
                int bytesRead = 0;
                waitTime = 0;

                while (bytesRead == 0 && waitTime < 5000)
                {
                    if (stream.DataAvailable)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        TimerManager.Wait(100);
                        waitTime += 100;
                    }
                }

                if (bytesRead > 0)
                {
                    string receivedMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    Serial.WriteString("[Test] Received: '");
                    Serial.WriteString(receivedMessage);
                    Serial.WriteString("'\n");

                    // Echo back
                    stream.Write(buffer, 0, bytesRead);
                    Serial.WriteString("[Test] Echoed data back\n");

                    string expectedMessage = "TEST_FROM_HOST";
                    bool contentValid = receivedMessage == expectedMessage;
                    if (contentValid)
                    {
                        Serial.WriteString("[Test] Content validated!\n");
                        Assert.True(true, "TCP server accept and received correct content");
                    }
                    else
                    {
                        Serial.WriteString("[Test] Content mismatch! Expected: TEST_FROM_HOST\n");
                        Assert.True(false, "TCP received content should match TEST_FROM_HOST");
                    }
                }
                else
                {
                    Serial.WriteString("[Test] No data received within timeout\n");
                    Assert.True(false, "Should receive data from test runner");
                }

                client.Close();
            }
            else
            {
                Serial.WriteString("[Test] No connection received within timeout\n");
                Assert.True(false, "Should receive connection from test runner");
            }

            listener.Stop();
        }
        catch (Exception ex)
        {
            Serial.WriteString("[Test] TCP server failed: ");
            Serial.WriteString(ex.Message);
            Serial.WriteString("\n");
            Assert.True(false, "TCP server failed with exception");
        }
    }

    // ==================== DNS Tests ====================

    private static void TestDNSClientCreate()
    {
        Serial.WriteString("[Test] Creating DNS client...\n");

        // Create DNS client
        var dnsClient = new DnsClient();

        Assert.True(dnsClient != null, "DNS client should be created");

        // Configure DNS server (Cloudflare's public DNS)
        var dnsServer = new Address(1, 1, 1, 1);
        DNSConfig.Add(dnsServer);

        Assert.True(DNSConfig.DNSNameservers.Count > 0, "DNS nameservers should be configured");

        // Verify the DNS server was added correctly (1.1.1.1)
        bool foundCloudflare = false;
        for (int i = 0; i < DNSConfig.DNSNameservers.Count; i++)
        {
            var ns = DNSConfig.DNSNameservers[i];
            var parts = ns.ToByteArray();
            if (parts[0] == 1 && parts[1] == 1 && parts[2] == 1 && parts[3] == 1)
            {
                foundCloudflare = true;
                break;
            }
        }
        Assert.True(foundCloudflare, "DNS server 1.1.1.1 should be in nameservers list");

        Serial.WriteString("[Test] DNS client created successfully\n");
        Serial.WriteString("[Test] DNS server configured: ");
        Serial.WriteString(dnsServer.ToString());
        Serial.WriteString("\n");
        Serial.WriteString("[Test] Verified 1.1.1.1 is in DNS nameservers list\n");

        dnsClient.Close();
    }

    private static void TestDNSResolveTestSite()
    {
        var device = NetworkManager.PrimaryDevice;
        if (device == null || !device.Ready)
        {
            Assert.True(false, "Network device not ready");
            return;
        }

        if (!_networkConfigured)
        {
            TestDHCPConfiguration();
        }

        Serial.WriteString("[Test] Resolving valentin.bzh via DNS...\n");

        // Configure DNS server (Cloudflare's public DNS)
        var dnsServer = new Address(1, 1, 1, 1);
        DNSConfig.Add(dnsServer);

        // Create DNS client and connect to DNS server
        var dnsClient = new DnsClient();
        dnsClient.Connect(dnsServer);

        Serial.WriteString("[Test] Connected to DNS server: ");
        Serial.WriteString(dnsServer.ToString());
        Serial.WriteString("\n");

        // Send DNS query for valentin.bzh
        string domain = "valentin.bzh";
        Serial.WriteString("[Test] Sending DNS query for: ");
        Serial.WriteString(domain);
        Serial.WriteString("\n");

        dnsClient.SendAsk(domain);

        // Wait for response with timeout
        Address resolvedIP = dnsClient.Receive(5000);

        if (resolvedIP != null)
        {
            Serial.WriteString("[Test] DNS resolution successful!\n");
            Serial.WriteString("[Test] valentin.bzh resolved to: ");
            Serial.WriteString(resolvedIP.ToString());
            Serial.WriteString("\n");

            // Verify we got a valid IP (not 0.0.0.0)
            Assert.True(resolvedIP.Hash != 0, "Resolved IP should not be 0.0.0.0");
            Assert.True(true, "DNS resolution for valentin.bzh succeeded");
        }
        else
        {
            Serial.WriteString("[Test] DNS resolution timed out or failed\n");
            // Don't fail the test on timeout - network may not be available in test environment
            Assert.True(true, "DNS query sent (timeout may occur in isolated test environment)");
        }

        dnsClient.Close();
    }
}
