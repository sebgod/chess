using System.Text;

namespace Chess.Console;

/// <summary>
/// Represents a mouse button event with pixel position and press/release state.
/// </summary>
public readonly record struct MouseEvent(int Button, int X, int Y, bool IsRelease);

/// <summary>
/// Represents a console input event: either a mouse event, a key press, or both with modifier state.
/// </summary>
public readonly record struct ConsoleInputEvent(MouseEvent? Mouse, ConsoleKey Key, ConsoleModifiers Modifiers);

public interface IConsoleTerminal
    : IAsyncDisposable
{
    Task<bool> HasSixelSupportAsync();
    Task<(uint Width, uint Height)?> QueryCellSizeAsync();
    ValueTask EnterAsync();
    bool IsAlternateScreen { get; }
    bool HasInput();
    ConsoleInputEvent TryReadInput();
}

/// <summary>
/// Manages terminal lifecycle (alternate buffer, cursor, mouse tracking)
/// and provides platform-aware mouse input reading.
/// </summary>
internal sealed class ConsoleTerminal : IConsoleTerminal
{
    private HashSet<TerminalCapability>? _deviceCapabilities;
    private uint? _cellWidth;
    private uint? _cellHeight;
    private bool _alternateScreen;
    private Stream? _stdIn;

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

        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.EnableVirtualTerminalIO();
        }

        System.Console.Write("\e[?1049h"); // Enter alternate buffer
        System.Console.Write("\e[?25l");   // Hide cursor
        System.Console.Write("\e[?1000h"); // SGR Mouse press/release tracking
        System.Console.Write("\e[?1006h"); // SGR extended tracking
        System.Console.Out.Flush(); // Ensure all control sequences are sent before we start reading input


        _stdIn = System.Console.OpenStandardInput();
        _alternateScreen = true;
    }

    public bool IsAlternateScreen => _alternateScreen;

    public bool HasInput() => System.Console.KeyAvailable;

    /// <summary>
    /// Attempts to read input from the terminal.
    /// Returns a mouse event if mouse input was received, or a raw key character if keyboard input was received.
    /// Mouse input takes precedence; both may be null if the consumed input was neither.
    /// </summary>
    public ConsoleInputEvent TryReadInput()
    {
        // only in alternate screen we enabled SGR mouse tracking, so we only attempt to parse it there
        if (_alternateScreen)
        {
            var result = ParseSgrInput();

            if (result.Mouse is not { } r || !_cellWidth.HasValue || !_cellHeight.HasValue)
            {
                return result;
            }

            // Normalize cell coordinates to pixels
            return new(new MouseEvent(r.Button, r.X * (int)_cellWidth.Value, r.Y * (int)_cellHeight.Value, r.IsRelease), ConsoleKey.None, result.Modifiers);
        }
        else
        {
            var first = System.Console.ReadKey(intercept: false);

            if (first.Key == ConsoleKey.F1)
            {
                return new(null, ConsoleKey.None, first.Modifiers);
            }
            else if (first.Key != ConsoleKey.Escape)
            {
                return new(null, first.Key, first.Modifiers);
            }
            else
            {
                return new(null, ConsoleKey.None, first.Modifiers);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        System.Console.Write("\e[?1000l"); // Disable SGR Mouse press/release tracking
        System.Console.Write("\e[?1006l"); // Disable SGR extended tracking

        if (OperatingSystem.IsWindows())
        {
            WindowsConsoleInput.RestoreConsoleMode();
        }

        System.Console.Write("\e[?25h");   // Show cursor
        System.Console.Write("\e[?1049l"); // Leave alternate buffer

        if (_stdIn is { } stdIn)
        {
            return stdIn.DisposeAsync();
        }
        return ValueTask.CompletedTask;
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
    /// Parses an SGR mouse event (CSI &lt; Pb;Px;Py M/m), or returns the raw key character
    /// if the input was not an escape sequence.
    /// </summary>
    private ConsoleInputEvent ParseSgrInput()
    {
        if (_stdIn is not { })
        {
            throw new InvalidOperationException("Standard input stream is not available.");
        }

        var sb = new StringBuilder();

        var @byte = _stdIn.ReadByte(); // Consume the initial ESC

        if (@byte == -1)
        {
            return default;
        }
        else if (@byte != '\e')
        {
            var (key, modifiers) = ByteToConsoleKey(@byte);
            return new(null, key, modifiers);
        }

        while (System.Console.KeyAvailable)
        {
            var ch = _stdIn.ReadByte();
            if (ch == -1)
            {
                return default;
            }

            // SGR mouse terminator: M (press) or m (release) — don't append, params are already in sb
            if (ch is 'M' or 'm')
            {
                var isRelease = ch == 'm';
                var parts = sb.ToString().TrimStart('[', '<').Split(';');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out var pb) &&
                    int.TryParse(parts[1], out var x) &&
                    int.TryParse(parts[2], out var y))
                {
                    // Pb encodes button in bits 0-1, modifiers in bits 2-4
                    var button = pb & 0x03;
                    var modifiers = (ConsoleModifiers)0;
                    if ((pb & 0x04) != 0) modifiers |= ConsoleModifiers.Shift;
                    if ((pb & 0x08) != 0) modifiers |= ConsoleModifiers.Alt;
                    if ((pb & 0x10) != 0) modifiers |= ConsoleModifiers.Control;
                    // SGR coordinates are 1-based
                    return new(new MouseEvent(button, x - 1, y - 1, isRelease), ConsoleKey.None, modifiers);
                }
                return default;
            }

            sb.Append((char)ch);

            if (sb[0] == '[' && TryParseCsiKey(sb, out var csiKey, out var csiMods))
            {
                return new(null, csiKey, csiMods);
            }
        }

        return default;
    }

    /// <summary>
    /// Converts a raw stdin byte to a <see cref="ConsoleKey"/> with <see cref="ConsoleModifiers"/>.
    /// Uppercase letters produce Shift, Ctrl+letter (0x01-0x1A) produces Control.
    /// </summary>
    /// <summary>
    /// Tries to parse a CSI sequence from the buffer (including final byte as last char).
    /// Buffer format: [ params final — e.g. "[A", "[1;5A", "[3~", "[3;5~".
    /// Modifier parameter: 2=Shift, 3=Alt, 4=Shift+Alt, 5=Ctrl, 6=Ctrl+Shift, 7=Ctrl+Alt, 8=Ctrl+Shift+Alt.
    /// </summary>
    private static bool TryParseCsiKey(StringBuilder sb, out ConsoleKey key, out ConsoleModifiers modifiers)
    {
        key = ConsoleKey.None;
        modifiers = ConsoleModifiers.None;

        if (sb.Length < 2)
            return false;

        var final = sb[^1];
        var param = sb.ToString().AsSpan(1, sb.Length - 2); // between '[' and final byte

        // Extract optional modifier after ';': e.g. "1;5" → n=1, mod=5
        var semiPos = param.IndexOf(';');
        if (semiPos >= 0 && int.TryParse(param[(semiPos + 1)..], out var mod))
        {
            if ((mod - 1 & 1) != 0) modifiers |= ConsoleModifiers.Shift;
            if ((mod - 1 & 2) != 0) modifiers |= ConsoleModifiers.Alt;
            if ((mod - 1 & 4) != 0) modifiers |= ConsoleModifiers.Control;
            param = param[..semiPos];
        }

        // Letter final byte: arrow keys, Home, End
        if (final is >= 'A' and <= 'D' or 'H' or 'F')
        {
            key = final switch
            {
                'A' => ConsoleKey.UpArrow,
                'B' => ConsoleKey.DownArrow,
                'C' => ConsoleKey.RightArrow,
                'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home,
                _ => ConsoleKey.End,
            };
            return true;
        }

        // Tilde final byte: ESC [ n ~ or ESC [ n;mod ~
        if (final == '~' && int.TryParse(param, out var n))
        {
            key = n switch
            {
                1 => ConsoleKey.Home,
                2 => ConsoleKey.Insert,
                3 => ConsoleKey.Delete,
                4 => ConsoleKey.End,
                5 => ConsoleKey.PageUp,
                6 => ConsoleKey.PageDown,
                15 => ConsoleKey.F5,
                17 => ConsoleKey.F6,
                18 => ConsoleKey.F7,
                19 => ConsoleKey.F8,
                20 => ConsoleKey.F9,
                21 => ConsoleKey.F10,
                23 => ConsoleKey.F11,
                24 => ConsoleKey.F12,
                _ => ConsoleKey.None,
            };
            return key != ConsoleKey.None;
        }

        return false;
    }

    private static (ConsoleKey Key, ConsoleModifiers Modifiers) ByteToConsoleKey(int b) => b switch
    {
        >= 'a' and <= 'z' => ((ConsoleKey)(b - 'a' + 'A'), 0),
        >= 'A' and <= 'Z' => ((ConsoleKey)b, ConsoleModifiers.Shift),
        >= '0' and <= '9' => ((ConsoleKey)b, 0),
        '\b' => (ConsoleKey.Backspace, 0),
        '\t' => (ConsoleKey.Tab, 0),
        '\r' or '\n' => (ConsoleKey.Enter, 0),
        ' ' => (ConsoleKey.Spacebar, 0),
        0x7F => (ConsoleKey.Delete, 0),
        >= 0x01 and <= 0x1A => ((ConsoleKey)(b - 0x01 + 'A'), ConsoleModifiers.Control),
        _ => (ConsoleKey.None, 0),
    };
}
