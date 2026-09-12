using System;
using System.Linq;
using Chess.Lib;
using Chess.UCI;
using Shouldly;
using Xunit;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Chess.Tests;

/// <summary>
/// Covers what <see cref="GameInbox"/> adds over <see cref="GameStore"/>: several games at once,
/// which of them is waiting on you, when one has gone quiet, and the one-time move off the old
/// single-slot save. One game's format and replay stay pinned by <see cref="GameStoreTests"/>.
/// </summary>
public sealed class GameInboxTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"chess-inbox-{Guid.NewGuid():N}");

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static Game GameFromUci(params string[] moves)
    {
        var game = new Game();
        foreach (var m in moves)
            game.TryMove(UciMove.Parse(m)).IsMoveOrCapture().ShouldBeTrue($"move {m} should apply");
        return game;
    }

    private string SaveLink(string id, Side correspondent, DateTimeOffset at, string opponent, params string[] moves)
    {
        GameInbox.Save(_dir, id, GameFromUci(moves), correspondent, GameMode.PlayByLink, opponent, at);
        return id;
    }

    // ── Several games at once ──────────────────────────────────────

    [Fact]
    public void Load_NoFolderYet_IsEmptyRatherThanAnError()
    {
        // First launch: there is nothing, and that is not a failure state.
        GameInbox.Load(_dir).ShouldBeEmpty();
    }

    [Fact]
    public void SaveThenLoad_KeepsGamesApart()
    {
        // The whole point of phase 2: a second correspondence game must not evict the first.
        SaveLink("alice", Side.Black, Now, "Alice", "e2e4");
        SaveLink("bob", Side.White, Now, "Bob", "d2d4", "d7d5");

        var all = GameInbox.Load(_dir);

        all.Count.ShouldBe(2);
        all.Single(e => e.Id == "alice").Game.PlyCount.ShouldBe(1);
        all.Single(e => e.Id == "bob").Game.PlyCount.ShouldBe(2);
        all.Single(e => e.Id == "alice").Opponent.ShouldBe("Alice");
    }

    [Fact]
    public void Load_OrdersMostRecentlyChangedFirst()
    {
        SaveLink("old", Side.Black, Now.AddDays(-10), "Old", "e2e4");
        SaveLink("fresh", Side.Black, Now, "Fresh", "d2d4");

        GameInbox.Load(_dir).Select(e => e.Id).ShouldBe(["fresh", "old"]);
    }

    [Fact]
    public void Load_OneCorruptEntry_DoesNotCostTheOthers()
    {
        // The reason each game is its own file. A half-written save loses that game, never the inbox.
        SaveLink("good", Side.Black, Now, "Good", "e2e4");
        File.WriteAllText(Path.Combine(GameInbox.FolderFor(_dir), "broken.uci"), "None PlayByLink\nzzzz");

        var all = GameInbox.Load(_dir);

        all.Count.ShouldBe(1);
        all[0].Id.ShouldBe("good");
    }

    [Fact]
    public void Delete_RemovesOnlyThatGame()
    {
        SaveLink("keep", Side.Black, Now, "Keep", "e2e4");
        SaveLink("drop", Side.Black, Now, "Drop", "d2d4");

        GameInbox.Delete(_dir, "drop");

        GameInbox.Load(_dir).Select(e => e.Id).ShouldBe(["keep"]);
    }

    [Fact]
    public void Delete_MissingGame_IsNotAnError()
    {
        Should.NotThrow(() => GameInbox.Delete(_dir, "never-existed"));
    }

    // ── Which ones need you ────────────────────────────────────────

    [Theory]
    [InlineData(Side.Black, true)]  // correspondent is Black, so we are White, and White is to move
    [InlineData(Side.White, false)] // correspondent is White and it is their turn
    public void IsWaitingOnYou_TracksWhoseTurnItIs(Side correspondent, bool expected)
    {
        SaveLink("g", correspondent, Now, "Them"); // fresh game, White to move

        GameInbox.TryLoad(_dir, "g")!.Value.IsWaitingOnYou.ShouldBe(expected);
    }

    [Fact]
    public void IsWaitingOnYou_FinishedGame_IsNot()
    {
        // Fool's mate: Black has mated, so nothing is waiting on anyone.
        SaveLink("done", Side.Black, Now, "Them", "f2f3", "e7e5", "g2g4", "d8h4");

        var diagnostics = new System.Collections.Generic.List<string>();
        var loaded = GameInbox.TryLoad(_dir, "done", diagnostics.Add);
        loaded.ShouldNotBeNull(string.Join(" | ", diagnostics));
        var entry = loaded.Value;

        entry.Game.IsFinished.ShouldBeTrue();
        entry.IsWaitingOnYou.ShouldBeFalse();
    }

    [Theory]
    [InlineData(GameMode.PlayerVsPlayer)]
    [InlineData(GameMode.PlayerVsComputer)]
    [InlineData(GameMode.AcrossTheTable)]
    public void IsWaitingOnYou_NonCorrespondenceModes_AreNeverListed(GameMode mode)
    {
        // A hot-seat game is ALWAYS your move and an engine game answers in a second; listing either
        // would bury the games that genuinely need you.
        GameInbox.Save(_dir, "g", new Game(), Side.Black, mode, "", Now);

        GameInbox.TryLoad(_dir, "g")!.Value.IsWaitingOnYou.ShouldBeFalse();
    }

    [Theory]
    [InlineData(Side.Black, Side.White)]
    [InlineData(Side.White, Side.Black)]
    [InlineData(Side.None, Side.None)] // hot-seat: both players are here, so there is no local side
    public void LocalSide_IsTheOppositeOfTheOtherPlayers(Side correspondent, Side expected)
    {
        GameInbox.Save(_dir, "g", new Game(), correspondent, GameMode.PlayByLink, "", Now);

        GameInbox.TryLoad(_dir, "g")!.Value.LocalSide.ShouldBe(expected);
    }

    // ── Going quiet ────────────────────────────────────────────────

    [Fact]
    public void IsStale_OnlyPastTheThreshold()
    {
        SaveLink("quiet", Side.Black, Now - GameInbox.StaleAfter - TimeSpan.FromDays(1), "Gone", "e2e4");
        SaveLink("recent", Side.Black, Now - TimeSpan.FromDays(30), "Here", "e2e4");

        GameInbox.TryLoad(_dir, "quiet")!.Value.IsStale(Now).ShouldBeTrue();
        GameInbox.TryLoad(_dir, "recent")!.Value.IsStale(Now).ShouldBeFalse();
    }

    [Fact]
    public void AStaleGameIsStillLoadable_BecauseItIsHiddenAndNotDeleted()
    {
        // The rule that matters: four months of silence hides a game from the default list. It must
        // not remove it -- correspondence games legitimately take months, and somebody coming back
        // from a long absence should find their game intact.
        var id = SaveLink("sabbatical", Side.Black, Now - TimeSpan.FromDays(365), "Patient", "e2e4", "e7e5");

        var entry = GameInbox.TryLoad(_dir, id)!.Value;

        entry.IsStale(Now).ShouldBeTrue();
        entry.Game.PlyCount.ShouldBe(2);
        GameInbox.Load(_dir).ShouldContain(e => e.Id == id);
    }

    // ── Metadata round-trip ────────────────────────────────────────

    [Fact]
    public void Opponent_SurvivesASpaceInTheName()
    {
        // Line 1 is split on spaces, so a two-word name has to be encoded or it becomes two tokens
        // and the mode parse downstream reads garbage.
        SaveLink("g", Side.Black, Now, "Ada Lovelace", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Opponent.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public void LastMove_RoundTripsToTheSecond()
    {
        SaveLink("g", Side.Black, Now, "Them", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.LastMove.ToUnixTimeSeconds()
            .ShouldBe(Now.ToUnixTimeSeconds());
    }

    [Fact]
    public void AnOlderBuildCanStillReadAnInboxFile()
    {
        // Forward compatibility: the metadata rides as extra key=value tokens on line 1, and a build
        // that predates them reads only the two positional tokens. GameStore.TryLoad IS that reader.
        SaveLink("g", Side.Black, Now, "Ada Lovelace", "e2e4", "e7e5");

        var asOldBuildSeesIt = GameStore.TryLoad(
            Path.Combine(GameInbox.FolderFor(_dir), "g.uci"))!.Value;

        asOldBuildSeesIt.ComputerSide.ShouldBe(Side.Black);
        asOldBuildSeesIt.Mode.ShouldBe(GameMode.PlayByLink);
        asOldBuildSeesIt.Game.PlyCount.ShouldBe(2);
    }

    // ── The one-time move off the single slot ──────────────────────

    [Fact]
    public void MigrateLegacySave_MovesTheOldGameInAndRemovesIt()
    {
        Directory.CreateDirectory(_dir);
        var legacy = Path.Combine(_dir, "game.uci");
        GameStore.Save(legacy, GameFromUci("e2e4", "e7e5"), Side.Black, GameMode.PlayerVsComputer);

        GameInbox.MigrateLegacySave(_dir, Now);

        File.Exists(legacy).ShouldBeFalse();
        var all = GameInbox.Load(_dir);
        all.Count.ShouldBe(1);
        all[0].Game.PlyCount.ShouldBe(2);
        all[0].ComputerSide.ShouldBe(Side.Black);
        all[0].Mode.ShouldBe(GameMode.PlayerVsComputer);
    }

    [Fact]
    public void MigrateLegacySave_KeepsWhenItWasLastPlayed()
    {
        // Stamping the migrated game "now" would make a long-abandoned save look freshly active and
        // push it to the top of the list, which is the opposite of what the timestamp is for.
        Directory.CreateDirectory(_dir);
        var legacy = Path.Combine(_dir, "game.uci");
        GameStore.Save(legacy, GameFromUci("e2e4"), Side.None, GameMode.PlayerVsPlayer);
        var played = Now - TimeSpan.FromDays(200);
        File.SetLastWriteTimeUtc(legacy, played.UtcDateTime);

        GameInbox.MigrateLegacySave(_dir, Now);

        var entry = GameInbox.Load(_dir).Single();
        entry.LastMove.ToUnixTimeSeconds().ShouldBe(played.ToUnixTimeSeconds());
        entry.IsStale(Now).ShouldBeTrue();
    }

    [Fact]
    public void MigrateLegacySave_RunsTwice_WithoutDuplicating()
    {
        Directory.CreateDirectory(_dir);
        GameStore.Save(Path.Combine(_dir, "game.uci"), GameFromUci("e2e4"), Side.None, GameMode.PlayerVsPlayer);

        GameInbox.MigrateLegacySave(_dir, Now);
        GameInbox.MigrateLegacySave(_dir, Now);

        GameInbox.Load(_dir).Count.ShouldBe(1);
    }

    [Fact]
    public void MigrateLegacySave_NothingToMigrate_IsANoOp()
    {
        Should.NotThrow(() => GameInbox.MigrateLegacySave(_dir, Now));
        GameInbox.Load(_dir).ShouldBeEmpty();
    }

    [Fact]
    public void MigrateLegacySave_UnreadableSave_IsDiscardedRatherThanRetriedForever()
    {
        Directory.CreateDirectory(_dir);
        var legacy = Path.Combine(_dir, "game.uci");
        File.WriteAllText(legacy, "None PlayerVsPlayer\nzzzz");

        GameInbox.MigrateLegacySave(_dir, Now);

        File.Exists(legacy).ShouldBeFalse();
        GameInbox.Load(_dir).ShouldBeEmpty();
    }

    [Fact]
    public void NewId_IsUniqueAndChronologicallySortable()
    {
        var earlier = GameInbox.NewId(Now);
        var later = GameInbox.NewId(Now.AddSeconds(1));

        earlier.ShouldNotBe(later);
        // Sortable as text, which is what makes a bare directory listing chronological.
        string.CompareOrdinal(earlier, later).ShouldBeLessThan(0);
        GameInbox.NewId(Now).ShouldNotBe(GameInbox.NewId(Now)); // same instant, still distinct
    }
}
