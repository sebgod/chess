using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net;

/// <summary>A raw discovery datagram plus the address it came from (the sender's IP becomes the host
/// half of the peer's TCP endpoint — the announce only carries the port).</summary>
public readonly record struct DiscoveryDatagram(string Text, IPAddress SenderAddress);

/// <summary>
/// Abstracts the two sockets LAN play needs — UDP broadcast for discovery and a TCP listener/dialer
/// for the game session — behind an interface so the discovery/lobby logic is unit-testable against
/// an in-memory fake with no real network. <see cref="UdpTcpLanTransport"/> is the real backend.
/// </summary>
public interface ILanTransport : IAsyncDisposable
{
    /// <summary>The TCP port our session listener is bound to — announced in the beacon so peers
    /// know where to dial.</summary>
    int ListenPort { get; }

    /// <summary>Broadcast a discovery datagram to the whole subnet.</summary>
    void Broadcast(string text);

    /// <summary>Raised for every discovery datagram received (on a background thread).</summary>
    event Action<DiscoveryDatagram>? DatagramReceived;

    /// <summary>Raised when a remote peer opens a TCP session to us — an inbound invite (background
    /// thread). The handler owns the connection's lifetime from here on.</summary>
    event Action<ILanConnection>? ConnectionAccepted;

    /// <summary>Dial a peer's TCP endpoint to open an outbound session (to send an invite).</summary>
    Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default);
}

/// <summary>
/// A duplex, line-oriented connection (one TCP socket): the invite handshake and then the move
/// stream flow over it as <see cref="LanProtocol"/> lines.
/// </summary>
public interface ILanConnection : IDisposable
{
    IPEndPoint RemoteEndPoint { get; }
    bool IsConnected { get; }

    /// <summary>Send one protocol line (the implementation adds newline framing). Thread-safe.</summary>
    void Send(string line);

    /// <summary>Raised for each complete line received (background thread).</summary>
    event Action<string>? LineReceived;

    /// <summary>Raised once when the peer disconnects or the socket errors.</summary>
    event Action? Closed;
}
