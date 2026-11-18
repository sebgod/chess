using System.Text;
using Chess.Lib;
using Chess.Net;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Terminal LAN lobby: name entry, then a live, non-blocking peer list with invite/accept, built on
/// the same <c>HasInput()/TryReadInput()</c> poll primitives the game loop and <see cref="MenuBase{T}"/>
/// use — so invites that arrive while browsing surface without the user pressing anything. Owns the
/// Chess.Net stack for its lifetime and returns the connected <see cref="NetworkSession"/> (or null if
/// the user backs out). The desktop GUI's <c>VkNetworkLobby</c> is the pixel equivalent.
/// </summary>
internal sealed class ConsoleLanLobby : MenuBase<NetworkSession?>
{
    private const int MaxNameLength = 24;

    private readonly TimeProvider _time;
    private readonly string _saveDir;
    private readonly Side _preferredColor;
    private string _lastContent = "";
    // The card's shape at the last backdrop redraw: terminal size, widest line and line count (see RenderList).
    private string _lastShape = "";
    // The board behind the lobby's panel (#84), redrawn whenever the card changes; null draws none.
    private readonly System.Action? _drawBackdrop;
    // The card on the alternate screen, built on first use.
    private MenuWidget? _panel;

    public ConsoleLanLobby(IVirtualTerminal terminal, TimeProvider timeProvider, string saveDir, Side preferredColor,
        System.Action? drawBackdrop = null)
        : base(terminal, timeProvider)
    {
        _drawBackdrop = drawBackdrop;
        _time = timeProvider;
        _saveDir = saveDir;
        _preferredColor = preferredColor;
    }

    protected override async Task<NetworkSession?> ShowAsyncCore(CancellationToken cancellationToken)
    {
        var profile = LanProfile.Load(_saveDir);

        var name = await PromptNameAsync(profile.Name, cancellationToken);
        if (name is null) return null; // cancelled
        new LanProfile(name).Save(_saveDir);

        await using var lan = new LanPlayStack(name, _preferredColor, _time);
        var lobby = lan.Lobby;
        lobby.Start();

        var selected = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (lobby.State == LobbyState.Connected)
                return lobby.Session;

            var (header, items) = BuildView(lobby, name);
            if (selected >= items.Count) selected = Math.Max(0, items.Count - 1);
            RenderList(header, items, selected, EscapeAction(lobby.State));

            if (!Terminal.HasInput())
            {
                OnIdle();
                await Task.Delay(TimeSpan.FromMilliseconds(100), _time, cancellationToken);
                continue;
            }

            var input = Terminal.TryReadInput();

            // F1 opens the key list on the card; while it is open F1 or Esc close it and every other key is swallowed
            // (MenuModel), so Esc closes the list here rather than leaving the lobby.
            if (_panel is not null && Terminal.IsAlternateScreen && (input.Key == ConsoleKey.F1 || _panel.ShowingHelp))
            {
                var (helpKey, helpMods) = input.ToInput;
                _panel.HandleKey(helpKey, helpMods);
                continue;
            }

            switch (input.Key)
            {
                case ConsoleKey.UpArrow:
                    if (items.Count > 0) selected = (selected - 1 + items.Count) % items.Count;
                    break;
                case ConsoleKey.DownArrow:
                    if (items.Count > 0) selected = (selected + 1) % items.Count;
                    break;
                case ConsoleKey.Escape:
                    if (HandleEscape(lobby)) return null; // aborted out of the lobby entirely
                    break;
                case ConsoleKey.Enter:
                    if (items.Count > 0 && Activate(lobby, selected)) return null;
                    break;
                default:
                    var digit = input.Key - ConsoleKey.D1;
                    if (digit >= 0 && digit < items.Count && Activate(lobby, digit)) return null;
                    break;
            }
        }

