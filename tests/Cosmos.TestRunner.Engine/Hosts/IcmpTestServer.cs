using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// Raw-Ethernet ICMP test peer for network testing with QEMU guests.
/// Slirp's hostfwd only forwards TCP/UDP, so host-sourced ICMP cannot reach the
/// guest through the user-mode backend. QEMU instead connects a stream netdev
/// (hubbed with slirp and the guest NIC) to this server, which speaks just
/// enough ARP and ICMP to ping the guest:
/// 1. Resolves the guest MAC via ARP (and answers ARP requests for its own IP)
/// 2. Sends periodic ICMP echo requests with a COSMOS_PING payload
/// 3. After validating an echo reply, switches the payload to HOST_OK so the
///    kernel test can assert the full host-to-guest-to-host round trip
/// </summary>
public class IcmpTestServer : IDisposable
{
    private const string ProbePayloadPrefix = "COSMOS_PING";
    private const string AckPayloadPrefix = "HOST_OK";
    private const int IcmpPayloadLength = 32;
    private const ushort EchoId = 0x4353; // 'CS'

    private static readonly byte[] HostMac = { 0x52, 0x54, 0x00, 0x12, 0x34, 0x99 };
    private static readonly byte[] HostIp = { 10, 0, 2, 99 };
    private static readonly byte[] GuestIp = { 10, 0, 2, 15 }; // slirp's first DHCP address
    private static readonly byte[] BroadcastMac = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    private readonly int _port;
    private TcpListener? _listener;
    private TcpClient? _qemuClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    private byte[]? _guestMac;
    private ushort _nextSequence;
    private ushort _nextIpId = 0x4200;
    private bool _replyValidated;
    private readonly Dictionary<ushort, byte[]> _sentPayloads = new();

    /// <summary>
    /// Number of ICMP echo requests sent to the guest.
    /// </summary>
    public int EchoRequestsSent { get; private set; }

    /// <summary>
    /// Number of echo replies that matched a sent request (id, sequence,
    /// payload and ICMP checksum all verified).
    /// </summary>
    public int ValidEchoRepliesReceived { get; private set; }

    /// <summary>
    /// Whether the guest MAC was resolved (via its ARP reply or a learned frame).
    /// </summary>
    public bool GuestMacResolved => _guestMac != null;

    /// <summary>
    /// Creates a new ICMP test server.
    /// </summary>
    /// <param name="port">TCP port QEMU's stream netdev connects to (default 5560)</param>
    public IcmpTestServer(int port = 5560)
    {
        _port = port;
    }

