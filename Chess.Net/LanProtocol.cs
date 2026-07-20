using System;
using Chess.Lib;

namespace Chess.Net;

/// <summary>The kind of a decoded LAN message. <see cref="Unknown"/> is a foreign/garbled
/// datagram or line that must be ignored (the port is shared with whatever else broadcasts).</summary>
public enum LanMessageKind
{
    Unknown,
    Announce,   // UDP beacon: "I'm here, this is my name and TCP port"
    Bye,        // UDP: "I'm leaving the lobby" (prompt removal; expiry is the fallback)
    Invite,     // TCP: "play me" — carries the inviter's chosen colour
    Accept,     // TCP: invite accepted; the connection becomes the game channel
    Decline,    // TCP: invite declined
    Move,       // TCP: one UCI move
    Resign,     // TCP: leaving the game
}

/// <summary>
/// A decoded LAN message. One record covers both channels; <see cref="Kind"/> says which fields are
/// meaningful (Announce: PeerId/Name/TcpPort/MachineName/Pid; Invite: PeerId/Name/Color; Move: Move; …).
/// </summary>
public readonly record struct LanMessage(
    LanMessageKind Kind,
    string PeerId = "",
    string Name = "",
    int TcpPort = 0,
    Side Color = Side.None,
    string Move = "",
    string MachineName = "",
    int Pid = 0);

/// <summary>
/// The wire format for LAN play — deliberately plain, space-separated ASCII text (no reflection-JSON,
/// so <c>Chess.Net</c> stays AOT-clean), the same "UCI token you replay through the rules engine"
/// spirit as <see cref="Chess.UCI.GameLinkCodec"/>/<see cref="Chess.UCI.GameStore"/>. Every message
/// is one line prefixed with a magic word + version so a foreign datagram on the shared discovery
/// port is ignored rather than misparsed. Free-text (the display name) is URL-encoded so it can never
/// contain a token-splitting space.
/// </summary>
public static class LanProtocol
{
    /// <summary>Magic prefix identifying our datagrams/lines on the shared port.</summary>
    public const string Magic = "CHESSLAN";

    /// <summary>Protocol version — bumped on an incompatible wire change.</summary>
    public const int Version = 1;

    /// <summary>Fixed UDP port both peers broadcast/listen on for discovery.</summary>
    public const int DiscoveryPort = 52821;

    private const string ColorWhite = "white";
    private const string ColorBlack = "black";

    // machineName + pid ride along so the lobby can disambiguate look-alike beacons (same display
    // name across machines → show the machine; same name on one machine → number by PID).
    public static string EncodeAnnounce(string peerId, int tcpPort, string name, string machineName = "", int pid = 0) =>
        $"{Magic} {Version} ANNOUNCE {peerId} {tcpPort} {Encode(name)} {Encode(machineName)} {pid}";

    public static string EncodeBye(string peerId) =>
        $"{Magic} {Version} BYE {peerId}";

    public static string EncodeInvite(string peerId, string name, Side inviterColor) =>
        $"{Magic} {Version} INVITE {peerId} {Encode(name)} {ColorToken(inviterColor)}";

    public static string EncodeAccept() => $"{Magic} {Version} ACCEPT";

    public static string EncodeDecline() => $"{Magic} {Version} DECLINE";

    public static string EncodeMove(string uci) => $"{Magic} {Version} MOVE {uci}";

    public static string EncodeResign() => $"{Magic} {Version} RESIGN";

    /// <summary>Parses one line/datagram. Returns <see cref="LanMessageKind.Unknown"/> for anything
    /// that isn't a well-formed message of ours (never throws).</summary>
    public static LanMessage Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return default;

        var t = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Need at least: magic, version, verb.
        if (t.Length < 3 || t[0] != Magic)
            return default;

        // t[1] is the version; unknown future versions still parse best-effort by verb.
        return t[2] switch
        {
            // machineName/pid are trailing tokens — tolerate their absence so a minimal (older) announce
            // still parses; they just default to unknown ("" / 0).
            "ANNOUNCE" when t.Length >= 6 =>
                new LanMessage(LanMessageKind.Announce, PeerId: t[3], TcpPort: ParseInt(t[4]), Name: Decode(t[5]),
                    MachineName: t.Length >= 7 ? Decode(t[6]) : "", Pid: t.Length >= 8 ? ParseInt(t[7]) : 0),
            "BYE" when t.Length >= 4 =>
                new LanMessage(LanMessageKind.Bye, PeerId: t[3]),
            "INVITE" when t.Length >= 6 =>
                new LanMessage(LanMessageKind.Invite, PeerId: t[3], Name: Decode(t[4]), Color: ParseColor(t[5])),
            "ACCEPT" =>
                new LanMessage(LanMessageKind.Accept),
            "DECLINE" =>
                new LanMessage(LanMessageKind.Decline),
            "MOVE" when t.Length >= 4 =>
                new LanMessage(LanMessageKind.Move, Move: t[3]),
            "RESIGN" =>
                new LanMessage(LanMessageKind.Resign),
            _ => default,
        };
    }

    // Empty names would produce a zero-length token that RemoveEmptyEntries drops, shifting every
    // field after it — so an empty string is encoded as a "-" sentinel (and decoded back to empty).
    private static string Encode(string s) => string.IsNullOrEmpty(s) ? "-" : Uri.EscapeDataString(s);
    private static string Decode(string s) => s == "-" ? "" : Uri.UnescapeDataString(s);

    private static string ColorToken(Side s) => s == Side.Black ? ColorBlack : ColorWhite;
    private static Side ParseColor(string s) => s == ColorBlack ? Side.Black : Side.White;

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
}
