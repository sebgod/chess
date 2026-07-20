using System;
using System.Net;

namespace Chess.Net;

/// <summary>
/// A peer currently visible on the LAN, learned from its UDP announce beacon. <see cref="EndPoint"/>
/// is the peer's TCP session endpoint (its sender IP + the port it announced) — dial it to invite.
/// <see cref="LastSeen"/> drives lobby expiry: a peer that stops beaconing is dropped.
/// </summary>
public sealed record LanPeer(string PeerId, string Name, IPEndPoint EndPoint, DateTimeOffset LastSeen)
{
    /// <summary>A friendly label for the lobby list — falls back to the id when unnamed.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? PeerId : Name;
}
