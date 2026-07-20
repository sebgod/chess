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
/// the user backs out). The desktop GUI's <c>VkLanLobby</c> is the pixel equivalent.
/// </summary>
internal sealed class ConsoleLanLobby : MenuBase<NetworkSession?>
{
    private const int MaxNameLength = 24;

    private readonly TimeProvider _time;
    private readonly string _saveDir;
    private readonly Side _preferredColor;
    private string _lastContent = "";

    public ConsoleLanLobby(IVirtualTerminal terminal, TimeProvider timeProvider, string saveDir, Side preferredColor)
        : base(terminal, timeProvider)
    {
        _time = timeProvider;
        _saveDir = saveDir;
        _preferredColor = preferredColor;
    }

    protected override async Task<NetworkSession?> ShowAsyncCore(CancellationToken cancellationToken)
    {
        var identity = LanIdentity.Load(_saveDir);

        var name = await PromptNameAsync(identity.Name, cancellationToken);
        if (name is null) return null; // cancelled
        new LanIdentity(name, identity.PeerId).Save(_saveDir);

        await using var transport = new UdpTcpLanTransport();
        using var discovery = new LanDiscovery(transport, _time, identity.PeerId, () => name);
        using var lobby = new LanLobby(transport, discovery, new LanIdentity(name, identity.PeerId), _preferredColor);
        lobby.Start();

        var selected = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (lobby.State == LobbyState.Connected)
                return lobby.Session;

            var (header, items) = BuildView(lobby, name);
            if (selected >= items.Count) selected = Math.Max(0, items.Count - 1);
            RenderList(header, items, selected);

            if (!Terminal.HasInput())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _time, cancellationToken);
                continue;
            }

            var input = Terminal.TryReadInput();
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
    private static (string[] Header, IReadOnlyList<string> Items) BuildView(LanLobby lobby, string myName)
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
                items.AddRange(peers.Select(p => p.DisplayName));
                items.Add("Back");
                return (header, items);
        }
    }

    // Acts on the chosen item; returns true if the lobby should close with no session (user backed out).
    private bool Activate(LanLobby lobby, int index)
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

    private static bool HandleEscape(LanLobby lobby)
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
                [$"> {typed}_"], 0);

            if (!Terminal.HasInput())
            {
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

    // Full-screen redraw (only when the content changes) — works in both the alternate-screen (Sixel)
    // and normal terminal modes, like MenuBase.
    private void RenderList(string[] header, IReadOnlyList<string> items, int selected)
    {
        var sb = new StringBuilder();
        foreach (var line in header) sb.Append(line).Append('\n');
        for (var i = 0; i < items.Count; i++)
        {
            var marker = i == selected ? " ▶ " : "   ";
            sb.Append(marker).Append(i + 1).Append(") ").Append(items[i]).Append('\n');
        }

        var content = sb.ToString();
        if (content == _lastContent) return;
        _lastContent = content;

        Terminal.Clear();
        Terminal.SetCursorPosition(0, 0);
        foreach (var line in content.Split('\n'))
            Terminal.WriteLine(line);
    }
}
