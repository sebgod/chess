using Chess.Lib;

namespace Chess.Console;

/// <summary>
/// Renders a centered startup menu in the terminal with arrow-key navigation
/// and returns the selected game mode.
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
        System.Console.CursorVisible = false;

        var mode = await ShowMenuAsync(
            "\u265A Chess \u2654",
            "Select game mode:",
            ["Player vs Player", "Player vs Computer"],
            cancellationToken);

        if (mode == 0)
        {
            return (GameMode.PlayerVsPlayer, Side.None);
        }

        var side = await ShowMenuAsync(
            "\u265A Chess \u2654",
            "Play as:",
            ["White", "Black"],
            cancellationToken);

        var computerSide = side == 0 ? Side.Black : Side.White;

        return (GameMode.PlayerVsComputer, computerSide);
    }

    private static async Task<int> ShowMenuAsync(
        string title, string prompt, string[] items, CancellationToken cancellationToken)
    {
        var selected = 0;
        DrawMenu(title, prompt, items, selected);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!System.Console.KeyAvailable)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            var key = System.Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = (selected - 1 + items.Length) % items.Length;
                    DrawMenu(title, prompt, items, selected);
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % items.Length;
                    DrawMenu(title, prompt, items, selected);
                    break;
                case ConsoleKey.Enter:
                    return selected;
                default:
                    // Also accept 1-based digit keys for quick selection
                    var digit = key.KeyChar - '1';
                    if (digit >= 0 && digit < items.Length)
                    {
                        return digit;
                    }
                    break;
            }
        }

        return 0;
    }

    private static void DrawMenu(string title, string prompt, string[] items, int selected)
    {
        var windowWidth = System.Console.WindowWidth;
        var windowHeight = System.Console.WindowHeight;

        // Total lines: title + blank + prompt + blank + items
        var totalLines = 4 + items.Length;
        var startRow = Math.Max(0, (windowHeight - totalLines) / 2);

        System.Console.Clear();

        WriteCenter(startRow, title, windowWidth);
        WriteCenter(startRow + 2, prompt, windowWidth);

        for (var i = 0; i < items.Length; i++)
        {
            var indicator = i == selected ? " \u25B6 " : "   ";
            var label = $"{indicator}{items[i]}";

            var row = startRow + 4 + i;
            if (i == selected)
            {
                WriteCenter(row, label, windowWidth, ConsoleColor.Black, ConsoleColor.White);
            }
            else
            {
                WriteCenter(row, label, windowWidth);
            }
        }
    }

    private static void WriteCenter(int row, string text, int windowWidth,
        ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        var col = Math.Max(0, (windowWidth - text.Length) / 2);
        System.Console.SetCursorPosition(col, row);

        if (foreground is { } fg && background is { } bg)
        {
            System.Console.ForegroundColor = fg;
            System.Console.BackgroundColor = bg;
            System.Console.Write(text);
            System.Console.ResetColor();
        }
        else
        {
            System.Console.Write(text);
        }
    }
}
