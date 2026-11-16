using System;
using System.Linq;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.Net.Cloud;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

/// <summary>
/// Desktop lobby for a LAN or an online game: name entry, then a live peer list with invite/accept, all
/// rendered through DIR.Lib's <see cref="PixelMenuWidget{TSurface}"/>, the same widget the startup menu
/// uses, so the look is consistent and no bespoke drawing is needed. Owns the courier's stack for its
/// lifetime (<see cref="LanPlayStack"/>: transport, discovery and lobby; or <see cref="CloudPlayStack"/>:
/// the backend's lobby) and talks only to <see cref="ILobby"/> after that, as the touch heads do. The host
/// polls <see cref="IsConnected"/> (take <see cref="Session"/> and start the game) and
/// <see cref="IsAborted"/> (return to the menu).
///
/// <para>Name entry is driven from KeyDown rather than SDL text-input events (the renderer never
/// starts SDL text input): enough for a display name — letters (Shift = caps), digits, space, '-'/'_'.
/// The lobby's content is derived from <see cref="ILobby.State"/> every frame, so an invite that
/// arrives while browsing switches the screen on its own.</para>
/// </summary>
internal sealed class VkNetworkLobby : IWidget, IDisposable
{
    private const int MaxNameLength = 24;

    private readonly string _saveDir;
    private readonly Side _preferredColor;
    // The backend config for an online game; null for a LAN game.
    private readonly string? _cloudConfig;

    private PixelMenuWidget<VulkanContext>? _menu;
    // Built with the menu, from the app's task tracker and the loop's redraw. The menu declares its own keys, so the
    // router lists it and needs no fallback.
    private InputRouter? _router;
    private readonly BackgroundTaskTracker _tasks;
    private readonly System.Action _requestRedraw;
    private string _typedName;
    private bool _nameCommitted;

    // Built once the name is committed, so the beacon (or the posted game) carries the final name.
    private LanPlayStack? _lan;
    private CloudPlayStack? _cloud;
    private ILobby? _lobby;

    // What the widget currently shows — so we only Reset() (which snaps the selection back to 0) when
    // the content actually changes, not every frame the live peer list is re-read.
    private string _shownTitle = "";
    private string _shownPrompt = "";
    private string[] _shownItems = [];
    private LobbyPeer[] _peers = [];

    public bool IsConnected => _lobby?.State == LobbyState.Connected;
    public NetworkSession? Session => _lobby?.Session;
    public bool IsAborted { get; private set; }
    /// <summary>An online game (the backend) rather than a LAN one.</summary>
    public bool IsOnline => _cloudConfig is not null;

    /// <summary>The underlying pixel widget, so the inspector can read its clickable regions +
    /// captured layout (null until the first Render builds it).</summary>
    public PixelWidgetBase<VulkanContext>? InspectorWidget => _menu;

    /// <summary>Lobby state exposed for the inspector's appState snapshot.</summary>
    public LobbyState State => _lobby?.State ?? LobbyState.Browsing;
    public System.Collections.Generic.IReadOnlyList<LobbyPeer> Peers => _lobby?.Peers ?? [];

    public VkNetworkLobby(VkRenderer renderer, string saveDir, Side preferredColor, string? cloudConfig,
        BackgroundTaskTracker tasks, System.Action requestRedraw)
    {
        _cloudConfig = cloudConfig;
        _tasks = tasks;
        _requestRedraw = requestRedraw;
        _saveDir = saveDir;
        _preferredColor = preferredColor;
        _typedName = LanProfile.Load(saveDir).Name;
    }

    public void Render(VkRenderer renderer, float dpiScale)
    {
        if (_menu is null)
        {
            var menu = new PixelMenuWidget<VulkanContext>(renderer, FontPaths.DejaVuSans);
            _menu = menu;
            _router = new InputRouter(menu.Ui, _tasks, _requestRedraw) { Widgets = () => [menu] };
        }
        RefreshMenuContent();
        // The panel is sized in design units, so it needs the window's display scale to keep its size on a
        // high-DPI screen.
        _menu.DpiScale = dpiScale;
        _menu.RenderPanel();
    }

    private void RefreshMenuContent()
    {
        string title, prompt;
        string[] items;

        if (!_nameCommitted)
        {
            title = GameTitle;
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
                    title = GameTitle;
                    prompt = _lobby.StatusMessage ?? "Inviting…";
                    items = ["Cancel"];
                    break;

                case LobbyState.Declined:
                case LobbyState.Failed:
                    title = GameTitle;
                    prompt = _lobby.StatusMessage ?? "Not connected";
                    items = ["Back"];
                    break;

                default: // Browsing
                    _peers = [.. _lobby.Peers];
                    title = $"{(IsOnline ? "Online" : "LAN")} Lobby: {DisplayName}";
                    prompt = _peers.Length > 0
                        ? (IsOnline ? "Select a game to join:" : "Select a player to invite:")
                        // The online posting is already up by the time this shows, so the wait is for
                        // someone to join it rather than a search.
                        : IsOnline ? "Your game is posted. Waiting for an opponent…" : "Searching for players on your network…";
                    items = [.. _peers.Select(p => p.Label), "Back"];
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

        if (_menu is null || _router is null || !_router.Handle(evt))
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
        new LanProfile(name).Save(_saveDir);

        if (IsOnline)
        {
            // No backend after all (the config did not parse): back to the menu, which is what a build
            // without one shows anyway.
            _cloud = CloudPlayStack.TryCreate(_cloudConfig, name, _preferredColor);
            if (_cloud is null)
            {
                IsAborted = true;
                return;
            }
            _lobby = _cloud.Lobby;
        }
        else
        {
            _lan = new LanPlayStack(name, _preferredColor, TimeProvider.System);
            _lobby = _lan.Lobby;
        }
        _lobby.Start();

        _nameCommitted = true;
        _shownItems = []; // force the first lobby screen to render
    }

    private string GameTitle => IsOnline ? "Online Game" : "Network Game";

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
        // Disposing the stack sends the bye and closes the discovery/listening sockets (LAN), or
        // withdraws the posting (online), but does NOT close an established session: its TcpClient or
        // its subscription is owned by the NetworkSession handed to the host.
        _ = _lan?.DisposeAsync();
        _ = _cloud?.DisposeAsync();
    }
}
