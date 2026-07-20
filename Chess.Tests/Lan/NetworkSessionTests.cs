using Chess.Lib;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class NetworkSessionTests
{
    private static (NetworkSession A, NetworkSession B, FakeLanConnection RawA, FakeLanConnection RawB) Connected()
    {
        var (a, b) = FakeLanConnection.CreatePair();
        return (new NetworkSession(a, Side.White, "B"), new NetworkSession(b, Side.Black, "A"), a, b);
    }

    [Fact]
    public void SendMove_DeliveredToPeerQueue_Once()
    {
        var (a, b, _, _) = Connected();
        using var _a = a; using var _b = b;

        a.SendMove("e2e4");

        b.TryDequeueMove(out var m).ShouldBeTrue();
        m.ShouldBe("e2e4");
        b.TryDequeueMove(out _).ShouldBeFalse();
    }

    [Fact]
    public void Promotion_MovePreserved()
    {
        var (a, b, _, _) = Connected();
        using var _a = a; using var _b = b;

        a.SendMove("e7e8q");

        b.TryDequeueMove(out var m).ShouldBeTrue();
        m.ShouldBe("e7e8q");
    }

    [Fact]
    public void LocalAndRemoteSides_AreOpposite()
    {
        var (a, b, _, _) = Connected();
        using var _a = a; using var _b = b;

        a.LocalSide.ShouldBe(Side.White);
        a.RemoteSide.ShouldBe(Side.Black);
        b.LocalSide.ShouldBe(Side.Black);
        b.RemoteSide.ShouldBe(Side.White);
    }

    [Fact]
    public void RemoteResign_SetsPeerLeft()
    {
        var (a, b, rawA, _) = Connected();
        using var _a = a; using var _b = b;

        rawA.Send(LanProtocol.EncodeResign()); // the far end resigns

        b.PeerLeft.ShouldBeTrue();
    }

    [Fact]
    public void PeerDisconnect_SetsPeerLeft()
    {
        var (a, b, _, _) = Connected();
        using var _b = b;

        a.Dispose(); // closing our socket makes the peer's read loop see us leave

        b.PeerLeft.ShouldBeTrue();
    }
}
