using System;
using System.Net;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanPeerTests
{
    private static LanPeer P(string name, string machine, int pid) =>
        new(Guid.NewGuid().ToString("N"), name, machine, pid,
            new IPEndPoint(IPAddress.Loopback, 1), DateTimeOffset.UnixEpoch);

    [Fact]
    public void ResolveLabels_UniqueNames_NoSuffix()
    {
        LanPeer.ResolveLabels([P("Seb", "lap1", 1), P("Ana", "lap2", 2)])
            .ShouldBe(["Seb", "Ana"]);
    }

    [Fact]
    public void ResolveLabels_NameCollidesAcrossMachines_AddsMachineName()
    {
        LanPeer.ResolveLabels([P("Seb", "lap1", 1), P("Seb", "lap2", 2)])
            .ShouldBe(["Seb (lap1)", "Seb (lap2)"]);
    }

    [Fact]
    public void ResolveLabels_NameAndMachineCollide_NumbersByAscendingPid()
    {
        // Two "Seb" instances on lap1. Labels come back in input order, but the #n index is assigned
        // by ascending PID — so the higher-PID peer (first here) is #2, independent of input order.
        LanPeer.ResolveLabels([P("Seb", "lap1", 50), P("Seb", "lap1", 10)])
            .ShouldBe(["Seb (lap1) #2", "Seb (lap1) #1"]);
    }

    [Fact]
    public void ResolveLabels_MixedCollision_OnlyDisambiguatesWhatCollides()
    {
        // "Ana" is unique → bare. Two "Seb": one alone on lap1 (just machine), two on lap2 (numbered).
        LanPeer.ResolveLabels([P("Ana", "lap9", 1), P("Seb", "lap1", 5), P("Seb", "lap2", 7), P("Seb", "lap2", 3)])
            .ShouldBe(["Ana", "Seb (lap1)", "Seb (lap2) #2", "Seb (lap2) #1"]);
    }
}
