using System;
using System.Linq;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

/// <summary>
/// Desktop LAN lobby: name entry, then a live peer list with invite/accept, all rendered through
/// DIR.Lib's <see cref="PixelMenuWidget{TSurface}"/> — the same widget the startup menu uses, so the
/// look is consistent and no bespoke drawing is needed. Owns the Chess.Net stack (transport +
/// discovery + lobby) for its lifetime; the host polls <see cref="IsConnected"/> (take
/// <see cref="Session"/> and start the game) and <see cref="IsAborted"/> (return to the menu).
///
/// <para>Name entry is driven from KeyDown rather than SDL text-input events (the renderer never
/// starts SDL text input): enough for a display name — letters (Shift = caps), digits, space, '-'/'_'.
/// The lobby's content is derived from <see cref="LanLobby.State"/> every frame, so an invite that
/// arrives while browsing switches the screen on its own.</para>
/// </summary>
internal sealed class VkLanLobby : IWidget, IDisposable
{
    private const int MaxNameLength = 24;

    private readonly string _saveDir;
    private readonly Side _preferredColor;
    private readonly string _peerId;

    private PixelMenuWidget<VulkanContext>? _menu;
    private string _typedName;
    private bool _nameCommitted;

    // Built once the name is committed, so the beacon announces the final name.
    private UdpTcpLanTransport? _transport;
    private LanDiscovery? _discovery;
    private LanLobby? _lobby;

    // What the widget currently shows — so we only Reset() (which snaps the selection back to 0) when
    // the content actually changes, not every frame the live peer list is re-read.
    private string _shownTitle = "";
    private string _shownPrompt = "";
    private string[] _shownItems = [];
    private LanPeer[] _peers = [];

    public bool IsConnected => _lobby?.State == LobbyState.Connected;
    public NetworkSession? Session => _lobby?.Session;
    public bool IsAborted { get; private set; }

#if DEBUG
    /// <summary>The underlying pixel widget, so the DEBUG inspector can read its clickable regions +
    /// captured layout (null until the first Render builds it).</summary>
    public PixelWidgetBase<VulkanContext>? InspectorWidget => _menu;

    /// <summary>Lobby state exposed for the DEBUG inspector's appState snapshot.</summary>
    public LobbyState State => _lobby?.State ?? LobbyState.Browsing;
    public System.Collections.Generic.IReadOnlyList<LanPeer> Peers => _lobby?.Peers ?? [];
#endif

    public VkLanLobby(VkRenderer renderer, string saveDir, Side preferredColor)
    {
        _saveDir = saveDir;
        _preferredColor = preferredColor;
        var identity = LanIdentity.Load(saveDir);
        _peerId = identity.PeerId;
        _typedName = identity.Name;
    }

    public void Render(VkRenderer renderer)
    {
        _menu ??= new PixelMenuWidget<VulkanContext>(renderer, FontPaths.DejaVuSans);
        RefreshMenuContent();
        _menu.Render();
    }

    private void RefreshMenuContent()
    {
        string title, prompt;
        string[] items;

        if (!_nameCommitted)
        {
            title = "Network Game";
            prompt = $"Your name: {_typedName}|";
            items = ["Type a name, then press Enter"];
        }
        else
        {
            switch (_lobby!.State)
            {
                case LobbyState.IncomingInvite:
                    var inv = _lobby.Incoming;
                    title = "Invitation";
                    prompt = inv is null
                        ? "Incoming invite…"
                        : $"{inv.PeerName} invites you — you play {inv.YourSide}";
                    items = ["Accept", "Decline"];
                    break;

                case LobbyState.Inviting:
                    title = "Network Game";
                    prompt = _lobby.StatusMessage ?? "Inviting…";
                    items = ["Cancel"];
                    break;

                case LobbyState.Declined:
                case LobbyState.Failed:
                    title = "Network Game";
                    prompt = _lobby.StatusMessage ?? "Not connected";
                    items = ["Back"];
                    break;

                default: // Browsing
                    _peers = [.. _lobby.Peers];
                    title = $"LAN Lobby — {DisplayName}";
                    prompt = _peers.Length == 0
                        ? "Searching for players on your network…"
                        : "Select a player to invite:";
                    items = [.. _peers.Select(p => p.DisplayName), "Back"];
                    break;
            }
        }

        if (title == _shownTitle && prompt == _shownPrompt && items.AsSpan().SequenceEqual(_shownItems))
            return;

        _shownTitle = title;
        _shownPrompt = prompt;
        _shownItems = items;
        _menu!.Reset(title, prompt, [.. items]);
    }

