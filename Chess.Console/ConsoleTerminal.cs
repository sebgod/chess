using System.Text;

namespace Chess.Console;

/// <summary>
/// Represents a mouse button event with pixel position and press/release state.
/// </summary>
internal readonly record struct MouseEvent(int Button, int X, int Y, bool IsRelease);

/// <summary>
/// Manages terminal lifecycle (alternate buffer, cursor, mouse tracking)
/// and provides platform-aware mouse input reading.
/// </summary>
internal sealed class ConsoleTerminal : IDisposable
{
    private HashSet<TerminalCapability>? _deviceCapabilities;
    private uint? _cellWidth;
    private uint? _cellHeight;
    private bool _useDecLocator;
    private bool _alternateScreen;

    public async Task<bool> HasSixelSupportAsync()
    {
        if (_deviceCapabilities is null)
        {
            var response = await GetControlSequenceResponseAsync("\e[0c");

            _deviceCapabilities = [.. response
                    .TrimStart('\e', '[', '?')
                    .TrimEnd('c')
                    .Split(';')
                    .Select((s) => (TerminalCapability) int.Parse(s))
            ];
        }

        return _deviceCapabilities.Contains(TerminalCapability.Sixel);
    }

    /// <summary>
    /// Queries the terminal cell size in pixels using XTWINOPS (CSI 16 t).
    /// Must be called before entering the alternate buffer to keep the response invisible.
    /// </summary>
    public async Task<(uint Width, uint Height)?> QueryCellSizeAsync()
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
            uint.TryParse(parts[1], out var height) &&
            uint.TryParse(parts[2], out var width))
        {
            _cellWidth = width;
            _cellHeight = height;
            return (width, height);
        }

        return null;
    }

    /// <summary>
    /// Enters the alternate screen buffer, hides the cursor, and enables mouse tracking.
    /// Prefers DEC Locator in pixel mode when available, falling back to
    /// Win32 mouse input (Windows) or SGR mouse tracking (other platforms).
    /// </summary>
    public async ValueTask EnterAsync()
    {
        // cache cell size
        _ = await QueryCellSizeAsync();

        var decLocatorAvailable = await ProbeDecLocatorAsync();
        if (decLocatorAvailable && (!OperatingSystem.IsWindows() || WindowsConsoleInput.EnableVirtualTerminalInput()))
        {
            _useDecLocator = true;
            System.Console.Write("\e[1;1'z");  // DECELR: enable locator, pixel coordinates
            System.Console.Write("\e[1;3'{");  // DECSLE: report button down + up
        }
        else if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.EnableMouseInput();
        }
        else
        {
            System.Console.Write("\e[?1006h"); // SGR mouse tracking fallback
        }

        System.Console.Write("\e[?1049h"); // Enter alternate buffer
        System.Console.Write("\e[?25l");   // Hide cursor

        _alternateScreen = true;
    }

    public bool IsAlternateScreen => _alternateScreen;

    /// <summary>
    /// Returns true if there are pending input events to read.
    /// </summary>
    public bool HasInput() =>
        _alternateScreen && OperatingSystem.IsWindows() && !_useDecLocator
            ? WindowsConsoleInput.HasInputEvents()
            : System.Console.KeyAvailable;

    /// <summary>
    /// Attempts to read input from the terminal.
    /// Returns a mouse event if mouse input was received, or a raw key character if keyboard input was received.
    /// Mouse input takes precedence; both may be null if the consumed input was neither.
    /// </summary>
    public (MouseEvent? Mouse, ConsoleKey Key) TryReadInput()
    {
        if (_alternateScreen)
        {
            if (_useDecLocator)
            {
                return ParseDecLocatorInput();
            }

            var (rawMouse, key) = OperatingSystem.IsWindows()
                ? WindowsConsoleInput.TryReadInputEvent()
                : ParseSgrInput();

            if (rawMouse is not { } r || !_cellWidth.HasValue || !_cellHeight.HasValue)
            {
                return (null, key);
            }

            // Normalize cell coordinates to pixels
            return (new MouseEvent(r.Button, r.X * (int)_cellWidth.Value, r.Y * (int)_cellHeight.Value, r.IsRelease), ConsoleKey.None);
        }
        else
        {
            var first = System.Console.ReadKey(intercept: false);

            if (first.Key == ConsoleKey.F1)
            {
                return (null, ConsoleKey.None);
            }
            else if (first.Key != ConsoleKey.Escape)
            {
                return (null, first.Key);
            }
            else
            {
                return (null, ConsoleKey.None);
            }
        }
    }

    public void Dispose()
    {
        if (_useDecLocator)
        {
            System.Console.Write("\e[0'z"); // DECELR: disable locator
        }
        else if (!OperatingSystem.IsWindows())
        {
            System.Console.Write("\e[?1006l"); // Disable SGR mouse tracking
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.RestoreConsoleMode();
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

    /// <summary>
    /// Probes whether the terminal supports DEC Locator by sending DECRQLP and checking for a DECLRP response.
    /// </summary>
    private static async Task<bool> ProbeDecLocatorAsync()
    {
        var response = await GetControlSequenceResponseAsync("\e[1'|");
        if (!response.Contains("&w"))
        {
            return false;
        }

        var content = response.TrimStart('\e', '[');
        var ampIndex = content.IndexOf('&');
        if (ampIndex < 0)
        {
            return false;
        }

        var parts = content[..ampIndex].Split(';');
        // Pe=0 means locator unavailable
        return parts.Length >= 1 && int.TryParse(parts[0], out var pe) && pe != 0;
    }

    /// <summary>
    /// Parses a DECLRP response (CSI Pe;Pb;Pr;Pc;Pp &amp; w) into a mouse event with pixel coordinates,
    /// or returns the raw key character if the input was not an escape sequence.
    /// </summary>
    private static (MouseEvent? Mouse, ConsoleKey Key) ParseDecLocatorInput()
    {
        var first = System.Console.ReadKey(intercept: true);
        if (first.Key != ConsoleKey.Escape)
        {
            // Map F1 to '?'
            return (null, first.Key);
        }

        var sb = new StringBuilder();
        while (true)
        {
            if (!System.Console.KeyAvailable)
            {
                return (null, ConsoleKey.None);
            }

            var ch = System.Console.ReadKey(intercept: true);
            if (ch.KeyChar == '&')
            {
                if (!System.Console.KeyAvailable)
                {
                    return (null, ConsoleKey.None);
                }

                var next = System.Console.ReadKey(intercept: true);
                if (next.KeyChar != 'w')
                {
                    return (null, ConsoleKey.None);
                }

                var parts = sb.ToString().TrimStart('[').Split(';');
                if (parts.Length >= 4 &&
                    int.TryParse(parts[0], out var pe) &&
                    int.TryParse(parts[2], out var pr) &&
                    int.TryParse(parts[3], out var pc))
                {
                    // Pe: 2=left down, 3=left up, 4=middle down, 5=middle up, 6=right down, 7=right up
                    var button = pe switch
                    {
                        2 or 3 => 0,
                        4 or 5 => 1,
                        6 or 7 => 2,
                        _ => -1
                    };
                    if (button < 0)
                    {
                        return (null, ConsoleKey.None);
                    }

                    var isRelease = pe is 3 or 5 or 7;
                    // DECLRP pixel coordinates are 1-based
                    return (new MouseEvent(button, pc - 1, pr - 1, isRelease), ConsoleKey.None);
                }

                return (null, ConsoleKey.None);
            }

            sb.Append(ch.KeyChar);
        }
    }

    /// <summary>
    /// Parses an SGR mouse event (CSI &lt; Pb;Px;Py M/m), or returns the raw key character
    /// if the input was not an escape sequence.
    /// </summary>
    private static ((int Button, int X, int Y, bool IsRelease)? Mouse, ConsoleKey Key) ParseSgrInput()
    {
        var sb = new StringBuilder();

        var first = System.Console.ReadKey(intercept: true);
        if (first.Key != ConsoleKey.Escape)
        {
            // Map F1 to '?'
            return (null, first.Key);
        }

        while (true)
        {
            if (!System.Console.KeyAvailable)
            {
                return (null, ConsoleKey.None);
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
                    return ((button, x - 1, y - 1, isRelease), ConsoleKey.None);
                }
                return (null, ConsoleKey.None);
            }

            sb.Append(ch);
        }
    }
}
