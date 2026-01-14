/*
* PROJECT:          Cosmos OS Development
* CONTENT:          TCP Connection
* PROGRAMMERS:      Valentin Charbonnier <valentinbreiz@gmail.com>
*                   Port of Cosmos Code.
*/

using System.Collections.Generic;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.System.Network.Config;

namespace Cosmos.Kernel.System.Network.IPv4.TCP;

/// <summary>
/// Represents a TCP connection status.
/// </summary>
public enum Status
{
    /// <summary>
    /// Wait for a connection request from any remote TCP and port.
    /// </summary>
    LISTEN,

    /// <summary>
    /// Wait for a matching connection request after having sent a connection request.
    /// </summary>
    SYN_SENT,

    /// <summary>
    /// Wait for a confirming connection request acknowledgment after having both received and sent a connection request.
    /// </summary>
    SYN_RECEIVED,

    /// <summary>
    /// Represents an open connection, data received can be delivered to the user. The normal state for the data transfer phase of the connection.
    /// </summary>
    ESTABLISHED,

    /// <summary>
    /// Wait for a connection termination request from the remote TCP, or an acknowledgment of the connection termination request previously sent.
    /// </summary>
    FIN_WAIT1,

    /// <summary>
    /// Wait for a connection termination request from the remote TCP.
    /// </summary>
    FIN_WAIT2,

    /// <summary>
    /// Wait for a connection termination request from the local user.
    /// </summary>
    CLOSE_WAIT,

    /// <summary>
    /// Wait for a connection termination request acknowledgment from the remote TCP.
    /// </summary>
    CLOSING,

    /// <summary>
    /// Wait for an acknowledgment of the connection termination request previously sent to the remote TCP (which includes an acknowledgment of its connection termination request).
    /// </summary>
    LAST_ACK,

    /// <summary>
    /// Wait for enough time to pass to be sure the remote TCP received the acknowledgment of its connection termination request.
    /// </summary>
    TIME_WAIT,

    /// <summary>
    /// Represents no connection state.
    /// </summary>
    CLOSED
}

/// <summary>
/// Represents a Transmission Control Block (TCB).
/// </summary>
public class TransmissionControlBlock
{
    /** Send Sequence Variables **/

    /// <summary>
    /// Send unacknowledged.
    /// </summary>
    public uint SndUna { get; set; }

    /// <summary>
    /// Send next.
    /// </summary>
    public uint SndNxt { get; set; }

    /// <summary>
    /// Send window.
    /// </summary>
    public ushort SndWnd { get; set; }

    /// <summary>
    /// Send urgent pointer.
    /// </summary>
    public uint SndUp { get; set; }

    /// <summary>
    /// Segment sequence number used for last window update.
    /// </summary>
    public uint SndWl1 { get; set; }

    /// <summary>
    /// Segment acknowledgment number used for last window update.
    /// </summary>
    public uint SndWl2 { get; set; }

    /// <summary>
    /// Initial send sequence number
    /// </summary>
    public uint ISS { get; set; }

    /** Receive Sequence Variables **/

    /// <summary>
    /// Receive next.
    /// </summary>
    public uint RcvNxt { get; set; }

    /// <summary>
    /// Receive window.
    /// </summary>
    public uint RcvWnd { get; set; }

    /// <summary>
    /// Receive urgent pointer.
    /// </summary>
    public uint RcvUp { get; set; }

    /// <summary>
    /// Initial receive sequence number.
    /// </summary>
    public uint IRS { get; set; }
}

/// <summary>
/// Used to manage the TCP state machine.
/// Handle received packets according to current TCP connection Status. Also contains TCB (Transmission Control Block) information.
/// </summary>
/// <remarks>
/// See <a href="https://datatracker.ietf.org/doc/html/rfc793">RFC 793</a> for more information.
/// </remarks>
public class Tcp
{
    public static ushort DynamicPortStart = 49152;

    private static ushort nextPort = 49152;

    /// <summary>
    /// Gets a dynamic port (simple incrementing approach for AOT compatibility).
    /// </summary>
    public static ushort GetDynamicPort(int tries = 10)
    {
        for (int i = 0; i < tries; i++)
        {
            ushort port = nextPort++;
            if (nextPort >= 65535)
            {
                nextPort = DynamicPortStart;
            }

            bool portInUse = false;
            foreach (var connection in Connections)
            {
                if (connection.LocalEndPoint.Port == port)
                {
                    portInUse = true;
                    break;
                }
            }

            if (!portInUse)
            {
                return port;
            }
        }

        return 0;
    }

