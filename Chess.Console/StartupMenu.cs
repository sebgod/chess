using Chess.Lib;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Renders a startup menu in the terminal with arrow-key navigation
/// and returns the selected game mode.
/// </summary>
internal class StartupMenu(IVirtualTerminal terminal, TimeProvider timeProvider)
{
    private int? _lastWindowWidth;
    private int? _lastWindowHeight;
    private uint? _cellHeight;

    /// <summary>
    /// Displays the game mode menu and waits for the user to make a selection.
    /// Returns the chosen game mode, the side the computer plays (<see cref="Side.None"/> for PvP),
    /// and whether to use the standard board (only relevant for <see cref="GameMode.CustomGame"/>).
    /// </summary>
    public async Task<(GameMode Mode, Side ComputerSide)> ShowAsync(CancellationToken cancellationToken)
    {
        _cellHeight = (await terminal.QueryCellSizeAsync())?.Height;
        return await ShowAsyncCore(cancellationToken);
    }

    private async Task<(GameMode Mode, Side ComputerSide)> ShowAsyncCore(CancellationToken cancellationToken)
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

    private async Task<int> ShowMenuAsync(
        string title, string prompt, string[] items, CancellationToken cancellationToken)
    {
        if (terminal.IsAlternateScreen)
        {
            return await ShowMenuAlternateAsync(title, prompt, items, cancellationToken);
        }

        System.Console.WriteLine();
        System.Console.WriteLine(prompt);
        for (var i = 0; i < items.Length; i++)
        {
            System.Console.WriteLine($"  {i + 1}) {items[i]}");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!terminal.HasInput())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeProvider, cancellationToken);
                continue;
            }

            var input = terminal.TryReadInput();

            var digit = input.Key - ConsoleKey.D1;
            if (digit >= 0 && digit < items.Length)
            {
                System.Console.WriteLine(items[digit]);
                return digit;
            }
        }

        return 0;
    }

    private async Task<int> ShowMenuAlternateAsync(
        string title, string prompt, string[] items, CancellationToken cancellationToken)
    {
        // Force a full redraw when entering a new menu (clears stale lines from previous menu)
        _lastWindowWidth = null;
        _lastWindowHeight = null;

        var selected = 0;
        var menuStartRow = DrawMenuAlternateScreen(title, prompt, items, selected);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!terminal.HasInput())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeProvider, cancellationToken);
                continue;
            }

            var input = terminal.TryReadInput();

            if (input.Mouse is { IsRelease: true } mouse)
            {
                var row = _cellHeight is > 0 ? mouse.Y / (int)_cellHeight.Value : mouse.Y;
                var clickedItem = row - menuStartRow;
                if (clickedItem >= 0 && clickedItem < items.Length)
                {
                    return clickedItem;
                }
                continue;
            }

            switch (input.Key)
            {
                case ConsoleKey.UpArrow:
                    selected = (selected - 1 + items.Length) % items.Length;
                    menuStartRow = DrawMenuAlternateScreen(title, prompt, items, selected);
                    break;
                case ConsoleKey.DownArrow:
                    selected = (selected + 1) % items.Length;
                    menuStartRow = DrawMenuAlternateScreen(title, prompt, items, selected);
                    break;
                case ConsoleKey.Enter:
                    return selected;
                default:
                    var digit = input.Key - ConsoleKey.D1;
                    if (digit >= 0 && digit < items.Length)
                    {
                        return digit;
                    }
                    break;
            }
        }

        return 0;
    }

    /// <returns>The row of the first menu item (for mouse hit testing).</returns>
    private int DrawMenuAlternateScreen(string title, string prompt, string[] items, int selected)
    {
        int windowWidth, windowHeight;
        try
        {
            windowWidth = System.Console.WindowWidth;
            windowHeight = System.Console.WindowHeight;
        }
        catch (IOException)
        {
            // No console available (e.g. redirected output) — compute layout but skip drawing
            var fallbackLines = 4 + items.Length;
            return Math.Max(0, (0 - fallbackLines) / 2) + 4;
        }

        // full redraw
        if (windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight)
        {
            System.Console.Clear();
        }

        _lastWindowWidth = windowWidth;
        _lastWindowHeight = windowHeight;

        // Total lines: title + blank + prompt + blank + items
        var totalLines = 4 + items.Length;
        var startRow = Math.Max(0, (windowHeight - totalLines) / 2);
        var menuStartRow = startRow + 4;

        WriteCenterPadded(startRow, title, windowWidth);
        WriteCenterPadded(startRow + 2, prompt, windowWidth);

        for (var i = 0; i < items.Length; i++)
        {
            var indicator = i == selected ? " \u25B6 " : "   ";
            var label = $"{indicator}{items[i]}";
            var row = menuStartRow + i;

            if (i == selected)
            {
                WriteCenterPadded(row, label, windowWidth, ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            }
            else
            {
                WriteCenterPadded(row, label, windowWidth);
            }
        }

        return menuStartRow;
    }

    /// <summary>
    /// Writes centered text padded to the full window width, erasing any stale content without a full clear.
    /// </summary>
    private static void WriteCenterPadded(int row, string text, int windowWidth,
        ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        var col = Math.Max(0, (windowWidth - text.Length) / 2);
        System.Console.SetCursorPosition(0, row);

        System.Console.Write(new string(' ', col));

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

        System.Console.Write(new string(' ', Math.Max(0, windowWidth - col - text.Length)));
    }
}
