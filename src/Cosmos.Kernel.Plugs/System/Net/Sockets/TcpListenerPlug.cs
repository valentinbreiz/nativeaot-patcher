using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Plugs.System.Net.Sockets;

[Plug(typeof(TcpListener))]
public static class TcpListenerPlug
{
    // Store listener state per instance
    public static readonly Dictionary<int, Socket> _serverSockets = new();
    public static readonly Dictionary<int, IPEndPoint> _serverSocketEPs = new();

    // Use object memory address as unique ID
    public static unsafe int GetId(TcpListener aThis) => (int)*(nint*)Unsafe.AsPointer(ref aThis);

    [PlugMember(".ctor")]
    public static void Ctor(TcpListener aThis, IPEndPoint localEP)
    {
        Serial.WriteString("[TcpListenerPlug] Ctor(localEP)\n");

        if (localEP == null)
        {
            throw new ArgumentNullException(nameof(localEP));
        }

        int id = GetId(aThis);
        _serverSocketEPs[id] = localEP;
        _serverSockets[id] = new Socket(localEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    [PlugMember(".ctor")]
    public static void Ctor(TcpListener aThis, IPAddress localaddr, int port)
    {
        Serial.WriteString("[TcpListenerPlug] Ctor(localaddr, port)\n");

        if (localaddr == null)
        {
            Serial.WriteString("[TcpListenerPlug] localaddr is null!\n");
            throw new ArgumentNullException(nameof(localaddr));
        }

        Serial.WriteString("[TcpListenerPlug] Getting ID\n");
        int id = GetId(aThis);
        Serial.WriteString("[TcpListenerPlug] ID=");
        Serial.WriteNumber(id);
        Serial.WriteString("\n");

        Serial.WriteString("[TcpListenerPlug] Creating IPEndPoint\n");
        var ep = new IPEndPoint(localaddr, port);
        Serial.WriteString("[TcpListenerPlug] IPEndPoint created\n");

        Serial.WriteString("[TcpListenerPlug] Storing endpoint\n");
        _serverSocketEPs[id] = ep;

        Serial.WriteString("[TcpListenerPlug] Creating socket\n");
        _serverSockets[id] = new Socket(ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        Serial.WriteString("[TcpListenerPlug] Socket created\n");
    }

    [PlugMember("get_Server")]
    public static Socket? get_Server(TcpListener aThis)
    {
        int id = GetId(aThis);
        return _serverSockets.TryGetValue(id, out var socket) ? socket : null;
    }

    [PlugMember("get_LocalEndpoint")]
    public static EndPoint? get_LocalEndpoint(TcpListener aThis)
    {
        int id = GetId(aThis);
        if (_serverSockets.TryGetValue(id, out var socket) && socket.LocalEndPoint != null)
        {
            return socket.LocalEndPoint;
        }
        return _serverSocketEPs.TryGetValue(id, out var ep) ? ep : null;
    }

    [PlugMember]
    public static void Start(TcpListener aThis)
    {
        Serial.WriteString("[TcpListenerPlug] Start()\n");

        int id = GetId(aThis);
        if (!_serverSockets.TryGetValue(id, out var socket) || !_serverSocketEPs.TryGetValue(id, out var ep))
        {
            throw new InvalidOperationException("TcpListener not initialized");
        }

        socket.Bind(ep);
        socket.Listen(int.MaxValue);

        Serial.WriteString("[TcpListenerPlug] Listening on port ");
        Serial.WriteNumber((ulong)ep.Port);
        Serial.WriteString("\n");
    }

    [PlugMember]
    public static void Start(TcpListener aThis, int backlog)
    {
        Serial.WriteString("[TcpListenerPlug] Start(backlog)\n");

        int id = GetId(aThis);
        if (!_serverSockets.TryGetValue(id, out var socket) || !_serverSocketEPs.TryGetValue(id, out var ep))
        {
            throw new InvalidOperationException("TcpListener not initialized");
        }

        socket.Bind(ep);
        socket.Listen(backlog);

        Serial.WriteString("[TcpListenerPlug] Listening on port ");
        Serial.WriteNumber((ulong)ep.Port);
        Serial.WriteString("\n");
    }

    [PlugMember]
    public static void Stop(TcpListener aThis)
    {
        Serial.WriteString("[TcpListenerPlug] Stop()\n");

        int id = GetId(aThis);
        if (_serverSockets.TryGetValue(id, out var socket))
        {
            socket.Close();
            _serverSockets.Remove(id);
        }
        _serverSocketEPs.Remove(id);
    }

    [PlugMember]
    public static bool Pending(TcpListener aThis)
    {
        int id = GetId(aThis);
        if (!_serverSockets.TryGetValue(id, out var socket))
        {
            throw new InvalidOperationException("TcpListener not started");
        }

        return socket.Poll(0, SelectMode.SelectRead);
    }

    [PlugMember]
    public static Socket AcceptSocket(TcpListener aThis)
    {
        Serial.WriteString("[TcpListenerPlug] AcceptSocket()\n");

        int id = GetId(aThis);
        if (!_serverSockets.TryGetValue(id, out var socket))
        {
            throw new InvalidOperationException("TcpListener not started");
        }

        return socket.Accept();
    }

    [PlugMember]
    public static TcpClient AcceptTcpClient(TcpListener aThis)
    {
        Serial.WriteString("[TcpListenerPlug] AcceptTcpClient()\n");

        int id = GetId(aThis);
        if (!_serverSockets.TryGetValue(id, out var socket))
        {
            throw new InvalidOperationException("TcpListener not started");
        }

        TcpClient client = new();
        Socket acceptedSocket = socket.Accept();
        client.Client = acceptedSocket;
        return client;
    }
}
