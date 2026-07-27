using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chess.Net;
using LAN.Lib;

namespace Chess.Tests.Lan;

/// <summary>
/// An in-memory stand-in for the whole LAN so <see cref="LanLobby"/> can be tested with no real
/// sockets (CI-safe). Nodes registered on this bus share it in both directions: a UDP broadcast
/// reaches every node's discovery listener (including the sender's, as real UDP does), and a TCP
/// "connect" is matched to the target node by its listen port and wired up as a synchronous in-memory
/// duplex pair.
///
/// <para>A node is the same pair of transports the real thing uses — LAN.Lib's
/// <see cref="ILanTransport"/> for discovery, chess's <see cref="ISessionTransport"/> for the session
/// channel — so the lobby under test is wired exactly as <see cref="LanPlayStack"/> wires it.</para>
/// </summary>
internal sealed class FakeLanBus
{
    private readonly List<FakeLanNode> _nodes = new();

    /// <summary>Create a node bound to this bus at the given (fake) address + TCP listen port.</summary>
    public FakeLanNode CreateNode(string address, int listenPort)
    {
        var node = new FakeLanNode(this, IPAddress.Parse(address), listenPort);
        _nodes.Add(node);
        return node;
    }

    public void Broadcast(FakeLanNode from, string text)
    {
        // Real UDP echoes a broadcast back to the sender too; LanDiscovery ignores its own peerId.
        foreach (var node in _nodes.ToArray())
        {
            node.Discovery.Deliver(new DiscoveryDatagram(text, from.Address));
        }
    }

    public FakeLanNode? FindByPort(int port) => _nodes.FirstOrDefault(n => n.Session.ListenPort == port);
}

/// <summary>One host on the fake LAN: its discovery transport and its session transport.</summary>
internal sealed class FakeLanNode
{
    public FakeLanNode(FakeLanBus bus, IPAddress address, int listenPort)
    {
        Address = address;
        Discovery = new FakeDiscoveryTransport(bus, this);
        Session = new FakeSessionTransport(bus, this, listenPort);
    }

    public IPAddress Address { get; }
    public FakeDiscoveryTransport Discovery { get; }
    public FakeSessionTransport Session { get; }
}

/// <summary>The UDP half: broadcasts land on every node of the bus, synchronously.</summary>
internal sealed class FakeDiscoveryTransport(FakeLanBus bus, FakeLanNode node) : ILanTransport
{
    /// <summary>Every datagram this node has broadcast (for asserting beacon content).</summary>
    public List<string> Broadcasts { get; } = new();

    public event Action<DiscoveryDatagram>? DatagramReceived;

    public Task BroadcastAsync(string text, CancellationToken cancellationToken = default)
    {
        Broadcasts.Add(text);
        bus.Broadcast(node, text);
        return Task.CompletedTask;
    }

    public void Deliver(DiscoveryDatagram dg) => DatagramReceived?.Invoke(dg);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>The TCP half: a "connect" finds the target node by port and hands both ends a paired
/// in-memory connection.</summary>
internal sealed class FakeSessionTransport(FakeLanBus bus, FakeLanNode node, int listenPort) : ISessionTransport
{
    public int ListenPort { get; } = listenPort;

    public event Action<ILanConnection>? ConnectionAccepted;

    public Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default)
    {
        var target = bus.FindByPort(endPoint.Port)
            ?? throw new SocketException((int)SocketError.ConnectionRefused);

        var (mine, theirs) = FakeLanConnection.CreatePair();
        mine.RemoteEndPoint = endPoint;
        theirs.RemoteEndPoint = new IPEndPoint(node.Address, ListenPort);
        target.Session.ConnectionAccepted?.Invoke(theirs);
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

    // Lines that arrived before StartReceiving, mirroring the real socket's contract: a peer writes
    // the instant it connects, so anything sent before the owner wires its handlers has to wait
    // rather than vanish. A consumer that forgets StartReceiving therefore sees NOTHING — which is
    // what makes the lobby tests a guard for the bug that lost the very first INVITE on a real LAN.
    private readonly List<string> _beforeReceiving = new();
    private bool _receiving;

    public event Action<string>? LineReceived;
    public event Action? Closed;

    public void StartReceiving()
    {
        if (_receiving) return;
        _receiving = true;
        var queued = _beforeReceiving.ToArray();
        _beforeReceiving.Clear();
        foreach (var line in queued)
            LineReceived?.Invoke(line);
    }

    public void Send(string line)
    {
        Sent.Add(line);
        _peer?.Deliver(line);
    }

    private void Deliver(string line)
    {
        if (_receiving) LineReceived?.Invoke(line);
        else _beforeReceiving.Add(line);
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
