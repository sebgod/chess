using System.Text;

namespace Chess.Console;

/// <summary>
/// Represents a mouse button event with position and press/release state.
/// </summary>
internal readonly record struct MouseEvent(int Button, int X, int Y, bool IsRelease);

/// <summary>
/// Manages terminal lifecycle (alternate buffer, cursor, mouse tracking)
/// and provides platform-aware mouse input reading.
/// </summary>
internal sealed class ConsoleTerminal : IDisposable
{
    private int? _cellWidth;
    private int? _cellHeight;

    /// <summary>
    /// Queries the terminal cell size in pixels using XTWINOPS (CSI 16 t).
    /// Must be called before entering the alternate buffer to keep the response invisible.
    /// </summary>
    public async Task<(int Width, int Height)?> QueryCellSizeAsync()
    {
        if (_cellWidth.HasValue && _cellHeight.HasValue)
        {
            return (_cellWidth.Value, _cellHeight.Value);
        }

        var response = await GetControlSequenceResponseAsync("\e[16t");

        var tIndex = response.IndexOf('t');
        if (tIndex < 0)
        {
            return null;
        }

        var content = response[..tIndex];
        var parts = content.TrimStart('\e', '[').Split(';');
        if (parts.Length == 3 &&
            parts[0] == "6" &&
            int.TryParse(parts[1], out var height) &&
            int.TryParse(parts[2], out var width))
        {
            _cellWidth = width;
            _cellHeight = height;
            return (width, height);
        }

        return null;
    }

    /// <summary>
    /// Enters the alternate screen buffer, hides the cursor, and enables mouse tracking.
    /// </summary>
    public async ValueTask EnterAsync()
    {
        // cache cell size
        _ = await QueryCellSizeAsync();

        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.EnableMouseInput();
        }
        else
        {
            System.Console.Write("\e[?1006h"); // Enable SGR mouse tracking
        }

        System.Console.Write("\e[?1049h"); // Enter alternate buffer
        System.Console.Write("\e[?25l");   // Hide cursor
    }

    /// <summary>
    /// Returns true if there are pending input events to read.
    /// </summary>
    public bool HasInput() =>
        OperatingSystem.IsWindows() ? WindowsConsoleInput.HasInputEvents() : System.Console.KeyAvailable;

    /// <summary>
    /// Attempts to read a mouse event from the terminal input.
    /// </summary>
    public MouseEvent? TryReadMouseEvent()
    {
        var raw = OperatingSystem.IsWindows() ? WindowsConsoleInput.TryReadMouseEvent() : ParseVTMouseEvent();
        return raw is { } r ? new MouseEvent(r.Button, r.X, r.Y, r.IsRelease) : null;
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.RestoreConsoleMode();
        }
        else
        {
            System.Console.Write("\e[?1006l"); // Disable SGR mouse tracking
        }

        System.Console.Write("\e[?25h");   // Show cursor
        System.Console.Write("\e[?1049l"); // Leave alternate buffer
    }

    private static async ValueTask<string> GetControlSequenceResponseAsync(string sequence)
    {
        const int maxTries = 10;

        var response = new StringBuilder();
        System.Console.Write(sequence);

        var tries = 0;
        while (!System.Console.KeyAvailable && tries++ < maxTries)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        while (System.Console.KeyAvailable)
        {
            var key = System.Console.ReadKey(true);
            response.Append(key.KeyChar);
        }

        return response.ToString();
    }

    private static (int Button, int X, int Y, bool IsRelease)? ParseVTMouseEvent()
    {
        var sb = new StringBuilder();

        var first = System.Console.ReadKey(intercept: true);
        if (first.Key != ConsoleKey.Escape)
        {
            return null;
        }

        while (true)
        {
            if (!System.Console.KeyAvailable)
            {
                return null;
            }

            var ch = System.Console.ReadKey(intercept: true);
            if (ch.KeyChar is 'M' or 'm')
            {
                var isRelease = ch.KeyChar == 'm';
                var parts = sb.ToString().TrimStart('[', '<').Split(';');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out var button) &&
                    int.TryParse(parts[1], out var x) &&
                    int.TryParse(parts[2], out var y))
                {
                    // SGR coordinates are 1-based
                    return (button, x - 1, y - 1, isRelease);
                }
                return null;
            }

            sb.Append(ch);
        }
    }
}
