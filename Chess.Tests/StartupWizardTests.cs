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
        var wizard = new StartupWizard(includeLinkPlay: true);

        wizard.Current.Items.ShouldBe(
            ["Player vs Player", "Player vs Computer", "Custom Game", "Play by Link"]);
    }

    // ── Existing flows unchanged (regression for the explicit-index rewrite) ──

    [Fact]
    public void Confirm_PlayerVsPlayer_CompletesWithNoComputer()
    {
        var wizard = new StartupWizard();

        wizard.Confirm(0);

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.PlayerVsPlayer, Side.None, Side.White));
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

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.PlayerVsComputer, expectedComputer, Side.White));
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

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.ShouldBe((GameMode.CustomGameStandardBoard, Side.Black, Side.Black));
    }

    // ── Play by Link ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, Side.Black)] // creator plays White → remote correspondent is Black
    [InlineData(1, Side.White)] // creator plays Black → remote correspondent is White
    public void Confirm_PlayByLink_PlayAsAssignsRemoteSide(int playAs, Side expectedRemote)
    {
        var wizard = new StartupWizard(includeLinkPlay: true);

        wizard.Confirm(3);
        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Play as:");

        wizard.Confirm(playAs);

        wizard.IsComplete.ShouldBeTrue();
        // Result.ComputerSide carries "the side NOT locally controlled" — the remote player.
        wizard.Result.ShouldBe((GameMode.PlayByLink, expectedRemote, Side.White));
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
