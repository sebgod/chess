namespace Console.Lib;

/// <summary>
/// Abstract base class for terminal menus with arrow-key navigation,
/// digit shortcuts, and mouse support in alternate screen mode.
/// </summary>
public abstract class MenuBase<T>(IVirtualTerminal terminal, TimeProvider timeProvider)
{
    private static readonly VtStyle SelectedStyle = new(SgrColor.BrightYellow, SgrColor.Blue);

    private int? _lastWindowWidth;
    private int? _lastWindowHeight;
    private readonly byte _cellHeight = terminal.CellSize.Height;

    protected IVirtualTerminal Terminal => terminal;

    public async Task<T> ShowAsync(CancellationToken cancellationToken)
    {
        return await ShowAsyncCore(cancellationToken);
    }

    protected abstract Task<T> ShowAsyncCore(CancellationToken cancellationToken);

    protected async Task<int> ShowMenuAsync(
        string title, string prompt, string[] items, CancellationToken cancellationToken)
    {
        if (terminal.IsAlternateScreen)
        {
            return await ShowMenuAlternateAsync(title, prompt, items, cancellationToken);
        }

        terminal.WriteLine();
        terminal.WriteLine(prompt);
        for (var i = 0; i < items.Length; i++)
        {
            terminal.WriteLine($"  {i + 1}) {items[i]}");
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
                terminal.WriteLine(items[digit]);
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
                // Check for resize while idle
                var (w, h) = terminal.Size;
                if (w != _lastWindowWidth || h != _lastWindowHeight)
                {
                    menuStartRow = DrawMenuAlternateScreen(title, prompt, items, selected);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), timeProvider, cancellationToken);
                continue;
            }

            var input = terminal.TryReadInput();

            if (input.Mouse is { IsRelease: true } mouse)
            {
                var row = _cellHeight is > 0 ? mouse.Y / _cellHeight : mouse.Y;
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
        var (windowWidth, windowHeight) = terminal.Size;

        // Total lines: title + blank + prompt + blank + items
        var totalLines = 4 + items.Length;
        var startRow = Math.Max(0, (windowHeight - totalLines) / 2);
        var menuStartRow = startRow + 4;

        // full redraw
        if (windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight)
        {
            terminal.Clear();
        }

        WriteCenterPadded(startRow, title, windowWidth);
        WriteCenterPadded(startRow + 2, prompt, windowWidth);

        for (var i = 0; i < items.Length; i++)
        {
            var indicator = i == selected ? " \u25B6 " : "   ";
            var label = $"{indicator}{items[i]}";
            var row = menuStartRow + i;

            WriteCenterPadded(row, label, windowWidth, i == selected ? SelectedStyle : null);
        }

        _lastWindowWidth = windowWidth;
        _lastWindowHeight = windowHeight;

        return menuStartRow;
    }

    /// <summary>
    /// Writes centered text padded to the full window width, erasing any stale content without a full clear.
    /// </summary>
    private void WriteCenterPadded(int row, string text, int windowWidth, VtStyle? style = null)
    {
        var col = Math.Max(0, (windowWidth - text.Length) / 2);
        terminal.SetCursorPosition(0, row);

        terminal.Write(new string(' ', col));

        if (style is { } s)
        {
            terminal.Write($"{s}{text}{VtStyle.Reset}");
        }
        else
        {
            terminal.Write(text);
        }

        terminal.Write(new string(' ', Math.Max(0, windowWidth - col - text.Length)));
    }
}
