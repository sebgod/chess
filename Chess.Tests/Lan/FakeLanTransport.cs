using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chess.Net;

namespace Chess.Tests.Lan;

/// <summary>
/// An in-memory stand-in for the whole LAN so <see cref="LanDiscovery"/>/<see cref="LanLobby"/> can be
/// tested with no real sockets (CI-safe). Registered transports share this bus: a broadcast reaches
/// every node's discovery listener, and a TCP "connect" is matched to the target node by its listen
/// port and wired up as a synchronous in-memory duplex pair.
/// </summary>
internal sealed class FakeLanBus
{
    private readonly List<FakeLanTransport> _nodes = new();

    /// <summary>Create a transport bound to this bus at the given (fake) address + listen port.</summary>
    public FakeLanTransport CreateNode(string address, int listenPort)
    {
        var node = new FakeLanTransport(this, IPAddress.Parse(address), listenPort);
        _nodes.Add(node);
        return node;
    }

    public void Broadcast(FakeLanTransport from, string text)
    {
        // Real UDP echoes a broadcast back to the sender too; LanDiscovery ignores its own peerId.
        foreach (var node in _nodes.ToArray())
        {
            node.DeliverDatagram(new DiscoveryDatagram(text, from.Address));
        }
    }

    public FakeLanTransport? FindByPort(int port) => _nodes.FirstOrDefault(n => n.ListenPort == port);
}

internal sealed class FakeLanTransport(FakeLanBus bus, IPAddress address, int listenPort) : ILanTransport
{
    public IPAddress Address { get; } = address;
    public int ListenPort { get; } = listenPort;

    /// <summary>Every datagram this node has broadcast (for asserting beacon content).</summary>
    public List<string> Broadcasts { get; } = new();

    public event Action<DiscoveryDatagram>? DatagramReceived;
    public event Action<ILanConnection>? ConnectionAccepted;

    public void Broadcast(string text)
    {
        Broadcasts.Add(text);
        bus.Broadcast(this, text);
    }

    public void DeliverDatagram(DiscoveryDatagram dg) => DatagramReceived?.Invoke(dg);

    public Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default)
    {
        var target = bus.FindByPort(endPoint.Port)
            ?? throw new SocketException((int)SocketError.ConnectionRefused);

        var (mine, theirs) = FakeLanConnection.CreatePair();
        mine.RemoteEndPoint = endPoint;
        theirs.RemoteEndPoint = new IPEndPoint(Address, ListenPort);
        target.ConnectionAccepted?.Invoke(theirs);
        return Task.FromResult<ILanConnection>(mine);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A synchronous in-memory duplex connection: what one end sends is delivered to the other
/// immediately (deterministic ordering, no threads).</summary>
internal sealed class FakeLanConnection : ILanConnection
{
    public IPEndPoint RemoteEndPoint { get; set; } = new(IPAddress.Loopback, 0);
    public bool IsConnected { get; private set; } = true;

    /// <summary>Everything sent from this end (for assertions).</summary>
    public List<string> Sent { get; } = new();

    private FakeLanConnection? _peer;

    public event Action<string>? LineReceived;
    public event Action? Closed;

    public void Send(string line)
    {
        Sent.Add(line);
        _peer?.LineReceived?.Invoke(line);
    }

    public void Dispose()
    {
        if (!IsConnected) return;
        IsConnected = false;
        Closed?.Invoke();
        _peer?.OnPeerClosed();
    }

    private void OnPeerClosed()
    {
        if (!IsConnected) return;
        IsConnected = false;
        Closed?.Invoke();
    }

    public static (FakeLanConnection A, FakeLanConnection B) CreatePair()
    {
        var a = new FakeLanConnection();
        var b = new FakeLanConnection();
        a._peer = b;
        b._peer = a;
        return (a, b);
    }
}
