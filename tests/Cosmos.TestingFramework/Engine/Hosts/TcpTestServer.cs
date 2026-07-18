using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cosmos.TestingFramework.Engine.Hosts;

/// <summary>
/// TCP test server for network testing with QEMU guests.
/// Handles three test scenarios:
/// 1. ClientConnectTest: Listens for kernel's TCP connection, receives data and echoes it back
/// 2. ServerConnectTest: Connects to the kernel's TCP server and sends data, expects echo
/// 3. CloseNoPeerFinTest: Listens for kernel's TCP connection, echoes, then deliberately
///    keeps its side of the connection open — the kernel's Close() must still succeed (issue #369)
/// </summary>
public class TcpTestServer : IDisposable
{
    private readonly int _listenPort;
    private readonly int _connectPort;
    private readonly int _lingerPort;
    private TcpListener? _listener;
    private TcpListener? _lingerListener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private Task? _connectTask;
    private Task? _lingerTask;
    private bool _disposed;

    /// <summary>
    /// Number of connections accepted.
    /// </summary>
    public int ConnectionsAccepted { get; private set; }

    /// <summary>
    /// Number of echo responses sent.
    /// </summary>
    public int EchosSent { get; private set; }

    /// <summary>
    /// Last data received from kernel.
    /// </summary>
    public string? LastReceivedFromKernel { get; private set; }

    /// <summary>
    /// Creates a new TCP test server.
    /// </summary>
    /// <param name="listenPort">Port to listen for kernel connections (default 5557)</param>
    /// <param name="connectPort">Port to connect to kernel's server (default 5558)</param>
    /// <param name="lingerPort">Port to listen for kernel connections that are echoed but never closed from this side (default 5559)</param>
    public TcpTestServer(int listenPort = 5557, int connectPort = 5558, int lingerPort = 5559)
    {
        _listenPort = listenPort;
        _connectPort = connectPort;
        _lingerPort = lingerPort;
    }

