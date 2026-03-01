using Chess.Lib;

namespace Chess.Console;

/// <summary>
/// Renders a simple startup menu in the terminal and returns the selected game mode.
/// </summary>
internal static class StartupMenu
{
    /// <summary>
    /// Displays the game mode menu and waits for the user to make a selection.
    /// Returns the chosen game mode and the side the computer plays (<see cref="Side.None"/> for PvP).
    /// </summary>
    public static async Task<(GameMode Mode, Side ComputerSide)> ShowAsync(CancellationToken cancellationToken)
    {
        System.Console.Clear();
        System.Console.CursorVisible = true;

        System.Console.WriteLine();
        System.Console.WriteLine("  \u265A Chess \u2654");
        System.Console.WriteLine();
        System.Console.WriteLine("  Select game mode:");
        System.Console.WriteLine();
        System.Console.WriteLine("  1. Player vs Player");
        System.Console.WriteLine("  2. Player vs Computer");
        System.Console.WriteLine();
        System.Console.Write("  > ");

        var mode = await ReadChoiceAsync(['1', '2'], cancellationToken);
        if (mode == '1')
        {
            return (GameMode.PlayerVsPlayer, Side.None);
        }

        System.Console.WriteLine();
        System.Console.WriteLine();
        System.Console.WriteLine("  Play as:");
        System.Console.WriteLine();
        System.Console.WriteLine("  1. White");
        System.Console.WriteLine("  2. Black");
        System.Console.WriteLine();
        System.Console.Write("  > ");

        var side = await ReadChoiceAsync(['1', '2'], cancellationToken);
        var computerSide = side == '1' ? Side.Black : Side.White;

        return (GameMode.PlayerVsComputer, computerSide);
    }

    private static async Task<char> ReadChoiceAsync(char[] validKeys, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!System.Console.KeyAvailable)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            var key = System.Console.ReadKey(intercept: true);
            if (Array.IndexOf(validKeys, key.KeyChar) >= 0)
            {
                return key.KeyChar;
            }
        }

        return validKeys[0];
    }
}
