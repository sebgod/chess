using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net;

/// <summary>
/// The socket LAN play needs on TOP of discovery: a TCP listener/dialer for the game session, behind
/// an interface so the lobby logic is unit-testable against an in-memory fake with no real network.
/// <see cref="TcpSessionTransport"/> is the real backend.
///
/// <para>Finding the peer in the first place is <c>LAN.Lib</c>'s job (a UDP announce beacon and a
/// self-expiring peer table, shared with every other SharpAstro app on one broadcast domain); its
/// whole output is the <c>address:port</c> this transport dials. The split is deliberate: discovery is
/// generic, the invite/move channel is chess's.</para>
/// </summary>
public interface ISessionTransport : IAsyncDisposable
{
    /// <summary>The TCP port our session listener is bound to — announced in the discovery beacon
    /// (as the service port) so peers know where to dial.</summary>
    int ListenPort { get; }

    /// <summary>Raised when a remote peer opens a TCP session to us — an inbound invite (background
    /// thread). The handler owns the connection's lifetime from here on.</summary>
    event Action<ILanConnection>? ConnectionAccepted;

    /// <summary>Dial a peer's TCP endpoint to open an outbound session (to send an invite).</summary>
    Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default);
}

/// <summary>
/// A duplex, line-oriented connection (one TCP socket): the invite handshake and then the move
/// stream flow over it as <see cref="SessionProtocol"/> lines.
/// </summary>
public interface ILanConnection : IDisposable
{
    IPEndPoint RemoteEndPoint { get; }
    bool IsConnected { get; }

    /// <summary>Send one protocol line (the implementation adds newline framing). Thread-safe.</summary>
    void Send(string line);

    /// <summary>
    /// Begins delivering <see cref="LineReceived"/>. Call it AFTER wiring the handlers: the peer
    /// typically writes its first line the instant it connects (the inviter sends INVITE straight
    /// after <see cref="ISessionTransport.ConnectAsync"/>), so a connection that started reading in
    /// its constructor could raise — and drop — that line before anyone had subscribed. Idempotent.
    /// </summary>
    void StartReceiving();

    /// <summary>Raised for each complete line received (background thread), once
    /// <see cref="StartReceiving"/> has been called.</summary>
    event Action<string>? LineReceived;

    /// <summary>Raised once when the peer disconnects or the socket errors.</summary>
    event Action? Closed;
}