    /// <summary>
    /// Starts the TCP test server tasks.
    /// </summary>
    public void Start()
    {
        if (_cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        ConnectionsAccepted = 0;
        EchosSent = 0;
        LastReceivedFromKernel = null;

        // Task 1: Listen for kernel's TCP connections (kernel connects to us)
        try
        {
            _listener = new TcpListener(IPAddress.Any, _listenPort);
            _listener.Start();
            _listenerTask = AcceptConnectionsAsync(_cts.Token);
            Console.WriteLine($"[TcpTestServer] Listening for kernel connections on port {_listenPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpTestServer] Failed to start listener: {ex.Message}");
        }

        // Task 2: Connect to kernel's TCP server after delay (kernel listens, we connect)
        try
        {
            _connectTask = ConnectToKernelAsync(_cts.Token);
            Console.WriteLine($"[TcpTestServer] Will connect to kernel on port {_connectPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpTestServer] Failed to start connect task: {ex.Message}");
        }

        // Task 3: Listen for kernel connections that we echo but never close
        // ourselves — validates the kernel's Close() against a lingering peer.
        try
        {
            _lingerListener = new TcpListener(IPAddress.Any, _lingerPort);
            _lingerListener.Start();
            _lingerTask = AcceptLingerConnectionsAsync(_cts.Token);
            Console.WriteLine($"[TcpTestServer] Listening for lingering-close connections on port {_lingerPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpTestServer] Failed to start linger listener: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the TCP server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        _listener?.Stop();
        _lingerListener?.Stop();

        try
        {
            if (_listenerTask != null)
            {
                await _listenerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        try
        {
            if (_connectTask != null)
            {
                await _connectTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        try
        {
            if (_lingerTask != null)
            {
                await _lingerTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        _cts.Dispose();
        _cts = null;
        _listener = null;
        _lingerListener = null;

        Console.WriteLine($"[TcpTestServer] Stopped. Connections: {ConnectionsAccepted}, Echos: {EchosSent}");
        if (LastReceivedFromKernel != null)
        {
            Console.WriteLine($"[TcpTestServer] Last received from kernel: {LastReceivedFromKernel}");
        }
    }

    /// <summary>
    /// Accepts incoming TCP connections from kernel and echoes received data.
    /// Kernel sends "COSMOS_TCP_TEST", we echo it back.
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                ConnectionsAccepted++;
                Console.WriteLine($"[TcpTestServer] Accepted connection from {client.Client.RemoteEndPoint}");

                // Handle client in background
                _ = HandleClientAsync(client, cancellationToken);
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
                Console.WriteLine($"[TcpTestServer] Accept error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles a client connection - receives data and echoes it back.
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[1024];

                // Read data from kernel
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    LastReceivedFromKernel = receivedData;
                    Console.WriteLine($"[TcpTestServer] Received from kernel: '{receivedData}'");

                    // Echo back
                    await stream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    EchosSent++;
                    Console.WriteLine($"[TcpTestServer] Echoed '{receivedData}' back to kernel");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpTestServer] Client handler error: {ex.Message}");
        }
    }

    /// <summary>
    /// Accepts kernel connections on the linger port. Each connection is
    /// echoed once and then deliberately held open — no FIN from this side —
    /// until the run is torn down. Reproduces a peer that lingers after the
    /// kernel calls Close() (issue #369).
    /// </summary>
    private async Task AcceptLingerConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _lingerListener!.AcceptTcpClientAsync(cancellationToken);
                Console.WriteLine($"[TcpTestServer] Accepted lingering-close connection from {client.Client.RemoteEndPoint}");

                // Handle client in background
                _ = HandleLingerClientAsync(client, cancellationToken);
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
                Console.WriteLine($"[TcpTestServer] Linger accept error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Echoes the kernel's data back, then keeps the connection open without
    /// closing until cancellation. The kernel's FIN is ACKed by the OS stack
    /// but this side never sends its own FIN while the test is running.
    /// </summary>
    private async Task HandleLingerClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[1024];

            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead > 0)
            {
                string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"[TcpTestServer] Linger peer received: '{receivedData}', echoing and holding the connection open");

                await stream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            }

            // Hold our half of the connection open until the run ends.
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpTestServer] Linger client handler error: {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Connects to kernel's TCP server and sends test data.
    /// Kernel listens, we connect and send "TEST_FROM_HOST", expect echo.
    /// </summary>
    private async Task ConnectToKernelAsync(CancellationToken cancellationToken)
    {
        // Wait for kernel to initialize and start listening
        await Task.Delay(4000, cancellationToken);

        int attempts = 0;
        while (!cancellationToken.IsCancellationRequested && attempts < 10)
        {
            try
            {
                using var client = new TcpClient();
                Console.WriteLine($"[TcpTestServer] Attempting to connect to kernel at localhost:{_connectPort} (attempt {attempts + 1})");

                // Connect to kernel's listening socket via QEMU port forward
                await client.ConnectAsync(IPAddress.Loopback, _connectPort, cancellationToken);
                Console.WriteLine($"[TcpTestServer] Connected to kernel");

                var stream = client.GetStream();

                // Send test data
                byte[] testData = Encoding.ASCII.GetBytes("TEST_FROM_HOST");
                await stream.WriteAsync(testData, 0, testData.Length, cancellationToken);
                Console.WriteLine($"[TcpTestServer] Sent 'TEST_FROM_HOST' to kernel");

                // Wait for echo
                var buffer = new byte[1024];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    string echoData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"[TcpTestServer] Received echo from kernel: '{echoData}'");
                }

                // Success - exit loop
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TcpTestServer] Connect attempt {attempts + 1} failed: {ex.Message}");
                attempts++;
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _listener?.Stop();
        _lingerListener?.Stop();
        _cts?.Dispose();
    }
}
