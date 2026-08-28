using System.Diagnostics.CodeAnalysis;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Timer;

namespace Cosmos.Kernel.System.Network.IPv4.UDP;

/// <summary>
/// Used to manage the UDP connection to a client.
/// </summary>
public class UdpClient : IDisposable
{
    private const ushort DynamicPortStart = 49152;

    private static ushort s_nextPort = 49152;

    /// <summary>
    /// Gets a dynamic port (simple incrementing approach for AOT compatibility).
    /// </summary>
    /// <param name="tries"></param>
    /// <returns></returns>
    public static ushort GetDynamicPort(int tries = 10)
    {
        for (int i = 0; i < tries; i++)
        {
            ushort port = s_nextPort++;
            if (s_nextPort >= 65535)
            {
                s_nextPort = DynamicPortStart;
            }
            if (!s_clients.ContainsKey(port))
            {
                return port;
            }
        }

        return 0;
    }

    private static readonly Dictionary<uint, UdpClient> s_clients = new();
    private readonly int _localPort;
    private int _destinationPort;

    /// <summary>
    /// The _destination address.
    /// </summary>
    internal Address? _destination;

    /// <summary>
    /// The RX buffer queue.
    /// </summary>
    internal Queue<UdpPacket> _rxBuffer;

    /// <summary>
    /// Gets a UDP client running on the given port.
    /// </summary>
    /// <param name="destPort">The _destination port.</param>
    /// <returns>If a client is running on the given port, the <see cref="UdpClient"/>; otherwise, <see langword="null"/>.</returns>
    internal static UdpClient? GetClient(ushort destPort)
    {
        if (s_clients.TryGetValue(destPort, out var client))
        {
            return client;
        }
        return null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpClient"/> class.
    /// </summary>
    public UdpClient()
        : this(0)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpClient"/> class.
    /// </summary>
    /// <param name="_localPort">Local port.</param>
    public UdpClient(int _localPort)
    {
        _rxBuffer = new Queue<UdpPacket>(8);

        this._localPort = _localPort;
        if (_localPort > 0)
        {
            s_clients[(uint)_localPort] = this;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpClient"/> class.
    /// </summary>
    /// <param name="dest">Destination address.</param>
    /// <param name="destPort">Destination port.</param>
    public UdpClient(Address dest, int destPort)
        : this(0)
    {
        _destination = dest;
        _destinationPort = destPort;
    }

    /// <summary>
    /// Connects to the given client.
    /// </summary>
    /// <param name="dest">The _destination address.</param>
    /// <param name="destPort">The _destination port.</param>
    public void Connect(Address dest, int destPort)
    {
        _destination = dest;
        _destinationPort = destPort;
    }

    /// <summary>
    /// Closes the active connection.
    /// </summary>
    public void Close()
    {
        if (s_clients.ContainsKey((uint)_localPort))
        {
            s_clients.Remove((uint)_localPort);
        }
    }

    /// <summary>
    /// Sends data to the client.
    /// </summary>
    /// <param name="data">The data to send.</param>
    public void Send(byte[] data)
    {
        if (_destination == null || _destinationPort == 0)
        {
            throw new InvalidOperationException("Must establish a default remote host by calling Connect() before using this Send() overload");
        }

        Send(data, _destination, _destinationPort);
        NetworkStack.Update();
    }

    /// <summary>
    /// Sends data to a remote host.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="dest">The _destination address.</param>
    /// <param name="destPort">The _destination port.</param>
    public void Send(byte[] data, Address dest, int destPort)
    {
        Serial.WriteString("[UdpClient] Send to ");
        Serial.WriteString(dest.ToString());
        Serial.WriteString(":");
        Serial.WriteNumber((ulong)destPort);
        Serial.WriteString(" _localPort=");
        Serial.WriteNumber((ulong)_localPort);
        Serial.WriteString("\n");

        Address? source = IPConfig.FindNetwork(dest);
        if (source == null)
        {
            Serial.WriteString("[UdpClient] ERROR: IPConfig.FindNetwork returned null!\n");
            throw new InvalidOperationException("No network route to _destination");
        }

        Serial.WriteString("[UdpClient] Source IP: ");
        Serial.WriteString(source.ToString());
        Serial.WriteString("\n");

        var packet = new UdpPacket(source, dest, (ushort)_localPort, (ushort)destPort, data);
        Serial.WriteString("[UdpClient] UdpPacket created, adding to outgoing buffer\n");
        OutgoingBuffer.AddPacket(packet);
        Serial.WriteString("[UdpClient] Packet added to outgoing buffer\n");
    }

    /// <summary>
    /// Transmits a prebuilt UDP packet through the stack's outgoing queue, including ARP
    /// resolution of the destination MAC address when it is not already known.
    /// </summary>
    /// <param name="packet">The packet to transmit.</param>
    /// <returns><see langword="true"/> when the packet was queued for transmission;
    /// <see langword="false"/> when no configured network interface matches the packet's
    /// source address.</returns>
    [Experimental(Experimentals.PacketSeamDiagId)]
    public bool Send(UdpPacket packet) => Cosmos.Kernel.System.Network.NetworkStack.Send(packet);

    /// <summary>
    /// Receives a datagram, waiting up to <paramref name="timeoutMs"/> for one
    /// to arrive.
    /// </summary>
    /// <param name="source">Carries the sender's end point back when a datagram arrives.</param>
    /// <param name="timeoutMs">How long to wait, in milliseconds; 0 polls and returns at once.</param>
    /// <returns>The datagram payload, or <see langword="null"/> when none arrived in time.</returns>
    public byte[]? Receive(ref EndPoint source, int timeoutMs = 5000)
    {
        int waited = 0;
        while (_rxBuffer.Count < 1 && waited < timeoutMs)
        {
            TimerManager.Wait(10);
            waited += 10;
        }

        if (_rxBuffer.Count < 1)
        {
            return null;
        }

        UdpPacket packet = new(_rxBuffer.Dequeue().RawData);
        source.Address = packet.SourceIP;
        source.Port = packet.SourcePort;

        return packet.UDPData;
    }

    /// <summary>
    /// Waits for a datagram to arrive on this client's local port and returns the parsed
    /// <see cref="UdpPacket"/> itself, without re-parsing. Unlike
    /// <see cref="Receive"/>, which returns the payload
    /// bytes alone, the returned packet exposes the ports, the source and destination
    /// addresses, and the payload. Polls the receive buffer in 10 millisecond slices.
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds; a non-positive value
    /// checks the receive buffer once without waiting.</param>
    /// <returns>The dequeued packet, or <see langword="null"/> when the timeout elapses with
    /// no datagram queued.</returns>
    [Experimental(Experimentals.PacketSeamDiagId)]
    public UdpPacket? ReceivePacket(int timeoutMs = 5000)
    {
        int waited = 0;
        while (_rxBuffer.Count < 1 && waited < timeoutMs)
        {
            TimerManager.Wait(10);
            waited += 10;
        }

        if (_rxBuffer.Count < 1)
        {
            return null;
        }

        return _rxBuffer.Dequeue();
    }

    /// <summary>
    /// Receives data from the given packet.
    /// </summary>
    /// <param name="packet">Packet to receive.</param>
    internal void ReceiveData(UdpPacket packet)
    {
        _rxBuffer.Enqueue(packet);
    }

    /// <summary>
    /// Closes the client, like <see cref="Close"/>.
    /// </summary>
    public void Dispose()
    {
        Close();
    }
}
