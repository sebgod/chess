using Chess.Lib;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

/// <summary>
/// The session channel's wire format. The discovery half (ANNOUNCE/BYE, name encoding, foreign-datagram
/// tolerance) moved out with the beacon and is covered by LAN.Lib's own LanProtocolTests.
/// </summary>
public class SessionProtocolTests
{
    [Theory]
    [InlineData(Side.White)]
    [InlineData(Side.Black)]
    public void Invite_RoundTrips_Color(Side color)
    {
        var msg = SessionProtocol.Parse(SessionProtocol.EncodeInvite("peer-1", "Bob", color));

        msg.Kind.ShouldBe(SessionMessageKind.Invite);
        msg.PeerId.ShouldBe("peer-1");
        msg.Name.ShouldBe("Bob");
        msg.Color.ShouldBe(color);
    }

    [Theory]
    [InlineData("Alice Smith")]        // spaces would split tokens without url-encoding
    [InlineData("møøse 🐴")]           // unicode
    [InlineData("a b\tc")]             // whitespace variety
    public void Invite_NameWithSpecialChars_SurvivesEncoding(string name)
    {
        var msg = SessionProtocol.Parse(SessionProtocol.EncodeInvite("id", name, Side.White));

        msg.Kind.ShouldBe(SessionMessageKind.Invite);
        msg.Name.ShouldBe(name);
    }

    [Fact]
    public void Invite_EmptyName_RoundTripsAsEmpty()
    {
        // The "-" sentinel: an empty token would be dropped by RemoveEmptyEntries and shift the colour.
        var msg = SessionProtocol.Parse(SessionProtocol.EncodeInvite("id", "", Side.Black));

        msg.Kind.ShouldBe(SessionMessageKind.Invite);
        msg.Name.ShouldBe("");
        msg.Color.ShouldBe(Side.Black);
    }

    [Theory]
    [InlineData("e2e4")]
    [InlineData("e7e8q")] // promotion token must survive
    [InlineData("a1h8")]
    public void Move_RoundTrips(string uci)
    {
        var msg = SessionProtocol.Parse(SessionProtocol.EncodeMove(uci));

        msg.Kind.ShouldBe(SessionMessageKind.Move);
        msg.Move.ShouldBe(uci);
    }

    [Fact]
    public void Accept_Decline_Resign_Parse()
    {
        SessionProtocol.Parse(SessionProtocol.EncodeAccept()).Kind.ShouldBe(SessionMessageKind.Accept);
        SessionProtocol.Parse(SessionProtocol.EncodeDecline()).Kind.ShouldBe(SessionMessageKind.Decline);
        SessionProtocol.Parse(SessionProtocol.EncodeResign()).Kind.ShouldBe(SessionMessageKind.Resign);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]                 // wrong magic
    [InlineData("CHESSLAN 1")]                  // no verb
    [InlineData("CHESSLAN 1 INVITE id")]        // invite missing name+colour
    [InlineData("SALAN 1 ANNOUNCE id chess 1 Seb")] // a discovery beacon is NOT a session line
    [InlineData("SOMETHINGELSE 1 MOVE e2e4")]   // foreign magic
    public void Parse_ForeignOrGarbled_ReturnsUnknown(string line)
    {
        SessionProtocol.Parse(line).Kind.ShouldBe(SessionMessageKind.Unknown);
    }
}
