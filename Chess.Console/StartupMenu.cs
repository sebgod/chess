using Chess.Lib;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Renders a startup menu in the terminal with arrow-key navigation
/// and returns the selected game mode.
/// </summary>
internal class StartupMenu(IVirtualTerminal terminal, TimeProvider timeProvider)
    : MenuBase<(GameMode Mode, Side ComputerSide)>(terminal, timeProvider)
{
    protected override async Task<(GameMode Mode, Side ComputerSide)> ShowAsyncCore(CancellationToken cancellationToken)
    {
        var mode = await ShowMenuAsync(
            "\u265A Chess \u2654",
            "Select game mode:",
            ["Player vs Player", "Player vs Computer", "Custom Game"],
            cancellationToken);

        if (mode == 0)
        {
            return (GameMode.PlayerVsPlayer, Side.None);
        }

        if (mode == 1)
        {
            var side = await ShowMenuAsync(
                "\u265A Chess \u2654",
                "Play as:",
                ["White", "Black"],
                cancellationToken);

            var computerSide = side == 0 ? Side.Black : Side.White;
            return (GameMode.PlayerVsComputer, computerSide);
        }

        // Custom Game
        var boardChoice = await ShowMenuAsync(
            "\u265A Chess \u2654",
            "Starting board:",
            ["Empty Board", "Standard Board"],
            cancellationToken);

        var customSide = await ShowMenuAsync(
            "\u265A Chess \u2654",
            "Play as:",
            ["White", "Black"],
            cancellationToken);

        var customComputerSide = customSide == 0 ? Side.Black : Side.White;
        return (boardChoice is 1 ? GameMode.CustomGameStandardBoard : GameMode.CustomGameEmpty, customComputerSide);
    }
}
