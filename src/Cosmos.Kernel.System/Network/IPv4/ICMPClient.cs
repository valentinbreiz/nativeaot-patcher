using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Timer;

namespace Cosmos.Kernel.System.Network.IPv4;

/// <summary>
/// Used to manage the ICMP connection to a client.
/// </summary>
public class ICMPClient : IDisposable
{
    private static readonly Dictionary<uint, ICMPClient> clients = new();

    /// <summary>
    /// The destination address.
    /// </summary>
    internal Address? destination;

    /// <summary>
    /// The RX buffer queue.
    /// </summary>
    internal Queue<ICMPPacket> rxBuffer;

    /// <summary>
    /// Gets a client by its IP address hash.
    /// </summary>
    /// <param name="iphash">The IP address hash.</param>
    /// <returns>If a client is connected to the given address, the <see cref="ICMPClient"/>; otherwise, <see langword="null"/>.</returns>
    internal static ICMPClient? GetClient(uint iphash)
    {
        if (clients.TryGetValue(iphash, out var client))
        {
            return client;
        }
        return null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ICMPClient"/> class.
    /// </summary>
    public ICMPClient()
    {
        rxBuffer = new Queue<ICMPPacket>(8);
    }

    /// <summary>
    /// Connects to the given client.
    /// </summary>
    /// <param name="dest">The destination address.</param>
    public void Connect(Address dest)
    {
        destination = dest;
        clients[dest.Hash] = this;
    }

    /// <summary>
    /// Closes the active connection.
    /// </summary>
    public void Close()
    {
        if (destination != null && clients.ContainsKey(destination.Hash))
        {
            clients.Remove(destination.Hash);
        }
    }

    /// <summary>
    /// Sends an ICMP echo request to the connected destination.
    /// </summary>
    /// <param name="id">The echo identifier.</param>
    /// <param name="sequence">The echo sequence number.</param>
    public void SendEcho(ushort id = 0x0001, ushort sequence = 0x0001)
    {
        if (destination == null)
        {
            throw new InvalidOperationException("Must establish a destination by calling Connect() before using SendEcho()");
        }

        Address source = IPConfig.FindNetwork(destination) ?? throw new InvalidOperationException("No network route to destination");
        var request = new ICMPEchoRequest(source, destination, id, sequence);
        OutgoingBuffer.AddPacket(request);
        NetworkStack.Update();
    }

    /// <summary>
    /// Receives an ICMP echo reply from the remote host.
    /// </summary>
    /// <param name="source">The source end point.</param>
    /// <param name="timeout">The timeout value in milliseconds; by default, 5000ms.</param>
    /// <returns>The elapsed time in milliseconds, or -1 if a timeout has been reached.</returns>
    public int Receive(ref EndPoint source, int timeout = 5000)
    {
        int waited = 0;
        while (rxBuffer.Count < 1 && waited < timeout)
        {
            TimerManager.Wait(10);
            waited += 10;
        }

        if (rxBuffer.Count < 1)
        {
            return -1;
        }

        var packet = new ICMPEchoReply(rxBuffer.Dequeue().RawData);
        source.Address = packet.SourceIP;

        return waited;
    }

    /// <summary>
    /// Receives data from the given packet.
    /// </summary>
    /// <param name="packet">The packet to receive.</param>
    internal void ReceiveData(ICMPPacket packet)
    {
        rxBuffer.Enqueue(packet);
    }

    public void Dispose()
    {
        Close();
    }
}