    /// <summary>
    /// The TCP window size.
    /// </summary>
    public const ushort TcpWindowSize = 8192;

    // Simple sequence number generator
    private static uint sequenceCounter = 1000;

    #region Static
    /// <summary>
    /// A list of currently active connections.
    /// </summary>
    public static List<Tcp> Connections = new();

    /// <summary>
    /// String / enum correspondance (used for debugging)
    /// </summary>
    public static readonly string[] Table = new string[11]
    {
        "LISTEN",
        "SYN_SENT",
        "SYN_RECEIVED",
        "ESTABLISHED",
        "FIN_WAIT1",
        "FIN_WAIT2",
        "CLOSE_WAIT",
        "CLOSING",
        "LAST_ACK",
        "TIME_WAIT",
        "CLOSED"
    };

    /// <summary>
    /// Gets a TCP connection object that matches the specified local and remote ports and addresses.
    /// </summary>
    internal static Tcp? GetConnection(ushort localPort, ushort remotePort, Address localIp, Address remoteIp)
    {
        foreach (var con in Connections)
        {
            if (con.Equals(localPort, remotePort, localIp, remoteIp))
            {
                return con;
            }
            else if (con.LocalEndPoint.Port.Equals(localPort) && con.Status == Status.LISTEN)
            {
                return con;
            }
        }
        return null;
    }

