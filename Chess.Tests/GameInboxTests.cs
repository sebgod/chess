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

    // ── The row a picker shows ──────────────────────

    [Fact]
    public void Summary_NamesThePlayersAndWhenItBegan()
    {
        // Correspondent is Black, so we are White and go first in the name.
        SaveLink("g", Side.Black, Now - TimeSpan.FromDays(2), "Ada", "e2e4", "e7e5");

        GameInbox.TryLoad(_dir, "g")!.Value.Summary(Now, "Sebastian")
            .ShouldBe("Sebastian – Ada (10 Sep) · your move · 2 days ago");
    }

    [Fact]
    public void Title_PutsWhiteFirstWhicheverSideIsLocal()
    {
        SaveLink("white", Side.Black, Now, "Ada", "e2e4");  // we are White
        SaveLink("black", Side.White, Now, "Ada", "e2e4");  // we are Black

        GameInbox.TryLoad(_dir, "white")!.Value.Title("Me", Now).ShouldStartWith("Me – Ada");
        GameInbox.TryLoad(_dir, "black")!.Value.Title("Me", Now).ShouldStartWith("Ada – Me");
    }

    [Fact]
    public void Title_UnknownCorrespondent_HoldsTheSlotRatherThanGoingBlank()
    {
        // Link play carries no name. A blank there reads as something failed to load.
        SaveLink("g", Side.Black, Now, "", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Title("Me", Now).ShouldStartWith("Me – ?");
    }

    [Fact]
    public void Title_NoLocalNameSet_SaysYou()
    {
        // The LAN profile is optional, so the picker must read sensibly before anyone sets a name.
        SaveLink("g", Side.Black, Now, "Ada", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Title("", Now).ShouldStartWith("You – Ada");
    }

    [Fact]
    public void Title_ShowsTheYearOnlyWhenItIsNotThisOne()
    {
        SaveLink("thisYear", Side.Black, Now - TimeSpan.FromDays(5), "Ada", "e2e4");
        SaveLink("older", Side.Black, Now - TimeSpan.FromDays(400), "Ada", "e2e4");

        // A year on every row is noise; its absence on the recent ones is what makes an old game
        // stand out at a glance.
        GameInbox.TryLoad(_dir, "thisYear")!.Value.Title("Me", Now).ShouldEndWith("(7 Sep)");
        GameInbox.TryLoad(_dir, "older")!.Value.Title("Me", Now).ShouldEndWith("(8 Aug 2025)");
    }

    [Fact]
    public void Title_HotSeat_NamesNeitherPlayer()
    {
        // Both players are at this device, so calling one of them "Me" would be a claim about the
        // other that is not true.
        GameInbox.Save(_dir, "g", new Game(), Side.None, GameMode.PlayerVsPlayer, "", Now);

        GameInbox.TryLoad(_dir, "g")!.Value.Title("Me", Now).ShouldBe("Hot seat (12 Sep)");
    }

    [Fact]
    public void Started_DoesNotMoveWhenTheGameDoes()
    {
        // The start date is half the game's NAME, so a later save must not rename it.
        var began = Now - TimeSpan.FromDays(30);
        GameInbox.Save(_dir, "g", GameFromUci("e2e4"), Side.Black, GameMode.PlayByLink, "Ada", began);
        var first = GameInbox.TryLoad(_dir, "g")!.Value.Started;

        GameInbox.Save(_dir, "g", GameFromUci("e2e4", "e7e5"), Side.Black, GameMode.PlayByLink, "Ada",
            Now, started: first);

        var again = GameInbox.TryLoad(_dir, "g")!.Value;
        again.Started.ToUnixTimeSeconds().ShouldBe(began.ToUnixTimeSeconds());
        again.LastMove.ToUnixTimeSeconds().ShouldBe(Now.ToUnixTimeSeconds());
    }

    [Fact]
    public void Summary_WhenItIsTheirTurn()
    {
        SaveLink("g", Side.Black, Now, "Ada", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Summary(Now, "Me").ShouldContain("their move");
    }

    [Theory]
    [InlineData(GameMode.PlayByLink, "Link game")]
    [InlineData(GameMode.PlayerVsComputer, "vs Computer")]
    [InlineData(GameMode.NetworkGame, "LAN game")]
    [InlineData(GameMode.AcrossTheTable, "Across the table")]
    [InlineData(GameMode.PlayerVsPlayer, "Hot seat")]
    public void OpponentLabel_FallsBackToTheMode_WhenNobodyIsNamed(GameMode mode, string expected)
    {
        GameInbox.Save(_dir, "g", new Game(), Side.Black, mode, "", Now);

        GameInbox.TryLoad(_dir, "g")!.Value.OpponentLabel.ShouldBe(expected);
    }

    [Fact]
    public void OpponentLabel_PrefersTheNameOverTheMode()
    {
        SaveLink("g", Side.Black, Now, "Ada", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.OpponentLabel.ShouldBe("Ada");
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    [InlineData(5, "5 days ago")]
    [InlineData(21, "3 weeks ago")]
    [InlineData(90, "3 months ago")]
    public void Summary_AgesInTheCoarsestUsefulUnit(int daysAgo, string expected)
    {
        SaveLink("g", Side.Black, Now - TimeSpan.FromDays(daysAgo), "Ada", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Summary(Now, "Me").ShouldEndWith(expected);
    }

    [Fact]
    public void Summary_AClockThatWentBackwards_DoesNotPrintNegativeDays()
    {
        // A save stamped in the future (clock correction, a file copied from another machine) must
        // not render as "-3 days ago".
        SaveLink("g", Side.Black, Now + TimeSpan.FromDays(3), "Ada", "e2e4");

        GameInbox.TryLoad(_dir, "g")!.Value.Summary(Now, "Me").ShouldEndWith("just now");
    }

    [Fact]
    public void Summary_FinishedGame_SaysSoRatherThanNamingATurn()
    {
        SaveLink("g", Side.Black, Now, "Ada", "f2f3", "e7e5", "g2g4", "d8h4");

        GameInbox.TryLoad(_dir, "g")!.Value.Summary(Now, "Me").ShouldContain("finished");
    }

    [Fact]
    public void Started_FallsBackToTheLastMove_OnASaveFromBeforeTheFieldExisted()
    {
        // Written the way an older build would: a last-move stamp and no start date.
        Directory.CreateDirectory(GameInbox.FolderFor(_dir));
        GameStore.Save(Path.Combine(GameInbox.FolderFor(_dir), "old.uci"),
            GameFromUci("e2e4"), Side.Black, GameMode.PlayByLink, null, "Ada", Now);

        // Not the truth, but the closest thing on disk -- and it keeps the row nameable rather than
        // dating every legacy game to 1970.
        GameInbox.TryLoad(_dir, "old")!.Value.Started.ToUnixTimeSeconds()
            .ShouldBe(Now.ToUnixTimeSeconds());
    }
}