    public bool HandleInput(InputEvent evt)
    {
        if (!_nameCommitted)
            return HandleNameEntry(evt);

        if (_menu is null || !_menu.HandleInput(evt))
            return false;

        if (!_menu.IsConfirmed)
            return true;

        var selected = _menu.SelectedIndex;
        switch (_lobby!.State)
        {
            case LobbyState.IncomingInvite:
                if (selected == 0) _lobby.Accept(); else _lobby.Decline();
                break;
            case LobbyState.Inviting:
                _lobby.Cancel();
                break;
            case LobbyState.Declined:
            case LobbyState.Failed:
                _lobby.Cancel(); // clears the message and returns to Browsing
                break;
            default: // Browsing
                if (selected >= 0 && selected < _peers.Length)
                    _lobby.Invite(_peers[selected]);
                else
                    IsAborted = true; // "Back"
                break;
        }

        // Force a content rebuild next Render (also clears the widget's confirmed flag via Reset).
        _shownItems = [];
        return true;
    }

    private bool HandleNameEntry(InputEvent evt)
    {
        if (evt is not InputEvent.KeyDown(var key, var mods))
            return false;

        switch (key)
        {
            case InputKey.Enter:
                CommitName();
                return true;
            case InputKey.Escape:
                IsAborted = true;
                return true;
            case InputKey.Backspace:
                if (_typedName.Length > 0)
                    _typedName = _typedName[..^1];
                return true;
            default:
                if (_typedName.Length < MaxNameLength && TryMapChar(key, mods, out var c))
                    _typedName += c;
                return true;
        }
    }

    private void CommitName()
    {
        var name = _typedName.Trim();
        if (name.Length == 0)
            name = "Player";
        _typedName = name;

        // Persist so the next launch prefills this name.
        new LanIdentity(name, _peerId).Save(_saveDir);

        _transport = new UdpTcpLanTransport();
        _discovery = new LanDiscovery(_transport, TimeProvider.System, _peerId, () => _typedName);
        _lobby = new LanLobby(_transport, _discovery, new LanIdentity(name, _peerId), _preferredColor);
        _lobby.Start();

        _nameCommitted = true;
        _shownItems = []; // force the first lobby screen to render
    }

    private string DisplayName => string.IsNullOrWhiteSpace(_typedName) ? "You" : _typedName;

    // Minimal printable-key mapping for the name field (no SDL text-input dependency).
    private static bool TryMapChar(InputKey key, InputModifier mods, out char c)
    {
        var shift = (mods & InputModifier.Shift) != 0;
        if (key is >= InputKey.A and <= InputKey.Z)
        {
            c = (char)((shift ? 'A' : 'a') + (key - InputKey.A));
            return true;
        }
        if (key is >= InputKey.D0 and <= InputKey.D9)
        {
            c = (char)('0' + (key - InputKey.D0));
            return true;
        }
        switch (key)
        {
            case InputKey.Space: c = ' '; return true;
            case InputKey.Minus: c = shift ? '_' : '-'; return true;
            default: c = '\0'; return false;
        }
    }

    public void Dispose()
    {
        _lobby?.Dispose();
        // Disposing the transport stops discovery/listening but does NOT close an established session
        // connection — that TcpClient is owned by the NetworkSession handed to the host.
        _ = _transport?.DisposeAsync();
    }
}
