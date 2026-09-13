using System.Text;
using Chess.Lib;
using Chess.UCI;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Takes a correspondence link off the terminal and decodes it — the console's answer to the GUI's
/// Ctrl+V, and to the browser's "open the URL".
///
/// <para><b>It reads the link as typed input, not from the clipboard, and that is the design rather
/// than a shortcut.</b> Console.Lib can WRITE the clipboard through OSC 52, which is how the reply
/// link gets out; the matching read escape exists but is disabled by default nearly everywhere and
/// unimplemented in Windows Terminal, for the obvious reason that it would let any program running
/// in your terminal siphon your clipboard. Pasting into a terminal, meanwhile, arrives as ordinary
/// input — so the portable way to receive a link is to let the terminal do what it already does.</para>
///
/// <para>Input is drained to exhaustion before each repaint. A link is a few hundred characters and a
/// paste delivers them in a burst; redrawing per character would repaint the screen three hundred
/// times and, on the Sixel display, cost an encode each time.</para>
/// </summary>
internal sealed class ConsoleLinkPrompt(IVirtualTerminal terminal, TimeProvider timeProvider)
    : MenuBase<Game?>(terminal, timeProvider)
{
    // Generous: GameLinkCodec caps a link at MaxPlies, and a 4096-ply game encodes far longer than
    // anyone will paste. This only exists so a runaway paste cannot grow the buffer without bound.
    private const int MaxLinkLength = 32 * 1024;

    private readonly TimeProvider _time = timeProvider;
    private string _lastContent = "";

    protected override async Task<Game?> ShowAsyncCore(CancellationToken cancellationToken)
    {
        var typed = new StringBuilder();
        string? error = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            Render(typed.Length, error);

            if (!Terminal.HasInput())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), _time, cancellationToken);
                continue;
            }

            // Drain everything waiting before rendering again — see the class summary.
            var done = false;
            while (Terminal.HasInput() && !done)
            {
                var input = Terminal.TryReadInput();
                switch (input.Key)
                {
                    case ConsoleKey.Enter:
                        done = true;
                        break;
                    case ConsoleKey.Escape:
                        return null;
                    case ConsoleKey.Backspace:
                        if (typed.Length > 0) typed.Length--;
                        break;
                    default:
                        // A link is printable ASCII plus whatever a URL carries; anything else in the
                        // stream is a control sequence the mapping did not recognise, and appending it
                        // would corrupt the link rather than extend it.
                        if (input.KeyChar is { } rune && !Rune.IsControl(rune) && typed.Length < MaxLinkLength)
                            typed.Append(rune.ToString());
                        break;
                }
            }

            if (!done) continue;

            var text = typed.ToString().Trim();
            if (text.Length == 0) return null; // Enter on an empty prompt is "never mind"

            switch (GameLinkCodec.TryDecode(text, out var game, out var why))
            {
                case GameLinkResult.Ok:
                    return game;
                case GameLinkResult.Invalid:
                    error = $"That link is not a valid game: {why}";
                    break;
                default:
                    error = "That doesn't look like a game link (it needs a '#g=' part).";
                    break;
            }

            // Keep what they pasted on screen only as a count; clearing it would make a typo mean
            // pasting the whole thing again, and showing it in full would wrap over the error.
            typed.Clear();
        }

        return null;
    }

    private void Render(int length, string? error)
    {
        var sb = new StringBuilder();
        sb.Append("♚ Play by Link ♔\n\n");
        sb.Append("Paste your opponent's link, then press Enter (Esc to cancel).\n");
        sb.Append("It can be the whole web address or just the '#g=...' part.\n\n");
        sb.Append(length == 0 ? "  (nothing pasted yet)\n" : $"  {length} characters pasted\n");
        if (error is not null) sb.Append('\n').Append("  ").Append(error).Append('\n');

        var content = sb.ToString();
        if (content == _lastContent) return;
        _lastContent = content;

        Terminal.Clear();
        Terminal.SetCursorPosition(0, 0);
        foreach (var line in content.Split('\n'))
            Terminal.WriteLine(line);
    }
}
