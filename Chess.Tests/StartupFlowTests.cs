using Chess.Lib;
using Chess.Lib.UI;
using Shouldly;
using Xunit;
using File = Chess.Lib.File;

namespace Chess.Tests;

/// <summary>
/// Logic behind the Android startup menu: the shared <see cref="StartupWizard"/> flow (which
/// Chess.Droid drives via PixelMenuWidget) and the in-process <see cref="AiEngine"/> reply that
/// powers Player-vs-Computer there (no engine child process on Android). Renderer-free, so they run
/// anywhere — the on-device screenshot is nice-to-have, but these pin the behaviour.
/// </summary>
public sealed class StartupFlowTests
{
    [Fact]
    public void Wizard_PlayerVsPlayer_completes_immediately_with_no_computer()
    {
        var wizard = new StartupWizard();
        wizard.Current.Items.ShouldContain("Player vs Player");

        wizard.Confirm(0); // Player vs Player

        wizard.IsComplete.ShouldBeTrue();
        var (mode, computerSide, _) = wizard.Result;
        mode.ShouldBe(GameMode.PlayerVsPlayer);
        computerSide.ShouldBe(Side.None); // hot-seat: no opponent process
    }

    [Fact]
    public void Wizard_PlayerVsComputer_asks_side_then_assigns_the_engine_the_other_colour()
    {
        var wizard = new StartupWizard();

        wizard.Confirm(1); // Player vs Computer
        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Play as:");

        wizard.Confirm(0); // human plays White

        wizard.IsComplete.ShouldBeTrue();
        var (mode, computerSide, _) = wizard.Result;
        mode.ShouldBe(GameMode.PlayerVsComputer);
        computerSide.ShouldBe(Side.Black); // human White => engine is Black
    }

    [Fact]
    public void Wizard_Continue_is_prepended_and_does_not_shift_the_standard_entries()
    {
        // Chess.Droid offers "Continue game" when an unfinished save exists (back button returns to
        // the menu mid-game) — it must NOT shift the standard entries' behavior (order is
        // load-bearing; Confirm normalizes the index).
        var wizard = new StartupWizard(includeContinue: true);
        wizard.Current.Items[0].ShouldBe("Continue game");

        wizard.Confirm(0);
        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Mode.ShouldBe(GameMode.Continue);

        var shifted = new StartupWizard(includeContinue: true);
        shifted.Confirm(2); // "Player vs Computer", one below its base index
        shifted.IsComplete.ShouldBeFalse();
        shifted.Current.Prompt.ShouldBe("Play as:"); // the PvC side question
    }

    [Fact]
    public void Ai_picks_a_legal_opening_move()
    {
        var game = new Game();

        var move = new AiEngine(game.CurrentSide, maxDepth: 3).PickMove(game);

        move.ShouldNotBeNull();
        game.TryMove(move!.Value).IsMoveOrCapture().ShouldBeTrue();
    }

    [Fact]
    public void Ai_replies_as_black_after_a_human_opening()
    {
        // Mirrors the Chess.Droid PvC loop: human moves, then the engine (Black) replies in-process.
        var game = new Game();
        game.TryMove(new Position(File.E, Rank.R2), new Position(File.E, Rank.R4)).IsMoveOrCapture().ShouldBeTrue();

        game.CurrentSide.ShouldBe(Side.Black);
        var reply = new AiEngine(game.CurrentSide, maxDepth: 3).PickMove(game);

        reply.ShouldNotBeNull();
        game.TryMove(reply!.Value).IsMoveOrCapture().ShouldBeTrue();
        game.CurrentSide.ShouldBe(Side.White); // turn handed back to the human
    }
}
