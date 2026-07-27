using Chess.Lib;
using Chess.Lib.UI;
using Shouldly;
using Xunit;

namespace Chess.Tests;

public class StartupWizardTests
{
    // ── GameMode items ─────────────────────────────────────────────

    [Fact]
    public void Current_DefaultCtor_ExcludesLinkPlay()
    {
        var wizard = new StartupWizard();

        wizard.Current.Items.ShouldBe(["Player vs Player", "Player vs Computer", "Custom Game"]);
    }

    [Fact]
    public void Current_IncludeLinkPlay_AppendsFourthItem()
    {
        var wizard = new StartupWizard(StartupWizardOptions.LinkPlay);

        wizard.Current.Items.ShouldBe(
            ["Player vs Player", "Player vs Computer", "Custom Game", "Play by Link"]);
    }

    [Fact]
    public void Current_IncludeAcrossTheTable_InsertsAfterPlayerVsPlayer()
    {
        var wizard = new StartupWizard(StartupWizardOptions.AcrossTheTable);

        wizard.Current.Items.ShouldBe(
            ["Player vs Player", "Across the table", "Player vs Computer", "Custom Game"]);
    }

    // ── Existing flows unchanged (regression for the explicit-index rewrite) ──

    [Fact]
    public void Confirm_PlayerVsPlayer_CompletesWithNoComputer()
    {
        var wizard = new StartupWizard();

        wizard.Confirm(0);

        wizard.IsComplete.ShouldBeTrue();
        // No engine in a hot-seat game, so difficulty is never asked for and keeps its default.
        wizard.Result.ShouldBe((GameMode.PlayerVsPlayer, Side.None, Side.White, Difficulty.Normal));
    }

    [Theory]
    [InlineData(0, Side.Black)] // play as White → computer is Black
    [InlineData(1, Side.White)] // play as Black → computer is White
    public void Confirm_PlayerVsComputer_PlayAsAssignsOpponent(int playAs, Side expectedComputer)
    {
        var wizard = new StartupWizard();

        wizard.Confirm(1);
        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Play as:");

        wizard.Confirm(playAs);
        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Difficulty:");
        wizard.Confirm(2); // Hard

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.PlayerVsComputer, expectedComputer, Side.White, Difficulty.Hard));
    }

    [Fact]
    public void Confirm_CustomGame_RunsBoardTypeSideToMoveHumanSide()
    {
        var wizard = new StartupWizard();

        wizard.Confirm(2); // Custom Game
        wizard.Current.Prompt.ShouldBe("Starting board:");
        wizard.Confirm(1); // Standard Board
        wizard.Current.Prompt.ShouldBe("Side to move first:");
        wizard.Confirm(1); // Black moves first
        wizard.Current.Prompt.ShouldBe("Play as:");
        wizard.Confirm(0); // human is White → computer is Black
        // A custom game is always against the engine, so this flow always asks the difficulty too.
        wizard.Current.Prompt.ShouldBe("Difficulty:");
        wizard.Confirm(0); // Easy

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.CustomGameStandardBoard, Side.Black, Side.Black, Difficulty.Easy));
    }

    // ── Difficulty ─────────────────────────────────────────────────

    [Fact]
    public void Current_Difficulty_ListsTheSharedLevels()
    {
        var wizard = new StartupWizard();
        wizard.Confirm(1); // Player vs Computer
        wizard.Confirm(0); // play as White

        // Generated from DifficultyExtensions.All, so a new level cannot appear in one front-end's
        // menu and not another's.
        wizard.Current.Items.ShouldBe(["Easy", "Normal", "Hard"]);
    }

    [Theory]
    [InlineData(0, Difficulty.Easy)]
    [InlineData(1, Difficulty.Normal)]
    [InlineData(2, Difficulty.Hard)]
    public void Confirm_Difficulty_SelectsTheLevelAtThatIndex(int selected, Difficulty expected)
    {
        var wizard = new StartupWizard();
        wizard.Confirm(1); // Player vs Computer
        wizard.Confirm(0); // play as White

        wizard.Confirm(selected);

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Difficulty.ShouldBe(expected);
    }

    [Fact]
    public void Confirm_NetworkGame_SkipsDifficulty()
    {
        // The "computer side" of a LAN game is a remote human. Asking how hard they should play
        // would be nonsense, and it would also strand the wizard on a step the host never answers.
        var wizard = new StartupWizard(StartupWizardOptions.NetworkPlay);

        wizard.Confirm(3); // Network game
        wizard.Current.Prompt.ShouldBe("Play as:");
        wizard.Confirm(0);

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Mode.ShouldBe(GameMode.NetworkGame);
    }

    // ── Across the table ───────────────────────────────────────────

    [Fact]
    public void Confirm_AcrossTheTable_CompletesWithNoComputer()
    {
        var wizard = new StartupWizard(StartupWizardOptions.AcrossTheTable);

        wizard.Confirm(1); // "Across the table", right after Player vs Player

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.AcrossTheTable, Side.None, Side.White, Difficulty.Normal));
    }

    [Theory]
    [InlineData(0, GameMode.PlayerVsPlayer)]   // before the inserted item: unchanged
    [InlineData(2, GameMode.PlayerVsComputer)] // shifted +1 by the insert
    public void Confirm_AcrossTheTable_KeepsStandardEntriesAtShiftedIndices(int selected, GameMode expected)
    {
        var wizard = new StartupWizard(StartupWizardOptions.AcrossTheTable);

        wizard.Confirm(selected);

        wizard.Result.Mode.ShouldBe(expected);
        if (expected is GameMode.PlayerVsComputer)
            wizard.Current.Prompt.ShouldBe("Play as:"); // PvC moves to the PlayAs step
        else
            wizard.IsComplete.ShouldBeTrue();
    }

    // ── Play by Link ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, Side.Black)] // creator plays White → remote correspondent is Black
    [InlineData(1, Side.White)] // creator plays Black → remote correspondent is White
    public void Confirm_PlayByLink_PlayAsAssignsRemoteSide(int playAs, Side expectedRemote)
    {
        var wizard = new StartupWizard(StartupWizardOptions.LinkPlay);

        wizard.Confirm(3);
        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Play as:");

        wizard.Confirm(playAs);

        wizard.IsComplete.ShouldBeTrue();
        // Result.ComputerSide carries "the side NOT locally controlled" — the remote player. A remote
        // human has no strength setting, so link play completes at "Play as:" with no difficulty step.
        wizard.Result.ShouldBe((GameMode.PlayByLink, expectedRemote, Side.White, Difficulty.Normal));
    }

    [Fact]
    public void Confirm_IndexThree_WithoutLinkPlay_IsIgnored()
    {
        // A well-behaved 3-item menu never produces index 3; if something does, the wizard must
        // not fall into another flow (the old catch-all else routed any index ≥ 2 to Custom).
        var wizard = new StartupWizard();

        wizard.Confirm(3);

        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Select game mode:");
    }
}
