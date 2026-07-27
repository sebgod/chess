using System;
using System.IO;
using Chess.Net;
using Shouldly;
using Xunit;

namespace Chess.Tests.Lan;

public class LanProfileTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "chess-lan-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveThenLoad_RoundTripsName()
    {
        var dir = TempDir();
        try
        {
            new LanProfile("Alice").Save(dir);

            LanProfile.Load(dir).Name.ShouldBe("Alice");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_Missing_GivesEmptyName()
    {
        LanProfile.Load(TempDir()).Name.ShouldBe(""); // directory doesn't exist
    }

    [Fact]
    public void Load_IgnoresTheLegacyPeerIdLine()
    {
        // Older builds wrote the peer id as a second line. Identity is LAN.Lib's job now (a fresh
        // per-process id), and that stale line must never be mistaken for the name.
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, LanProfile.FileName), ["Alice", "deadbeef"]);

            LanProfile.Load(dir).Name.ShouldBe("Alice");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
