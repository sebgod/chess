using Chess.Lib;
using Console.Lib;
using Shouldly;
using Xunit;

namespace Chess.Console.Tests;

/// <summary>
/// The TUI's startup wizard has no "Back" row (StartupWizardOptions.HeaderBack): a later step's way back is the panel's
/// "&lt; Back" control, Esc and Backspace, which MenuBase maps to Back on a step that has one (Backspace since
/// Console.Lib 5.33).
/// </summary>
public class StartupMenuTests
{
    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    private static async Task<(GameMode Mode, Side ComputerSide, Side SideToMove, Difficulty Difficulty)> RunAsync(
        params ConsoleKey[] keys)
    {
        var inputs = new Queue<ConsoleInputEvent>(keys.Select(Key));
        var menu = new StartupMenu(new TestableTerminal(inputs), TimeProvider.System);
        return await menu.ShowAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.Backspace)]
    public async Task Esc_or_Backspace_on_a_later_step_goes_back_to_the_first(ConsoleKey back)
    {
        // Player vs Computer, then back. Had the key not gone back, "3" would answer the colour step (and fail, it has
        // two rows), and no custom game would come out.
        var result = await RunAsync(
            ConsoleKey.D2, back,
            ConsoleKey.D3, // Custom Game
            ConsoleKey.D2, // Standard Board
            ConsoleKey.D1, // White moves first
            ConsoleKey.D1, // play White
            ConsoleKey.D1); // Easy

        result.Mode.ShouldBe(GameMode.CustomGameStandardBoard);
    }

    [Fact]
    public async Task A_later_step_has_no_back_row()
    {
        // Player vs Computer, then "2": with no "Back" row the second row is Black, not Back.
        var result = await RunAsync(ConsoleKey.D2, ConsoleKey.D2, ConsoleKey.D1);

        result.Mode.ShouldBe(GameMode.PlayerVsComputer);
        result.ComputerSide.ShouldBe(Side.White);
    }
}
