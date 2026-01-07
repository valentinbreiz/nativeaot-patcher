using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// UDP test server for network testing with QEMU guests.
/// Handles two test scenarios:
/// 1. SendTest: Listens for kernel's packets and echoes them back
/// 2. ReceiveTest: Sends periodic packets to the kernel
/// </summary>
public class UdpTestServer : IDisposable
{
    private readonly int _sendTestPort;
    private readonly int _receiveTestPort;
    private UdpClient? _sendTestClient;
    private UdpClient? _receiveTestClient;
    private CancellationTokenSource? _cts;
    private Task? _sendTestTask;
    private Task? _receiveTestTask;
    private bool _disposed;

    /// <summary>
    /// Number of packets sent for receive test.
    /// </summary>
    public int ReceiveTestPacketsSent { get; private set; }

    /// <summary>
    /// Number of echo packets sent for send test.
    /// </summary>
    public int SendTestEchosSent { get; private set; }

    /// <summary>
    /// Last data received from kernel's send test.
    /// </summary>
    public string? LastReceivedFromKernel { get; private set; }

    /// <summary>
    /// Creates a new UDP test server.
    /// </summary>
    /// <param name="sendTestPort">Port for kernel's send test (default 5555)</param>
    /// <param name="receiveTestPort">Port for kernel's receive test (default 5556)</param>
    public UdpTestServer(int sendTestPort = 5555, int receiveTestPort = 5556)
    {
        _sendTestPort = sendTestPort;
        _receiveTestPort = receiveTestPort;
    }

    /// <summary>
    /// Starts the UDP test server tasks.
    /// </summary>
    public void Start()
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        ReceiveTestPacketsSent = 0;
        SendTestEchosSent = 0;
        LastReceivedFromKernel = null;

        // Task 1: Send periodic "TEST_FROM_HOST" packets for kernel's TestUDPReceivePacket
        try
        {
            _receiveTestClient = new UdpClient();
            _receiveTestTask = SendPeriodicPacketsAsync(_cts.Token);
            Console.WriteLine($"[UdpTestServer] Will send TEST_FROM_HOST to guest port {_receiveTestPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UdpTestServer] Failed to start receive test sender: {ex.Message}");
        }

        // Task 2: Listen for kernel's "COSMOS_UDP_TEST" and echo it back for TestUDPSendPacket
        try
        {
            _sendTestClient = new UdpClient(_sendTestPort);
            _sendTestTask = ListenAndEchoAsync(_cts.Token);
            Console.WriteLine($"[UdpTestServer] Listening for send test on port {_sendTestPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UdpTestServer] Failed to start send test listener: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the UDP server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _cts.Cancel();

        // Close clients
        _receiveTestClient?.Close();
        _sendTestClient?.Close();

        // Wait for tasks to complete
        try
        {
            if (_receiveTestTask != null)
                await _receiveTestTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        try
        {
            if (_sendTestTask != null)
                await _sendTestTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        _cts.Dispose();
        _cts = null;
        _receiveTestClient = null;
        _sendTestClient = null;

        Console.WriteLine($"[UdpTestServer] Stopped. ReceiveTest packets: {ReceiveTestPacketsSent}, SendTest echos: {SendTestEchosSent}");
        if (LastReceivedFromKernel != null)
            Console.WriteLine($"[UdpTestServer] Last received from kernel: {LastReceivedFromKernel}");
    }

    /// <summary>
    /// Sends periodic UDP packets to the kernel for TestUDPReceivePacket.
    /// The kernel listens on port 5556 and we send to localhost:5556 which QEMU forwards to the guest.
    /// </summary>
    private async Task SendPeriodicPacketsAsync(CancellationToken cancellationToken)
    {
        // Wait a bit for kernel to initialize network stack
        await Task.Delay(3000, cancellationToken);

        var endpoint = new IPEndPoint(IPAddress.Loopback, _receiveTestPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Send test packet to kernel
                byte[] testData = Encoding.ASCII.GetBytes("TEST_FROM_HOST");
                await _receiveTestClient!.SendAsync(testData, testData.Length, endpoint);
                ReceiveTestPacketsSent++;

                Console.WriteLine($"[UdpTestServer] Sent TEST_FROM_HOST to {endpoint}");

                // Wait before sending next packet
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpTestServer] Send error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Listens for kernel's UDP packets and echoes them back for TestUDPSendPacket.
    /// The kernel sends "COSMOS_UDP_TEST" to port 5555, we receive it and echo back.
    /// </summary>
    private async Task ListenAndEchoAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[UdpTestServer] Listening for kernel packets on port {_sendTestPort}...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Receive packet from kernel
                var result = await _sendTestClient!.ReceiveAsync(cancellationToken);
                string receivedData = Encoding.ASCII.GetString(result.Buffer);
                LastReceivedFromKernel = receivedData;

                Console.WriteLine($"[UdpTestServer] Received from kernel: '{receivedData}' from {result.RemoteEndPoint}");

                // Echo the data back to the kernel
                await _sendTestClient.SendAsync(result.Buffer, result.Buffer.Length, result.RemoteEndPoint);
                SendTestEchosSent++;

                Console.WriteLine($"[UdpTestServer] Echoed '{receivedData}' back to {result.RemoteEndPoint}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpTestServer] Listen error: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        _receiveTestClient?.Dispose();
        _sendTestClient?.Dispose();
        _cts?.Dispose();
    }
}
