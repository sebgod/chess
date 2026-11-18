using Chess.Lib;
using Chess.Lib.UI;
using Shouldly;
using Xunit;

namespace Chess.Tests;

/// <summary>
/// Going back through the startup wizard. iPadOS has no back gesture, so on a touch-only iPad the wizard's own
/// "Back" row is the only way from a later step (side, difficulty, board) to the first; the hosts map Esc to
/// <see cref="StartupWizard.Back"/> as well. "Back" is appended LAST, so no index Confirm switches on moves.
/// </summary>
public sealed class StartupWizardBackTests
{
    [Fact]
    public void The_first_step_has_no_back()
    {
        var wizard = new StartupWizard();

        wizard.CanGoBack.ShouldBeFalse();
        wizard.Current.Items.ShouldNotContain(StartupWizard.BackItem);
        wizard.Back().ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Select game mode:");
    }

    [Fact]
    public void A_later_step_ends_with_back_and_keeps_its_own_items_first()
    {
        var wizard = new StartupWizard();
        wizard.Confirm(1); // Player vs Computer

        wizard.Current.Prompt.ShouldBe("Play as:");
        wizard.Current.Items.ShouldBe(["White", "Black", StartupWizard.BackItem]);
        wizard.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void Picking_the_back_row_returns_to_the_previous_step()
    {
        var wizard = new StartupWizard();
        wizard.Confirm(1); // Player vs Computer

        wizard.Confirm(wizard.Current.Items.Length - 1); // Back

        wizard.Current.Prompt.ShouldBe("Select game mode:");
        wizard.IsComplete.ShouldBeFalse();
        wizard.CanGoBack.ShouldBeFalse();
    }

    [Fact]
    public void Back_walks_every_step_in_turn_and_a_new_answer_takes_the_new_branch()
    {
        var wizard = new StartupWizard();
        wizard.Confirm(2); // Custom Game
        wizard.Confirm(1); // Standard Board
        wizard.Confirm(0); // White moves first
        wizard.Current.Prompt.ShouldBe("Play as:");

        wizard.Back().ShouldBeTrue();
        wizard.Current.Prompt.ShouldBe("Side to move first:");
        wizard.Back().ShouldBeTrue();
        wizard.Current.Prompt.ShouldBe("Starting board:");
        wizard.Back().ShouldBeTrue();
        wizard.Current.Prompt.ShouldBe("Select game mode:");

        wizard.Confirm(0); // Player vs Player this time
        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Mode.ShouldBe(GameMode.PlayerVsPlayer);
    }

    [Fact]
    public void Back_from_the_difficulty_step_then_finishing_gives_the_new_answers()
    {
        var wizard = new StartupWizard();
        wizard.Confirm(1); // Player vs Computer
        wizard.Confirm(0); // play White
        wizard.Current.Prompt.ShouldBe("Difficulty:");

        wizard.Back().ShouldBeTrue();
        wizard.Confirm(1); // play Black instead
        wizard.Confirm(0); // the first difficulty

        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Mode.ShouldBe(GameMode.PlayerVsComputer);
        wizard.Result.ComputerSide.ShouldBe(Side.White);
    }

    [Fact]
    public void With_a_header_back_a_later_step_has_no_back_row()
    {
        var wizard = new StartupWizard(StartupWizardOptions.HeaderBack);
        wizard.Confirm(1); // Player vs Computer

        wizard.Current.Prompt.ShouldBe("Play as:");
        wizard.Current.Items.ShouldBe(["White", "Black"]);
        wizard.CanGoBack.ShouldBeTrue();
    }

    [Fact]
    public void With_a_header_back_the_last_row_is_an_answer_and_Back_still_steps_back()
    {
        var wizard = new StartupWizard(StartupWizardOptions.HeaderBack);
        wizard.Confirm(1); // Player vs Computer

        wizard.Confirm(1); // Black, not Back
        wizard.Current.Prompt.ShouldBe("Difficulty:");

        wizard.Back().ShouldBeTrue();
        wizard.Current.Prompt.ShouldBe("Play as:");
    }

    [Fact]
    public void Reopen_returns_to_the_completing_step_with_its_back_and_the_same_answer_completes_again()
    {
        var wizard = new StartupWizard(StartupWizardOptions.OnlinePlay | StartupWizardOptions.HeaderBack);
        wizard.Confirm(wizard.Current.Items.IndexOf("Online game"));
        wizard.Confirm(0); // White
        wizard.IsComplete.ShouldBeTrue();
        wizard.CanGoBack.ShouldBeFalse();

        wizard.Reopen().ShouldBeTrue();

        wizard.IsComplete.ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Play as:");
        wizard.CanGoBack.ShouldBeTrue("the reopened step keeps its way back to the mode");
        wizard.Confirm(1); // Black this time
        wizard.IsComplete.ShouldBeTrue();
        wizard.Result.Mode.ShouldBe(GameMode.OnlineGame);
        wizard.Result.ComputerSide.ShouldBe(Side.White);
    }

    [Fact]
    public void Reopen_does_nothing_before_the_flow_is_complete()
    {
        var wizard = new StartupWizard();
        wizard.Reopen().ShouldBeFalse();
        wizard.Current.Prompt.ShouldBe("Select game mode:");
    }
}