    /// <summary>
    /// Starts listening. Must be called before QEMU starts: the stream netdev
    /// connects at QEMU startup and aborts the VM if the connection is refused.
    /// </summary>
    public void Start()
    {
        if (_cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        EchoRequestsSent = 0;
        ValidEchoRepliesReceived = 0;
        _guestMac = null;
        _replyValidated = false;
        _sentPayloads.Clear();

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _serverTask = RunAsync(_cts.Token);
            Console.WriteLine($"[IcmpTestServer] Listening for QEMU stream netdev on port {_port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IcmpTestServer] Failed to start listener: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the ICMP server.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();
        _qemuClient?.Close();
        _listener?.Stop();

        try
        {
            if (_serverTask != null)
            {
                await _serverTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }

        _cts.Dispose();
        _cts = null;
        _qemuClient = null;
        _stream = null;
        _listener = null;

        Console.WriteLine($"[IcmpTestServer] Stopped. Requests sent: {EchoRequestsSent}, valid replies: {ValidEchoRepliesReceived}");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _qemuClient = await _listener!.AcceptTcpClientAsync(cancellationToken);
            _stream = _qemuClient.GetStream();
            Console.WriteLine("[IcmpTestServer] QEMU connected");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IcmpTestServer] Accept failed: {ex.Message}");
            return;
        }

        var readTask = ReadFramesAsync(cancellationToken);
        var pingTask = PingLoopAsync(cancellationToken);

        try
        {
            await Task.WhenAll(readTask, pingTask);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Sends an ARP request until the guest MAC is known, then periodic ICMP
    /// echo requests. Payload starts as COSMOS_PING and switches to HOST_OK
    /// once a reply validated, acknowledging the round trip to the kernel.
    /// </summary>
    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        // Wait a bit for kernel to initialize network stack
        await Task.Delay(3000, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_guestMac == null)
                {
                    await SendFrameAsync(BuildArpRequest(), cancellationToken);
                }
                else
                {
                    ushort sequence = _nextSequence++;
                    byte[] payload = BuildPayload(_replyValidated ? AckPayloadPrefix : ProbePayloadPrefix);
                    lock (_sentPayloads)
                    {
                        _sentPayloads[sequence] = payload;
                    }

                    await SendFrameAsync(BuildEchoRequest(_guestMac, sequence, payload), cancellationToken);
                    EchoRequestsSent++;
                    Console.WriteLine($"[IcmpTestServer] Sent echo request seq={sequence} ({(_replyValidated ? AckPayloadPrefix : ProbePayloadPrefix)})");
                }

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
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IcmpTestServer] Ping error: {ex.Message}");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Reads length-prefixed Ethernet frames from QEMU and handles ARP and
    /// ICMP addressed to the host peer; everything else on the hub is ignored.
    /// </summary>
    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadExactAsync(lengthBuffer, 4, cancellationToken);
                int frameLength = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3];
                if (frameLength <= 0 || frameLength > 65535)
                {
                    Console.WriteLine($"[IcmpTestServer] Bad frame length {frameLength}, closing");
                    break;
                }

                var frame = new byte[frameLength];
                await ReadExactAsync(frame, frameLength, cancellationToken);
                HandleFrame(frame);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("QEMU stream closed");
            }
            offset += read;
        }
    }

    private void HandleFrame(byte[] frame)
    {
        if (frame.Length < 14)
        {
            return;
        }

        ushort etherType = (ushort)((frame[12] << 8) | frame[13]);
        if (etherType == 0x0806)
        {
            HandleArp(frame);
        }
        else if (etherType == 0x0800)
        {
            HandleIpv4(frame);
        }
    }

    private void HandleArp(byte[] frame)
    {
        // Ethernet(14) + ARP: htype(2) ptype(2) hlen(1) plen(1) op(2)
        //                     sha(6) spa(4) tha(6) tpa(4)
        if (frame.Length < 42)
        {
            return;
        }

        ushort operation = (ushort)((frame[20] << 8) | frame[21]);
        var senderMac = frame.AsSpan(22, 6);
        var senderIp = frame.AsSpan(28, 4);
        var targetIp = frame.AsSpan(38, 4);

        // Learn the guest MAC from any ARP frame it authored
        if (senderIp.SequenceEqual(GuestIp) && _guestMac == null)
        {
            _guestMac = senderMac.ToArray();
            Console.WriteLine($"[IcmpTestServer] Guest MAC resolved: {FormatMac(_guestMac)}");
        }

        // Answer requests for the host peer's IP so the guest can send us replies
        if (operation == 1 && targetIp.SequenceEqual(HostIp))
        {
            byte[] reply = BuildArpReply(senderMac.ToArray(), senderIp.ToArray());
            _ = SendFrameAsync(reply, _cts?.Token ?? CancellationToken.None);
            Console.WriteLine("[IcmpTestServer] Answered ARP request for host peer IP");
        }
    }

    private void HandleIpv4(byte[] frame)
    {
        if (frame.Length < 34)
        {
            return;
        }

        int ipStart = 14;
        int ipHeaderLength = (frame[ipStart] & 0x0F) * 4;
        byte protocol = frame[ipStart + 9];
        var destIp = frame.AsSpan(ipStart + 16, 4);

        if (protocol != 1 || !destIp.SequenceEqual(HostIp))
        {
            return;
        }

        int icmpStart = ipStart + ipHeaderLength;
        int totalLength = (frame[ipStart + 2] << 8) | frame[ipStart + 3];
        int icmpLength = totalLength - ipHeaderLength;
        if (icmpLength < 8 || icmpStart + icmpLength > frame.Length)
        {
            return;
        }

        byte icmpType = frame[icmpStart];
        if (icmpType != 0)
        {
            return;
        }

        ushort id = (ushort)((frame[icmpStart + 4] << 8) | frame[icmpStart + 5]);
        ushort sequence = (ushort)((frame[icmpStart + 6] << 8) | frame[icmpStart + 7]);

        if (id != EchoId)
        {
            return;
        }

        if (ComputeChecksum(frame, icmpStart, icmpLength) != 0)
        {
            Console.WriteLine($"[IcmpTestServer] Echo reply seq={sequence} has a bad ICMP checksum");
            return;
        }

        byte[]? sentPayload;
        lock (_sentPayloads)
        {
            _sentPayloads.TryGetValue(sequence, out sentPayload);
        }

        if (sentPayload == null || icmpLength - 8 != sentPayload.Length ||
            !frame.AsSpan(icmpStart + 8, sentPayload.Length).SequenceEqual(sentPayload))
        {
            Console.WriteLine($"[IcmpTestServer] Echo reply seq={sequence} payload mismatch");
            return;
        }

        ValidEchoRepliesReceived++;
        if (!_replyValidated)
        {
            _replyValidated = true;
            Console.WriteLine($"[IcmpTestServer] Valid echo reply seq={sequence} — switching payload to {AckPayloadPrefix}");
        }
        else
        {
            Console.WriteLine($"[IcmpTestServer] Valid echo reply seq={sequence}");
        }
    }

    private async Task SendFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        // Pad to the Ethernet minimum; real NIC models may drop runt frames
        if (frame.Length < 60)
        {
            Array.Resize(ref frame, 60);
        }

        var lengthPrefix = new byte[4]
        {
            (byte)(frame.Length >> 24),
            (byte)(frame.Length >> 16),
            (byte)(frame.Length >> 8),
            (byte)frame.Length
        };

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream!.WriteAsync(lengthPrefix, cancellationToken);
            await _stream.WriteAsync(frame, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static byte[] BuildPayload(string prefix)
    {
        var payload = new byte[IcmpPayloadLength];
        byte[] prefixBytes = Encoding.ASCII.GetBytes(prefix);
        Array.Copy(prefixBytes, payload, prefixBytes.Length);
        for (int i = prefixBytes.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)i;
        }

        return payload;
    }

    private static byte[] BuildArpRequest()
    {
        var frame = new byte[42];
        WriteEthernetHeader(frame, BroadcastMac, 0x0806);
        WriteArpBody(frame, 1, HostMac, HostIp, new byte[6], GuestIp);
        return frame;
    }

    private static byte[] BuildArpReply(byte[] targetMac, byte[] targetIp)
    {
        var frame = new byte[42];
        WriteEthernetHeader(frame, targetMac, 0x0806);
        WriteArpBody(frame, 2, HostMac, HostIp, targetMac, targetIp);
        return frame;
    }

    private static void WriteEthernetHeader(byte[] frame, byte[] destMac, ushort etherType)
    {
        Array.Copy(destMac, 0, frame, 0, 6);
        Array.Copy(HostMac, 0, frame, 6, 6);
        frame[12] = (byte)(etherType >> 8);
        frame[13] = (byte)etherType;
    }

    private static void WriteArpBody(byte[] frame, ushort operation, byte[] senderMac, byte[] senderIp, byte[] targetMac, byte[] targetIp)
    {
        frame[14] = 0x00;
        frame[15] = 0x01; // Ethernet
        frame[16] = 0x08;
        frame[17] = 0x00; // IPv4
        frame[18] = 6;
        frame[19] = 4;
        frame[20] = (byte)(operation >> 8);
        frame[21] = (byte)operation;
        Array.Copy(senderMac, 0, frame, 22, 6);
        Array.Copy(senderIp, 0, frame, 28, 4);
        Array.Copy(targetMac, 0, frame, 32, 6);
        Array.Copy(targetIp, 0, frame, 38, 4);
    }

    private byte[] BuildEchoRequest(byte[] guestMac, ushort sequence, byte[] payload)
    {
        int icmpLength = 8 + payload.Length;
        int ipLength = 20 + icmpLength;
        var frame = new byte[14 + ipLength];

        WriteEthernetHeader(frame, guestMac, 0x0800);

        // IPv4 header
        frame[14] = 0x45;
        frame[15] = 0x00;
        frame[16] = (byte)(ipLength >> 8);
        frame[17] = (byte)ipLength;
        ushort ipId = _nextIpId++;
        frame[18] = (byte)(ipId >> 8);
        frame[19] = (byte)ipId;
        frame[20] = 0x00;
        frame[21] = 0x00;
        frame[22] = 64; // TTL
        frame[23] = 1;  // ICMP
        frame[24] = 0x00;
        frame[25] = 0x00;
        Array.Copy(HostIp, 0, frame, 26, 4);
        Array.Copy(GuestIp, 0, frame, 30, 4);
        ushort ipChecksum = ComputeChecksum(frame, 14, 20);
        frame[24] = (byte)(ipChecksum >> 8);
        frame[25] = (byte)ipChecksum;

        // ICMP echo request
        frame[34] = 8;
        frame[35] = 0;
        frame[36] = 0x00;
        frame[37] = 0x00;
        frame[38] = (byte)(EchoId >> 8);
        frame[39] = (byte)(EchoId & 0xFF);
        frame[40] = (byte)(sequence >> 8);
        frame[41] = (byte)sequence;
        Array.Copy(payload, 0, frame, 42, payload.Length);
        ushort icmpChecksum = ComputeChecksum(frame, 34, icmpLength);
        frame[36] = (byte)(icmpChecksum >> 8);
        frame[37] = (byte)icmpChecksum;

        return frame;
    }

    /// <summary>
    /// RFC 1071 ones'-complement checksum. Returns 0 when run over a section
    /// whose stored checksum is valid.
    /// </summary>
    private static ushort ComputeChecksum(byte[] buffer, int offset, int length)
    {
        uint sum = 0;
        int i = offset;
        int end = offset + (length & ~1);

        while (i < end)
        {
            sum += (uint)((buffer[i] << 8) | buffer[i + 1]);
            i += 2;
        }

        if ((length & 1) != 0)
        {
            sum += (uint)(buffer[end] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static string FormatMac(byte[] mac) =>
        $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _qemuClient?.Dispose();
        _listener?.Stop();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}