    /// <summary>
    /// Removes a TCP connection object that matches the specified local and remote ports and addresses.
    /// </summary>
    public static void RemoveConnection(ushort localPort, ushort remotePort, Address localIp, Address remoteIp)
    {
        for (int i = 0; i < Connections.Count; i++)
        {
            if (Connections[i].Equals(localPort, remotePort, localIp, remoteIp))
            {
                Connections.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Removes a TCP connection object by reference.
    /// </summary>
    public static void RemoveConnection(Tcp connection)
    {
        for (int i = 0; i < Connections.Count; i++)
        {
            if (object.ReferenceEquals(Connections[i], connection))
            {
                Connections.RemoveAt(i);
                return;
            }
        }
    }

    #endregion

    #region TCB

    /// <summary>
    /// The local end-point.
    /// </summary>
    public EndPoint LocalEndPoint;

    /// <summary>
    /// The remote end-point.
    /// </summary>
    public EndPoint RemoteEndPoint;

    /// <summary>
    /// The connection Transmission Control Block.
    /// </summary>
    public TransmissionControlBlock TCB { get; set; }

    #endregion

    /// <summary>
    /// The connection status.
    /// </summary>
    public Status Status;

    /// <summary>
    /// The received data buffer.
    /// </summary>
    public byte[] Data { get; set; }

    public Tcp(ushort localPort, ushort remotePort, Address localIp, Address remoteIp)
    {
        LocalEndPoint = new EndPoint(localIp, localPort);
        RemoteEndPoint = new EndPoint(remoteIp, remotePort);
        TCB = new TransmissionControlBlock();
    }

    /// <summary>
    /// Handles incoming TCP packets according to the current connection status.
    /// </summary>
    internal void ReceiveData(TCPPacket packet)
    {
        Serial.WriteString("[" + Table[(int)Status] + "] " + packet.ToString() + "\n");

        if (Status == Status.LISTEN)
        {
            ProcessListen(packet);
        }
        else if (Status == Status.SYN_SENT)
        {
            ProcessSynSent(packet);
        }
        else if (Status == Status.CLOSED)
        {
            // Connection is closed - just ignore all packets
            // Don't send RST as the connection state may be invalid
            Serial.WriteString("[TCP] Packet received on CLOSED connection, ignoring\n");
            return;
        }
        else if (Status == Status.TIME_WAIT)
        {
            // In TIME_WAIT, just ignore packets (we've already sent final ACK)
            Serial.WriteString("[TCP] Packet received in TIME_WAIT, ignoring\n");
        }
        else
        {
            // Check sequence number and segment data.
            if (TCB.RcvNxt <= packet.SequenceNumber && packet.SequenceNumber + packet.TCP_DataLength < TCB.RcvNxt + TCB.RcvWnd)
            {
                switch (Status)
                {
                    case Status.SYN_RECEIVED:
                        ProcessSynReceived(packet);
                        break;
                    case Status.ESTABLISHED:
                        ProcessEstablished(packet);
                        break;
                    case Status.FIN_WAIT1:
                        ProcessFinWait1(packet);
                        break;
                    case Status.FIN_WAIT2:
                        ProcessFinWait2(packet);
                        break;
                    case Status.CLOSE_WAIT:
                        ProcessCloseWait(packet);
                        break;
                    case Status.CLOSING:
                        ProcessClosing(packet);
                        break;
                    case Status.LAST_ACK:
                        ProcessCloseWait(packet);
                        break;
                    default:
                        Serial.WriteString("[TCP] Unknown TCP connection state = " + (int)Status + "\n");
                        break;
                }
            }
            else
            {
                if (!packet.RST)
                {
                    SendEmptyPacket(Flags.ACK);
                }

                Serial.WriteString("[TCP] Sequence number or segment data invalid, packet passed.\n");
            }
        }
    }

    #region Process Status

    /// <summary>
    /// Processes a TCP LISTEN state packet and updates the connection status accordingly.
    /// </summary>
    public void ProcessListen(TCPPacket packet)
    {
        if (packet.RST)
        {
            Serial.WriteString("[TCP] RST received at LISTEN state, packet passed.\n");
            return;
        }
        else if (packet.FIN)
        {
            Serial.WriteString("[TCP] Connection closed! (FIN received on LISTEN state)\n");
        }
        else if (packet.ACK)
        {
            TCB.RcvNxt = packet.SequenceNumber;
            TCB.SndNxt = packet.AckNumber;

            Status = Status.ESTABLISHED;
        }
        else if (packet.SYN)
        {
            LocalEndPoint.Address = IPConfig.FindNetwork(packet.SourceIP);
            RemoteEndPoint.Address = packet.SourceIP;
            RemoteEndPoint.Port = packet.SourcePort;

            // Simple sequence number generation
            uint sequenceNumber = sequenceCounter++;

            //Fill TCB
            TCB.SndUna = sequenceNumber;
            TCB.SndNxt = sequenceNumber;
            TCB.SndWnd = Tcp.TcpWindowSize;
            TCB.SndUp = 0;
            TCB.SndWl1 = packet.SequenceNumber - 1;
            TCB.SndWl2 = 0;
            TCB.ISS = sequenceNumber;

            TCB.RcvNxt = packet.SequenceNumber + 1;
            TCB.RcvWnd = Tcp.TcpWindowSize;
            TCB.RcvUp = 0;
            TCB.IRS = packet.SequenceNumber;

            SendEmptyPacket(Flags.SYN | Flags.ACK);

            Status = Status.SYN_RECEIVED;
        }
    }

    /// <summary>
    /// Processes a TCP SYN_RECEIVED state packet and updates the connection status accordingly.
    /// </summary>
    public void ProcessSynReceived(TCPPacket packet)
    {
        if (packet.ACK)
        {
            if (TCB.SndUna <= packet.AckNumber && packet.AckNumber <= TCB.SndNxt)
            {
                TCB.SndWnd = packet.WindowSize;
                TCB.SndWl1 = packet.SequenceNumber;
                TCB.SndWl2 = packet.SequenceNumber;

                Status = Status.ESTABLISHED;
            }
            else
            {
                SendEmptyPacket(Flags.RST, packet.AckNumber);
            }
        }
    }

    /// <summary>
    /// Processes a SYN_SENT state TCP packet and updates the connection state accordingly.
    /// </summary>
    public void ProcessSynSent(TCPPacket packet)
    {
        if (packet.SYN)
        {
            TCB.IRS = packet.SequenceNumber;
            TCB.RcvNxt = packet.SequenceNumber + 1;

            if (packet.ACK)
            {
                TCB.SndUna = packet.AckNumber;
                TCB.SndWnd = packet.WindowSize;
                TCB.SndWl1 = packet.SequenceNumber;
                TCB.SndWl2 = packet.AckNumber;

                SendEmptyPacket(Flags.ACK);

                Status = Status.ESTABLISHED;
            }
            else if (packet.TCPFlags == (byte)Flags.SYN)
            {
                Status = Status.CLOSED;
                Serial.WriteString("[TCP] Simultaneous open not supported.\n");
            }
            else
            {
                Status = Status.CLOSED;
                Serial.WriteString("[TCP] Connection closed! (" + packet.GetFlags() + " received on SYN_SENT state)\n");
            }
        }
        else if (packet.ACK)
        {
            //Check for bad ACK packet
            if (packet.AckNumber - TCB.ISS < 0 || packet.AckNumber - TCB.SndNxt > 0)
            {
                SendEmptyPacket(Flags.RST, packet.AckNumber);
                Serial.WriteString("[TCP] Bad ACK received at SYN_SENT.\n");
            }
            else
            {
                TCB.RcvNxt = packet.SequenceNumber;
                TCB.SndNxt = packet.AckNumber;

                Status = Status.ESTABLISHED;
            }
        }
        else if (packet.FIN)
        {
            Status = Status.CLOSED;
            Serial.WriteString("[TCP] Connection closed! (FIN received on SYN_SENT state).\n");
        }
        else if (packet.RST)
        {
            Status = Status.CLOSED;
            Serial.WriteString("[TCP] Connection refused by remote computer.\n");
        }
    }

    /// <summary>
    /// Processes a ESTABLISHED state TCP packet.
    /// </summary>
    public void ProcessEstablished(TCPPacket packet)
    {
        if (packet.ACK)
        {
            if (TCB.SndUna < packet.AckNumber && packet.AckNumber <= TCB.SndNxt)
            {
                TCB.SndUna = packet.AckNumber;

                //Update Window Size
                if (TCB.SndWl1 < packet.SequenceNumber || (TCB.SndWl1 == packet.SequenceNumber && TCB.SndWl2 <= packet.AckNumber))
                {
                    TCB.SndWnd = packet.WindowSize;
                    TCB.SndWl1 = packet.SequenceNumber;
                    TCB.SndWl2 = packet.AckNumber;
                }
            }

            // Check for duplicate packet
            if (packet.AckNumber < TCB.SndUna)
            {
                Serial.WriteString("[TCP] Duplicate ACK, ignoring\n");
                return;
            }

            // Something not yet sent
            if (packet.AckNumber > TCB.SndNxt)
            {
                Serial.WriteString("[TCP] ACK for unsent data, sending ACK\n");
                SendEmptyPacket(Flags.ACK);
                return;
            }

            if (packet.PSH)
            {
                Serial.WriteString("[TCP] PSH received, data length: ");
                Serial.WriteNumber((ulong)packet.TCP_DataLength);
                Serial.WriteString(", storing data\n");

                TCB.RcvNxt += packet.TCP_DataLength;

                Data = ArrayConcat(Data, packet.TCP_Data);

                Serial.WriteString("[TCP] Data buffer now has ");
                Serial.WriteNumber((ulong)(Data?.Length ?? 0));
                Serial.WriteString(" bytes\n");

                // Handle FIN flag within PSH handling if both are set
                if (packet.FIN)
                {
                    Serial.WriteString("[TCP] PSH+FIN received, closing\n");
                    TCB.RcvNxt++;

                    SendEmptyPacket(Flags.ACK);

                    Status = Status.CLOSE_WAIT;

                    SimpleWait(300);

                    SendEmptyPacket(Flags.FIN);

                    Status = Status.LAST_ACK;
                }
                else
                {
                    SendEmptyPacket(Flags.ACK);
                }
                return;
            }
            else if (packet.FIN)
            {
                Serial.WriteString("[TCP] FIN received, closing connection\n");
                TCB.RcvNxt++;

                SendEmptyPacket(Flags.ACK);

                WaitAndClose();

                return;
            }

            if (packet.TCP_DataLength > 0 && packet.SequenceNumber >= TCB.RcvNxt) //packet sequencing
            {
                TCB.RcvNxt += packet.TCP_DataLength;

                Data = ArrayConcat(Data, packet.TCP_Data);
            }
        }
        if (packet.RST)
        {
            Status = Status.CLOSED;

            Serial.WriteString("[TCP] Connection resetted!\n");
        }
        else if (packet.FIN)
        {
            TCB.RcvNxt++;

            SendEmptyPacket(Flags.ACK);

            Status = Status.CLOSE_WAIT;

            SimpleWait(300);

            SendEmptyPacket(Flags.FIN);

            Status = Status.LAST_ACK;
        }
    }

    /// <summary>
    /// Process FIN_WAIT1 Status.
    /// </summary>
    public void ProcessFinWait1(TCPPacket packet)
    {
        if (packet.ACK)
        {
            if (packet.FIN)
            {
                TCB.RcvNxt++;

                SendEmptyPacket(Flags.ACK);

                WaitAndClose();
            }
            else
            {
                Status = Status.FIN_WAIT2;
            }
        }
        else if (packet.FIN)
        {
            TCB.RcvNxt++;

            SendEmptyPacket(Flags.ACK);

            Status = Status.CLOSING;
        }
    }

    /// <summary>
    /// Process FIN_WAIT2 Status.
    /// </summary>
    public void ProcessFinWait2(TCPPacket packet)
    {
        if (packet.FIN)
        {
            TCB.RcvNxt++;

            SendEmptyPacket(Flags.ACK);

            WaitAndClose();
        }
    }

    /// <summary>
    /// Process CLOSING Status.
    /// </summary>
    public void ProcessClosing(TCPPacket packet)
    {
        if (packet.ACK)
        {
            WaitAndClose();
        }
    }

    /// <summary>
    /// Process Close_WAIT Status.
    /// </summary>
    public void ProcessCloseWait(TCPPacket packet)
    {
        if (packet.ACK)
        {
            Status = Status.CLOSED;
        }
    }

    #endregion

    #region Utils

    /// <summary>
    /// Simple busy wait (iteration-based for bare metal).
    /// </summary>
    private void SimpleWait(int iterations)
    {
        for (int i = 0; i < iterations * 10000; i++)
        {
            // Busy wait
        }
    }

    /// <summary>
    /// Waits until remote receives an ACKnowledge of its connection termination request.
    /// </summary>
    private void WaitAndClose()
    {
        Serial.WriteString("[TCP] WaitAndClose: entering TIME_WAIT\n");
        Status = Status.TIME_WAIT;

        SimpleWait(300);

        Serial.WriteString("[TCP] WaitAndClose: entering CLOSED\n");
        Status = Status.CLOSED;
    }

    /// <summary>
    /// Waits for a new TCP connection status (with timeout in milliseconds).
    /// </summary>
    public bool WaitStatus(Status status, int timeout)
    {
        int waited = 0;
        while (Status != status && waited < timeout)
        {
            Timer.TimerManager.Wait(10);
            waited += 10;
        }
        return Status == status;
    }

    /// <summary>
    /// Waits for a new TCP connection status (blocking).
    /// </summary>
    public bool WaitStatus(Status status)
    {
        while (Status != status)
        {
            Timer.TimerManager.Wait(10);
        }
        return true;
    }

    /// <summary>
    /// Sends an empty packet.
    /// </summary>
    public void SendEmptyPacket(Flags flag)
    {
        SendPacket(new TCPPacket(LocalEndPoint.Address, RemoteEndPoint.Address, LocalEndPoint.Port, RemoteEndPoint.Port, TCB.SndNxt, TCB.RcvNxt, 20, (byte)flag, TCB.SndWnd, 0));
    }

    /// <summary>
    /// Sends an empty packet.
    /// </summary>
    internal void SendEmptyPacket(Flags flag, uint sequenceNumber)
    {
        SendPacket(new TCPPacket(LocalEndPoint.Address, RemoteEndPoint.Address, LocalEndPoint.Port, RemoteEndPoint.Port, sequenceNumber, TCB.RcvNxt, 20, (byte)flag, TCB.SndWnd, 0));
    }

    /// <summary>
    /// Sends a TCP packet.
    /// </summary>
    private void SendPacket(TCPPacket packet)
    {
        OutgoingBuffer.AddPacket(packet);

        // Increment SndNxt BEFORE NetworkStack.Update() so that incoming packets
        // processed during Update() see the correct value
        if (packet.SYN || packet.FIN)
        {
            TCB.SndNxt++;
        }

        NetworkStack.Update();
    }

    /// <summary>
    /// Concatenates two byte arrays.
    /// </summary>
    private static byte[] ArrayConcat(byte[] first, byte[] second)
    {
        if (first == null)
        {
            return second;
        }
        if (second == null)
        {
            return first;
        }

        byte[] result = new byte[first.Length + second.Length];
        for (int i = 0; i < first.Length; i++)
        {
            result[i] = first[i];
        }
        for (int i = 0; i < second.Length; i++)
        {
            result[first.Length + i] = second[i];
        }
        return result;
    }

    internal bool Equals(ushort localPort, ushort remotePort, Address localIp, Address remoteIp)
    {
        return LocalEndPoint.Port.Equals(localPort) && RemoteEndPoint.Port.Equals(remotePort) && LocalEndPoint.Address.Hash.Equals(localIp.Hash) && RemoteEndPoint.Address.Hash.Equals(remoteIp.Hash);
    }

    #endregion
}
