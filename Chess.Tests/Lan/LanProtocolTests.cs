using Chess.Lib;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanProtocolTests
{
    [Fact]
    public void Announce_RoundTrips_AllFields()
    {
        var line = LanProtocol.EncodeAnnounce("peer-abc", 51234, "Alice");

        var msg = LanProtocol.Parse(line);

        msg.Kind.ShouldBe(LanMessageKind.Announce);
        msg.PeerId.ShouldBe("peer-abc");
        msg.TcpPort.ShouldBe(51234);
        msg.Name.ShouldBe("Alice");
    }

    [Theory]
    [InlineData("Alice Smith")]        // spaces would split tokens without url-encoding
    [InlineData("møøse 🐴")]           // unicode
    [InlineData("a b\tc")]             // whitespace variety
    public void Announce_NameWithSpecialChars_SurvivesEncoding(string name)
    {
        var msg = LanProtocol.Parse(LanProtocol.EncodeAnnounce("id", 1, name));

        msg.Kind.ShouldBe(LanMessageKind.Announce);
        msg.Name.ShouldBe(name);
    }

    [Fact]
    public void Announce_EmptyName_RoundTripsAsEmpty()
    {
        var msg = LanProtocol.Parse(LanProtocol.EncodeAnnounce("id", 1, ""));

        msg.Kind.ShouldBe(LanMessageKind.Announce);
        msg.Name.ShouldBe("");
    }

    [Theory]
    [InlineData(Side.White)]
    [InlineData(Side.Black)]
    public void Invite_RoundTrips_Color(Side color)
    {
        var msg = LanProtocol.Parse(LanProtocol.EncodeInvite("peer-1", "Bob", color));

        msg.Kind.ShouldBe(LanMessageKind.Invite);
        msg.PeerId.ShouldBe("peer-1");
        msg.Name.ShouldBe("Bob");
        msg.Color.ShouldBe(color);
    }

    [Theory]
    [InlineData("e2e4")]
    [InlineData("e7e8q")] // promotion token must survive
    [InlineData("a1h8")]
    public void Move_RoundTrips(string uci)
    {
        var msg = LanProtocol.Parse(LanProtocol.EncodeMove(uci));

        msg.Kind.ShouldBe(LanMessageKind.Move);
        msg.Move.ShouldBe(uci);
    }

    [Fact]
    public void Bye_RoundTrips_PeerId()
    {
        LanProtocol.Parse(LanProtocol.EncodeBye("peer-x")).ShouldBe(
            new LanMessage(LanMessageKind.Bye, PeerId: "peer-x"));
    }

    [Fact]
    public void Accept_Decline_Resign_Parse()
    {
        LanProtocol.Parse(LanProtocol.EncodeAccept()).Kind.ShouldBe(LanMessageKind.Accept);
        LanProtocol.Parse(LanProtocol.EncodeDecline()).Kind.ShouldBe(LanMessageKind.Decline);
        LanProtocol.Parse(LanProtocol.EncodeResign()).Kind.ShouldBe(LanMessageKind.Resign);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]                 // wrong magic
    [InlineData("CHESSLAN 1")]                  // no verb
    [InlineData("CHESSLAN 1 ANNOUNCE id")]      // announce missing port+name
    [InlineData("SOMETHINGELSE 1 MOVE e2e4")]   // foreign magic on the shared port
    public void Parse_ForeignOrGarbled_ReturnsUnknown(string line)
    {
        LanProtocol.Parse(line).Kind.ShouldBe(LanMessageKind.Unknown);
    }
}
