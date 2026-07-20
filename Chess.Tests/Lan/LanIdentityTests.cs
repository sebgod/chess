using System;
using System.IO;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanIdentityTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "chess-lan-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsName()
    {
        var dir = TempDir();
        try
        {
            new LanIdentity("Alice", "ignored-peer-id").Save(dir);

            LanIdentity.Load(dir).Name.ShouldBe("Alice");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_Missing_MintsFreshIdWithEmptyName()
    {
        var id = LanIdentity.Load(TempDir()); // directory doesn't exist

        id.Name.ShouldBe("");
        id.PeerId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerId_IsMintedPerLoad_NotPersisted()
    {
        var dir = TempDir();
        try
        {
            new LanIdentity("Alice", "aaaa").Save(dir);

            // Even loading the SAME saved dir twice yields two fresh ids (never the one "saved") —
            // this is what lets two instances sharing one lan.txt discover each other.
            var first = LanIdentity.Load(dir).PeerId;
            var second = LanIdentity.Load(dir).PeerId;

            first.ShouldNotBe("aaaa");
            second.ShouldNotBe("aaaa");
            first.ShouldNotBe(second);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
