using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Network.Config;

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
    internal Queue<UDPPacket> rxBuffer;

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
        rxBuffer = new Queue<UDPPacket>(8);

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

        var packet = new UDPPacket(source, dest, (ushort)_localPort, (ushort)destPort, data);
        Serial.WriteString("[UdpClient] UDPPacket created, adding to outgoing buffer\n");
        OutgoingBuffer.AddPacket(packet);
        Serial.WriteString("[UdpClient] Packet added to outgoing buffer\n");
    }

    /// <summary>
    /// Receives data from the given end-point (non-blocking).
    /// </summary>
    /// <param name="source">The source end point.</param>
    public byte[]? NonBlockingReceive(ref EndPoint source)
    {
        if (rxBuffer.Count < 1)
        {
            return null;
        }

        var packet = new UDPPacket(rxBuffer.Dequeue().RawData);
        source.Address = packet.SourceIP;
        source.Port = packet.SourcePort;

        return packet.UDPData;
    }

    /// <summary>
    /// Receives data from the given end-point (blocking).
    /// </summary>
    /// <param name="source">The source end point.</param>
    public byte[] Receive(ref EndPoint source)
    {
        while (rxBuffer.Count < 1)
        {
            ;
        }

        var packet = new UDPPacket(rxBuffer.Dequeue().RawData);
        source.Address = packet.SourceIP;
        source.Port = packet.SourcePort;

        return packet.UDPData;
    }

    /// <summary>
    /// Receives data from the given packet.
    /// </summary>
    /// <param name="packet">Packet to receive.</param>
    internal void ReceiveData(UDPPacket packet)
    {
        rxBuffer.Enqueue(packet);
    }

    public void Dispose()
    {
        Close();
    }
}