        return null;
    }

    // Returns the header lines and the selectable items for the current lobby state.
    private static (string[] Header, IReadOnlyList<string> Items) BuildView(ILobby lobby, string myName)
    {
        switch (lobby.State)
        {
            case LobbyState.IncomingInvite:
                var inv = lobby.Incoming;
                return ([
                    "♚ Network Game ♔",
                    "",
                    inv is null ? "Incoming invite…" : $"{inv.PeerName} invites you — you play {inv.YourSide}.",
                    ""
                ], ["Accept", "Decline"]);

            case LobbyState.Inviting:
                return (["♚ Network Game ♔", "", lobby.StatusMessage ?? "Inviting…", ""], ["Cancel"]);

            case LobbyState.Declined:
            case LobbyState.Failed:
                return (["♚ Network Game ♔", "", lobby.StatusMessage ?? "Not connected.", ""], ["Back"]);

            default: // Browsing
                var peers = lobby.Peers;
                var header = new[]
                {
                    $"♚ LAN Lobby — {myName} ♔",
                    "",
                    peers.Count == 0 ? "Searching for players on your network…" : "Select a player to invite:",
                    ""
                };
                var items = new List<string>(peers.Count + 1);
                foreach (var peer in peers) items.Add(peer.Label);
                items.Add("Back");
                return (header, items);
        }
    }

    // Acts on the chosen item; returns true if the lobby should close with no session (user backed out).
    private bool Activate(ILobby lobby, int index)
    {
        switch (lobby.State)
        {
            case LobbyState.IncomingInvite:
                if (index == 0) lobby.Accept(); else lobby.Decline();
                return false;
            case LobbyState.Inviting:
                lobby.Cancel();
                return false;
            case LobbyState.Declined:
            case LobbyState.Failed:
                lobby.Cancel(); // clears the message, back to Browsing
                return false;
            default: // Browsing
                var peers = lobby.Peers;
                if (index >= 0 && index < peers.Count)
                {
                    lobby.Invite(peers[index]);
                    return false;
                }
                return true; // "Back"
        }
    }

    // What Esc does in each state, for the key list (F1); HandleEscape below is what it does.
    private static string EscapeAction(LobbyState state) => state switch
    {
        LobbyState.IncomingInvite => "Decline",
        LobbyState.Inviting or LobbyState.Declined or LobbyState.Failed => "Cancel",
        _ => "Leave",
    };

    private static bool HandleEscape(ILobby lobby)
    {
        switch (lobby.State)
        {
            case LobbyState.IncomingInvite:
                lobby.Decline();
                return false;
            case LobbyState.Inviting:
            case LobbyState.Declined:
            case LobbyState.Failed:
                lobby.Cancel();
                return false;
            default:
                return true; // Browsing: Esc leaves the lobby
        }
    }

    private async Task<string?> PromptNameAsync(string current, CancellationToken cancellationToken)
    {
        var typed = current;
        while (!cancellationToken.IsCancellationRequested)
        {
            RenderList(
                ["♚ Network Game ♔", "", "Enter your name (Enter to confirm, Esc to cancel):", ""],
                // The card marks the selected row with its own ▶, so no "> " prompt here: it read "▶ > Seb_".
                [$"{typed}_"], 0);

            if (!Terminal.HasInput())
            {
                OnIdle();
                await Task.Delay(TimeSpan.FromMilliseconds(50), _time, cancellationToken);
                continue;
            }

            var input = Terminal.TryReadInput();
            switch (input.Key)
            {
                case ConsoleKey.Enter:
                    var name = typed.Trim();
                    return name.Length == 0 ? "Player" : name;
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.Backspace:
                    if (typed.Length > 0) typed = typed[..^1];
                    break;
                default:
                    if (typed.Length < MaxNameLength && input.KeyChar is { } rune && IsNameChar(rune))
                        typed += rune.ToString();
                    break;
            }
        }

        return null;
    }

    private static bool IsNameChar(Rune r) =>
        Rune.IsLetterOrDigit(r) || r.Value == ' ' || r.Value == '-' || r.Value == '_';

    // Lets the inspector's queued commands run while the lobby waits for input.
    protected override void OnIdle() => InspectorHooks.Pump();

    // Redraws only when the content changes. On the alternate screen the lobby is the menu panel's card over the
    // board (#84), as the startup menu is; otherwise a plain list, like MenuBase's normal mode.
    // The terminal size is part of the key: this loop is the lobby's own, not MenuBase's (which redraws on a size
    // change itself), so without it a resize redrew nothing and the card stayed centred for the old grid, clipped
    // to a sliver at the right edge of a narrower window.
    // escHelp, when given, is what Esc does here, listed in the card's key list (F1). The list's open state is part of
    // both keys below: opening or closing it redraws the card, at a different shape.
    private void RenderList(string[] header, IReadOnlyList<string> items, int selected, string? escHelp = null)
    {
        var (columns, rows) = Terminal.Size;
        if (Terminal.IsAlternateScreen)
        {
            var help = _panel?.ShowingHelp == true;
            var key = $"{columns}x{rows}#{help}#{escHelp}#{string.Join('|', header)}#{string.Join('|', items)}#{selected}";
            if (key == _lastContent) return;
            _lastContent = key;

            // The card cannot restore the cells beneath it, so the board goes back first (it clears). But only when
            // the card's shape may have changed: the backdrop is a full clear plus a Sixel blit, and redrawing it on
            // every typed letter or arrow press flashed the whole screen. A card of the same shape paints over the
            // old one cell for cell, so a selection move, or typing a name shorter than the prompt line, needs no
            // board. The card is as wide as its longest line and as tall as its line count, so those are the shape.
            var prompt = header.Length > 2 ? header[2] : "";
            var widest = Math.Max(Math.Max(header[0].Length, prompt.Length), items.Count == 0 ? 0 : items.Max(i => i.Length));
            var shape = $"{columns}x{rows}|{help}|{widest}|{items.Count}";
            if (shape != _lastShape)
            {
                _lastShape = shape;
                _drawBackdrop?.Invoke();
            }
            _panel ??= new MenuWidget(Terminal);
            _panel.HostHelp = escHelp is null ? [] : [new("Esc", escHelp)];
            _panel.Reset(header[0], prompt, [.. items], selected);
            _panel.RenderPanel();
            Terminal.Flush();
            return;
        }

        var sb = new StringBuilder();
        foreach (var line in header) sb.Append(line).Append('\n');
        for (var i = 0; i < items.Count; i++)
        {
            var marker = i == selected ? " ▶ " : "   ";
            sb.Append(marker).Append(i + 1).Append(") ").Append(items[i]).Append('\n');
        }

        var content = sb.ToString();
        if ($"{columns}x{rows}#{content}" == _lastContent) return;
        _lastContent = $"{columns}x{rows}#{content}";

        Terminal.Clear();
        Terminal.SetCursorPosition(0, 0);
        foreach (var line in content.Split('\n'))
            Terminal.WriteLine(line);
    }
}
