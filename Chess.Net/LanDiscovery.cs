using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Chess.Lib;

namespace Chess.Net;

/// <summary>
/// The symmetric discovery half of LAN play: periodically broadcasts our own announce beacon *and*
/// listens for everyone else's, keeping a live, self-expiring table of peers. (tianwen only ever
/// scanned passive hardware, so it needed neither a beacon nor expiry — a peer lobby needs both.)
/// Time is driven by <see cref="TimeProvider"/> so beacon cadence and staleness are deterministic in
/// tests.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    /// <summary>How often we re-announce ourselves and prune stale peers.</summary>
    public static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(1);

    /// <summary>A peer unheard-from for this long is dropped from the list.</summary>
    public static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(5);

    private readonly ILanTransport _transport;
    private readonly TimeProvider _time;
    private readonly string _localPeerId;
    private readonly Func<string> _localName;
    private readonly object _lock = new();
    private readonly Dictionary<string, LanPeer> _peers = new();
    private ITimer? _beacon;

    /// <param name="localName">Read lazily each beacon so a name change mid-lobby propagates.</param>
    public LanDiscovery(ILanTransport transport, TimeProvider time, string localPeerId, Func<string> localName)
    {
        _transport = transport;
        _time = time;
        _localPeerId = localPeerId;
        _localName = localName;
        _transport.DatagramReceived += OnDatagram;
    }

    /// <summary>Begin beaconing (immediately, then every <see cref="BeaconInterval"/>).</summary>
    public void Start()
    {
        SendBeacon();
        _beacon = _time.CreateTimer(_ => SendBeacon(), null, BeaconInterval, BeaconInterval);
    }

    /// <summary>The current peers, most-recently-seen kept, sorted by display name.</summary>
    public IReadOnlyList<LanPeer> Peers
    {
        get
        {
            lock (_lock)
            {
                return _peers.Values
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <summary>Announce that we're leaving so peers drop us promptly (expiry is the fallback).</summary>
    public void SendBye() => _transport.Broadcast(LanProtocol.EncodeBye(_localPeerId));

    private void SendBeacon()
    {
        _transport.Broadcast(LanProtocol.EncodeAnnounce(_localPeerId, _transport.ListenPort, _localName()));
        Prune();
    }

    private void OnDatagram(DiscoveryDatagram dg)
    {
        var msg = LanProtocol.Parse(dg.Text);
        switch (msg.Kind)
        {
            case LanMessageKind.Announce:
                if (msg.PeerId == _localPeerId) return; // our own beacon echoes back to us — ignore it
                var endpoint = new IPEndPoint(dg.SenderAddress, msg.TcpPort);
                lock (_lock)
                {
                    _peers[msg.PeerId] = new LanPeer(msg.PeerId, msg.Name, endpoint, _time.GetUtcNow());
                }
                break;

            case LanMessageKind.Bye:
                lock (_lock) { _peers.Remove(msg.PeerId); }
                break;
        }
    }

    private void Prune()
    {
        var cutoff = _time.GetUtcNow() - PeerTimeout;
        lock (_lock)
        {
            var stale = _peers.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToArray();
            foreach (var id in stale)
            {
                _peers.Remove(id);
            }
        }
    }

    public void Dispose()
    {
        _beacon?.Dispose();
        _transport.DatagramReceived -= OnDatagram;
    }
}
