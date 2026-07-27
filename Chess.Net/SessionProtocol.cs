using System;
using Chess.Lib;

namespace Chess.Net;

/// <summary>The kind of a decoded session message. <see cref="Unknown"/> is a garbled line that must
/// be ignored.</summary>
public enum SessionMessageKind
{
    Unknown,
    Invite,     // "play me" — carries the inviter's chosen colour
    Accept,     // invite accepted; the connection becomes the game channel
    Decline,    // invite declined
    Move,       // one UCI move
    Resign,     // leaving the game
}

/// <summary>
/// A decoded session message. <see cref="Kind"/> says which fields are meaningful (Invite:
/// PeerId/Name/Color; Move: Move; the rest carry nothing).
/// </summary>
public readonly record struct SessionMessage(
    SessionMessageKind Kind,
    string PeerId = "",
    string Name = "",
    Side Color = Side.None,
    string Move = "");

/// <summary>
/// The wire format for the LAN game session — the TCP channel that opens once a peer has been found.
/// Deliberately plain, space-separated ASCII text (no reflection-JSON, so <c>Chess.Net</c> stays
/// AOT-clean), the same "UCI token you replay through the rules engine" spirit as
/// <see cref="Chess.UCI.GameLinkCodec"/>/<see cref="Chess.UCI.GameStore"/>. Every message is one line
/// prefixed with a magic word + version. Free text (the display name) is URL-encoded so it can never
/// contain a token-splitting space.
///
/// <para>Finding the peer is <b>not</b> here: the ANNOUNCE/BYE beacon this file used to carry now
/// lives in <c>LAN.Lib.LanProtocol</c>, on a broadcast port shared with every other SharpAstro app.
/// This is the private, point-to-point half — a stream, not a beacon — so it keeps its own magic.</para>
/// </summary>
public static class SessionProtocol
{
    /// <summary>The discovery service name chess announces and filters peers on — what keeps a
    /// tianwen rig on the same shared broadcast domain out of the chess lobby
    /// (<c>LAN.Lib.IPeerTable.PeersOf</c>).</summary>
    public const string ServiceName = "chess";

    /// <summary>Magic prefix identifying our session lines.</summary>
    public const string Magic = "CHESSLAN";

    /// <summary>Protocol version — bumped on an incompatible wire change.</summary>
    public const int Version = 1;

    private const string ColorWhite = "white";
    private const string ColorBlack = "black";

    public static string EncodeInvite(string peerId, string name, Side inviterColor) =>
        $"{Magic} {Version} INVITE {peerId} {Encode(name)} {ColorToken(inviterColor)}";

    public static string EncodeAccept() => $"{Magic} {Version} ACCEPT";

    public static string EncodeDecline() => $"{Magic} {Version} DECLINE";

    public static string EncodeMove(string uci) => $"{Magic} {Version} MOVE {uci}";

    public static string EncodeResign() => $"{Magic} {Version} RESIGN";

    /// <summary>Parses one line. Returns <see cref="SessionMessageKind.Unknown"/> for anything that
    /// isn't a well-formed message of ours (never throws).</summary>
    public static SessionMessage Parse(string line)
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
            "INVITE" when t.Length >= 6 =>
                new SessionMessage(SessionMessageKind.Invite, PeerId: t[3], Name: Decode(t[4]), Color: ParseColor(t[5])),
            "ACCEPT" =>
                new SessionMessage(SessionMessageKind.Accept),
            "DECLINE" =>
                new SessionMessage(SessionMessageKind.Decline),
            "MOVE" when t.Length >= 4 =>
                new SessionMessage(SessionMessageKind.Move, Move: t[3]),
            "RESIGN" =>
                new SessionMessage(SessionMessageKind.Resign),
            _ => default,
        };
    }

    // Empty names would produce a zero-length token that RemoveEmptyEntries drops, shifting every
    // field after it — so an empty string is encoded as a "-" sentinel (and decoded back to empty).
    private static string Encode(string s) => string.IsNullOrEmpty(s) ? "-" : Uri.EscapeDataString(s);
    private static string Decode(string s) => s == "-" ? "" : Uri.UnescapeDataString(s);

    private static string ColorToken(Side s) => s == Side.Black ? ColorBlack : ColorWhite;
    private static Side ParseColor(string s) => s == ColorBlack ? Side.Black : Side.White;
}
