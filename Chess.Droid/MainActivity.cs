using System;
using System.IO;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;
using static Android.Content.PM.ConfigChanges;
using File = System.IO.File;
using GameMode = Chess.Lib.GameMode; // Android.App.GameMode collides

// Use the rendered white-knight launcher icon (mipmap PNGs generated from DIR.Lib's
// chess_white_knight baseline) instead of the default Android robot.
[assembly: Application(Label = "Chess", Icon = "@mipmap/ic_launcher", Theme = "@style/AppTheme")]

// LAN network play (Chess.Net): sockets + Wi-Fi state, plus the multicast lock's permission — UDP
// broadcast discovery is dropped on many devices without CHANGE_WIFI_MULTICAST_STATE.
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessNetworkState)]
[assembly: UsesPermission(Android.Manifest.Permission.AccessWifiState)]
[assembly: UsesPermission(Android.Manifest.Permission.ChangeWifiMulticastState)]

namespace Chess.Droid;

/// <summary>
/// The Android chess head — pilot consumer of <see cref="SdlVulkanActivity"/>. SDL's Java bridge
/// launches this activity, and the base brings up SDL3 + Vulkan and calls <see cref="OnRendererReady"/>
/// where we mount the shared startup menu (<see cref="StartupWizard"/> via DIR.Lib's
/// <see cref="PixelMenuWidget{TSurface}"/>) and then the game display
/// (<see cref="PixelGameDisplay{TSurface}"/>), routing touches into whichever is active.
///
/// Player-vs-Computer runs the engine IN-PROCESS (<see cref="AiEngine"/>, on a background thread) —
/// there is no engine child process on Android, exactly like Chess.Web. Custom games currently start
/// from the standard board (interactive piece-placement setup is a follow-up).
/// </summary>
[Activity(
    Label = "Chess",
    MainLauncher = true,
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    Exported = true,
    ConfigurationChanges =
        LayoutDirection | Locale | GrammaticalGender | FontScale |
        FontWeightAdjustment | ConfigChanges.Orientation | UiMode |
        ScreenLayout | ScreenSize | SmallestScreenSize |
        Keyboard | KeyboardHidden | Navigation)]
public sealed class MainActivity : SdlVulkanActivity
{
    private const int AiDepth = 3; // modest depth: responsive on a phone (tune later)

    protected override string WindowTitle => "Chess";

    // Match the display's own canvas background so the raw-cleared surface and the margins GameUI
    // paints don't band (same rationale as the desktop GUI).
    protected override RGBAColor32 BackgroundColor => PixelGameDisplay<VulkanContext>.Background;

    private VkRenderer _renderer = null!;
    private StartupWizard? _wizard;
    private PixelMenuWidget<VulkanContext>? _menu;
    private PixelGameDisplay<VulkanContext>? _display;
    private Game? _game;

    // Player-vs-Computer state. The engine runs in-process and SYNCHRONOUSLY right after the human's
    // move: at AiDepth=3 the search is a few tens of ms, so it doesn't need a background thread (and a
    // struct move can't be handed across threads via volatile — that'd need a lock, only worth it for a
    // slower/stronger engine later). It's the AI's turn while it runs, so no input races.
    private bool _vsComputer;
    private Side _humanSide;

    // LAN network play (Chess.Net). Android has no GameLoop, so the session is driven directly here:
    // taps send our move, and DrainNetworkMoves applies the peer's on the SDL/render thread (GameUI is
    // single-threaded). The lobby/discovery run only while browsing; the multicast lock lets us receive
    // UDP broadcast at all. Lobby is built on the SDL thread via the _pendingLobby* flags so the name
    // dialog (UI thread) never touches renderer objects across threads.
    private UdpTcpLanTransport? _netTransport;
    private LanDiscovery? _netDiscovery;
    private LanLobby? _netLobby;
    private PixelMenuWidget<VulkanContext>? _lobbyMenu;
    private NetworkSession? _netSession;
    private Side _netLocalSide;
    private WifiManager.MulticastLock? _multicastLock;
    private volatile bool _pendingLobbyStart;
    private volatile bool _pendingShowMenu;
    private string _pendingLobbyName = "";
    private string _pendingLobbyPeerId = "";
    private Side _pendingLobbyPreferred;
    private string _lobbyShownKey = "";
    private LanPeer[] _lobbyPeers = [];

    protected override void OnRendererReady(VkRenderer renderer, SdlEventLoop loop)
    {
        // Route the renderer's loop diagnostics to logcat (Android has no console) — surfaces the
        // background/foreground surface-recreation traces. The renderer's DebugLog is compiled in only
        // for DEBUG or ANDROID, so this costs nothing on desktop Release. Tag: "chessdroid".
        SdlEventLoop.DiagnosticLog = m => Android.Util.Log.Info("chessdroid", m);

        // Match the navigation-bar (home button row) background to the app background — the system
        // default otherwise clashes below our status bar when the bars are visible. UI-thread call.
        RunOnUiThread(() =>
        {
            var c = PixelGameDisplay<VulkanContext>.Background;
#pragma warning disable CA1422 // deprecated in API 35 (edge-to-edge enforcement); fine through 34
            Window?.SetNavigationBarColor(Android.Graphics.Color.Argb(c.Alpha, c.Red, c.Green, c.Blue));
#pragma warning restore CA1422
        });

        // PixelGameDisplay loads its glyph fonts from FontPaths.FontsDirectory
        // (AppContext.BaseDirectory/Fonts). That path is empty in the APK sandbox, so stage the
        // bundled asset copies into it first — the Android analog of Chess.Web's LoadFontsAsync.
        StageFonts();
        _renderer = renderer;

        // Resume an unfinished game across a process kill (Android reclaims backgrounded apps freely);
        // otherwise open the startup menu. A background->foreground return keeps the in-memory state, so
        // this only runs on a cold launch.
        if (TryLoadGame() is { } saved && !saved.Game.IsFinished)
            StartGame(saved.Game, saved.ComputerSide);
        else
            ShowMenu();

        loop.OnRender = Render;
        loop.OnResize = (w, h) =>
        {
            if (_display is null) return;
            // Re-query the safe area on every resize — the cutout/gesture-bar insets move with
            // rotation and can change on fold/resume. The setter relayouts only when they differ.
            _display.SafeAreaInsets = SdlWindow.GetSafeAreaInsets();
            _display.TopCutout = QueryTopCutout();
            _display.OnResize((int)w, (int)h);
        };
        // SDL synthesizes mouse-button events from single-finger touches, so a tap arrives here as a
        // left button-down. Route it to the menu or the board depending on what's up.
        loop.OnMouseDown = (button, x, y, _, _) =>
        {
            if (button != 1) return false;
            return HandleTap((int)x, (int)y);
        };
        // The menu is static between taps (each tap already forces a redraw), so only the in-play
        // display needs the external-update poll. The lobby (live peer list), a pending lobby/menu
        // transition (set from the name dialog's UI thread), and an incoming network move all need the
        // ~16ms WaitEventTimeout poll to drive a redraw with no input event.
        loop.CheckNeedsRedraw = () =>
            (_display?.HasPendingUpdate ?? false)
            || _pendingLobbyStart || _pendingShowMenu || _netLobby is not null
            || (_netSession is { } s && (s.HasIncomingMove || s.PeerLeft));
        // Android's back button/gesture: SDL traps it before the activity's onBackPressed and
        // delivers it as a key (AC_BACK -> InputKey.Escape), already on the SDL thread. Desktop Esc
        // semantics, staged: playback -> live game -> menu (state is saved move-by-move) -> launcher.
        loop.OnKeyDown = (key, _) =>
        {
            if (key != InputKey.Escape) return false;
            if (_netLobby is not null)
            {
                ShowMenu(); // leave the lobby (tears down discovery/sockets)
                return true;
            }
            if (_display is { } d)
            {
                if (d.UI.Mode == GameUIMode.Playback)
                    d.UI.ExitPlayback();
                else
                    ShowMenu();
                return true;
            }
            RunOnUiThread(() => MoveTaskToBack(true)); // menu: hand back to the launcher
            return true;
        };
    }

    private void ShowMenu()
    {
        CleanupNetwork(); // tear down any lobby/session/lock before returning to the menu
        _display = null;
        // No Play-by-Link on Android (no link driver), but Network game is on — Android can open
        // sockets. "Continue game" appears whenever an unfinished save exists (back button mid-game,
        // or a cold launch with one on disk) — returning to the menu must never cost the game; only
        // starting a new one overwrites it.
        var canContinue = TryLoadGame() is { } s && !s.Game.IsFinished;
        _wizard = new StartupWizard(includeContinue: canContinue, includeNetworkPlay: true);
        _menu = new PixelMenuWidget<VulkanContext>(_renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = _wizard.Current;
        _menu.Reset(title, prompt, [.. items]);
    }

    private void Render()
    {
        // Transitions requested from the name dialog (UI thread) are actioned here on the SDL thread,
        // so no renderer object is ever touched across threads.
        if (_pendingShowMenu) { _pendingShowMenu = false; ShowMenu(); }
        if (_pendingLobbyStart) { _pendingLobbyStart = false; StartLobby(); }

        if (_netLobby is not null)
        {
            if (_netLobby.State == LobbyState.Connected)
            {
                StartNetworkGame(); // sets _display + _netSession, clears the lobby -> falls through
            }
            else
            {
                RenderLobby();
                return;
            }
        }

        if (_netSession is not null)
            DrainNetworkMoves();

        if (_display is not null)
            _display.Render();
        else
            _menu?.Render();
    }

    private bool HandleTap(int x, int y)
    {
        if (_menu is not null && _wizard is not null)
        {
            if (!_menu.HandleInput(new InputEvent.MouseDown(x, y)))
                return false;
            if (_menu.IsConfirmed)
            {
                _wizard.Confirm(_menu.SelectedIndex);
                if (_wizard.IsComplete)
                {
                    var (mode, computerSide, _) = _wizard.Result;
                    _menu = null;
                    _wizard = null;
                    if (mode == GameMode.Continue)
                    {
                        // The menu only offers Continue when the save parsed moments ago; if it
                        // fails NOW, re-showing the menu (without Continue) is the safe move — a
                        // silent fresh StartGame would overwrite the very game being continued.
                        if (TryLoadGame() is { } saved)
                            StartGame(saved.Game, saved.ComputerSide);
                        else
                            ShowMenu();
                    }
                    else if (mode == GameMode.NetworkGame)
                        EnterNetworkLobby(computerSide);
                    else
                        StartGame(new Game(), computerSide); // Custom -> standard board for now
                }
                else
                {
                    var (title, prompt, items) = _wizard.Current;
                    _menu.Reset(title, prompt, [.. items]);
                }
            }
            return true;
        }

        // LAN lobby: taps drive the peer list / accept-decline menu.
        if (_lobbyMenu is not null && _netLobby is not null)
        {
            HandleLobbyTap(x, y);
            return true;
        }

        if (_display is null) return false;

        // Network game: only our own turn is tappable; the peer's move arrives over the socket and is
        // applied by DrainNetworkMoves. After a local move lands, relay it to the peer.
        if (_netSession is not null)
        {
            if (_game is not null && !_game.IsFinished && _game.CurrentSide == _netLocalSide)
            {
                var before = _game.Plies.Count;
                _display.UI.HandleMouseDown(x, y);
                if (_game.Plies.Count == before + 1)
                    _netSession.SendMove(UciMove.FormatPly(_game.Plies[^1]));
            }
            return true;
        }

        // In-game: apply the human's tap, then let the engine reply (synchronously) if it's PvC.
        _display.UI.HandleMouseDown(x, y);
        SaveGame();
        PlayAiReply();
        return true;
    }

    private void StartGame(Game game, Side computerSide)
    {
        _game = game;
        _vsComputer = computerSide != Side.None;
        _humanSide = computerSide == Side.White ? Side.Black : Side.White;

        _menu = null;
        _wizard = null;
        _display = new PixelGameDisplay<VulkanContext>(_renderer);
        // Safe area BEFORE ResetGame so the first layout already clears the notch and the rounded
        // bottom; the notch strip shows the mode left and the move counter right of the camera.
        _display.SafeAreaInsets = SdlWindow.GetSafeAreaInsets();
        _display.TopCutout = QueryTopCutout();
        // Short labels: the notch strip is status-bar-sized chrome, not a headline.
        _display.TopStripLabel = _vsComputer ? $"vs AI ({_humanSide})" : "PvP";
        // Touch-only: no keyboard hints in the status bar; playback exits via the history chip.
        _display.KeyboardHints = false;
        _display.ResetGame(_game);

        SaveGame();
        PlayAiReply(); // if the human chose Black, White (the AI) opens
    }

    // Plays the engine's reply in-process while it's the computer's turn. Synchronous: at AiDepth the
    // search is brief, and it's not the human's turn, so blocking the loop for it is acceptable and far
    // simpler than a background thread + cross-thread struct handoff. (Loop handles a chain in case a
    // future mode has the AI move more than once.)
    private void PlayAiReply()
    {
        if (!_vsComputer || _game is null) return;
        while (!_game.IsFinished && _game.CurrentSide != _humanSide)
        {
            var move = new AiEngine(_game.CurrentSide, maxDepth: AiDepth).PickMove(_game);
            if (move is not { } mv) break;
            _game.TryMove(mv);
            SaveGame();
        }
    }

    // ── LAN network play (Chess.Net) ────────────────────────────────────────────────────────────

    // Network game chosen: ask for a display name (native dialog), then hand off to StartLobby on the
    // SDL thread via the _pendingLobby* flags — the dialog callbacks run on the UI thread and must not
    // touch renderer objects.
    private void EnterNetworkLobby(Side computerSide)
    {
        _menu = null;
        _wizard = null;
        var identity = LanIdentity.Load(FilesDir!.AbsolutePath);
        _pendingLobbyPeerId = identity.PeerId;
        _pendingLobbyPreferred = computerSide == Side.White ? Side.Black : Side.White;
        var current = string.IsNullOrWhiteSpace(identity.Name)
            ? (Android.OS.Build.Model ?? "Player")
            : identity.Name;
        PromptName(current);
    }

    private void PromptName(string current)
    {
        RunOnUiThread(() =>
        {
            var input = new Android.Widget.EditText(this) { Text = current };
            input.SetSingleLine(true);
            new AlertDialog.Builder(this)
                .SetTitle("Network game — your name")!
                .SetView(input)!
                .SetPositiveButton("Join", (_, _) =>
                {
                    var name = input.Text?.Trim();
                    _pendingLobbyName = string.IsNullOrEmpty(name) ? "Player" : name;
                    _pendingLobbyStart = true; // picked up by Render on the SDL thread
                })!
                .SetNegativeButton("Cancel", (_, _) => _pendingShowMenu = true)!
                .SetCancelable(false)!
                .Show();
        });
    }

    // Builds the Chess.Net stack on the SDL thread (all renderer/socket objects live here).
    private void StartLobby()
    {
        var name = _pendingLobbyName;
        new LanIdentity(name, _pendingLobbyPeerId).Save(FilesDir!.AbsolutePath);

        AcquireMulticastLock();
        _netTransport = new UdpTcpLanTransport();
        _netDiscovery = new LanDiscovery(_netTransport, TimeProvider.System, _pendingLobbyPeerId, () => name);
        _netLobby = new LanLobby(_netTransport, _netDiscovery,
            new LanIdentity(name, _pendingLobbyPeerId), _pendingLobbyPreferred);
        _netLobby.Start();
        _lobbyMenu = new PixelMenuWidget<VulkanContext>(_renderer, FontPaths.DejaVuSans);
        _lobbyShownKey = "";
    }

    private void RenderLobby()
    {
        if (_netLobby is null || _lobbyMenu is null) return;

        string title, prompt;
        string[] items;
        switch (_netLobby.State)
        {
            case LobbyState.IncomingInvite:
                var inv = _netLobby.Incoming;
                title = "Invitation";
                prompt = inv is null ? "Incoming invite…" : $"{inv.PeerName} invites you — you play {inv.YourSide}";
                items = ["Accept", "Decline"];
                break;
            case LobbyState.Inviting:
                title = "Network Game";
                prompt = _netLobby.StatusMessage ?? "Inviting…";
                items = ["Cancel"];
                break;
            case LobbyState.Declined:
            case LobbyState.Failed:
                title = "Network Game";
                prompt = _netLobby.StatusMessage ?? "Not connected";
                items = ["Back"];
                break;
            default: // Browsing
                _lobbyPeers = [.. _netLobby.Peers];
                title = "LAN Lobby";
                prompt = _lobbyPeers.Length == 0 ? "Searching for players…" : "Tap a player to invite:";
                items = [.. LanPeer.ResolveLabels(_lobbyPeers), "Back"];
                break;
        }

        // Only Reset (which snaps selection to 0) when the content actually changes.
        var key = $"{title}\n{prompt}\n{string.Join('\n', items)}";
        if (key != _lobbyShownKey)
        {
            _lobbyShownKey = key;
            _lobbyMenu.Reset(title, prompt, [.. items]);
        }
        _lobbyMenu.Render();
    }

    private void HandleLobbyTap(int x, int y)
    {
        if (_lobbyMenu is null || _netLobby is null) return;
        if (!_lobbyMenu.HandleInput(new InputEvent.MouseDown(x, y))) return;
        if (!_lobbyMenu.IsConfirmed) return;

        var selected = _lobbyMenu.SelectedIndex;
        switch (_netLobby.State)
        {
            case LobbyState.IncomingInvite:
                if (selected == 0) _netLobby.Accept(); else _netLobby.Decline();
                break;
            case LobbyState.Inviting:
            case LobbyState.Declined:
            case LobbyState.Failed:
                _netLobby.Cancel();
                break;
            default: // Browsing
                if (selected >= 0 && selected < _lobbyPeers.Length)
                    _netLobby.Invite(_lobbyPeers[selected]);
                else
                    ShowMenu(); // "Back"
                break;
        }
        _lobbyShownKey = ""; // force a rebuild next RenderLobby (also clears the widget's confirmed flag)
    }

    // A peer connected: keep the session (its socket outlives the lobby), tear down discovery, and
    // start a board driven by taps (send) + DrainNetworkMoves (receive).
    private void StartNetworkGame()
    {
        var session = _netLobby!.Session!;
        _netSession = session;
        _netLocalSide = session.LocalSide;

        _netLobby.Dispose();
        _netLobby = null;
        _netDiscovery = null;
        _lobbyMenu = null;
        if (_netTransport is not null) { _ = _netTransport.DisposeAsync(); _netTransport = null; }
        ReleaseMulticastLock(); // discovery is done; the game socket stays open

        _game = new Game();
        _vsComputer = false;
        _display = new PixelGameDisplay<VulkanContext>(_renderer);
        _display.SafeAreaInsets = SdlWindow.GetSafeAreaInsets();
        _display.TopCutout = QueryTopCutout();
        _display.TopStripLabel = $"LAN ({_netLocalSide})";
        _display.KeyboardHints = false;
        _display.ResetGame(_game);
    }

    // Applies moves the peer sent, on the SDL/render thread (GameUI is single-threaded). No
    // MoveLockSide is set, so TryPerformAction isn't gated — the local-turn guard lives in HandleTap.
    private void DrainNetworkMoves()
    {
        if (_netSession is null || _display is null || _game is null) return;

        if (_netSession.PeerLeft)
        {
            ShowMenu(); // opponent left / disconnected -> back to the menu
            return;
        }

        while (!_game.IsFinished && _game.CurrentSide == _netSession.RemoteSide
               && _netSession.TryDequeueMove(out var uci))
        {
            _display.UI.TryPerformAction(UciMove.Parse(uci));
        }
    }

    private void AcquireMulticastLock()
    {
        try
        {
            var wifi = (WifiManager?)(ApplicationContext?.GetSystemService(Android.Content.Context.WifiService));
            _multicastLock = wifi?.CreateMulticastLock("chess-lan");
            _multicastLock?.Acquire();
        }
        catch { /* best-effort: without it, some devices drop broadcast receives */ }
    }

    private void ReleaseMulticastLock()
    {
        try { if (_multicastLock is { IsHeld: true }) _multicastLock.Release(); } catch { /* ignore */ }
        _multicastLock = null;
    }

    private void CleanupNetwork()
    {
        _netSession?.Dispose();
        _netSession = null;
        _netLobby?.Dispose();
        _netLobby = null;
        _netDiscovery = null;
        _lobbyMenu = null;
        if (_netTransport is not null) { _ = _netTransport.DisposeAsync(); _netTransport = null; }
        ReleaseMulticastLock();
        _pendingLobbyStart = false;
        _pendingShowMenu = false;
        _lobbyShownKey = "";
    }

    // The game is persisted to app-internal storage: a header line (mode marker: the computer's side)
    // then the UCI move list. Replaying the moves rebuilds the full position AND history (castling /
    // en-passant rights included) that a bare FEN snapshot would lose.
    private string GamePath => Path.Combine(FilesDir!.AbsolutePath, "game.uci");

    // Persistence lives in the shared Chess.UCI.GameStore so every front-end (desktop GUI too) uses
    // the same file format and replay logic; these wrappers just supply the path, the computer side
    // (derived from this activity's mode state), and the logcat sink.
    private (Game Game, Side ComputerSide)? TryLoadGame()
        => GameStore.TryLoad(GamePath, m => SdlEventLoop.DiagnosticLog?.Invoke(m));

    private void SaveGame()
    {
        if (_game is null) return;
        var computerSide = _vsComputer ? (_humanSide == Side.White ? Side.Black : Side.White) : Side.None;
        GameStore.Save(GamePath, _game, computerSide, m => SdlEventLoop.DiagnosticLog?.Invoke(m));
    }

    // The exact camera punch-hole bounds, so the notch strip lines its text up with the camera's row
    // (the safe-area top inset is deeper than the cutout — strip-centered text sits visibly below the
    // camera) and keeps out of its true horizontal span. Null when unavailable (pre-API-29, insets
    // not attached yet, no cutout) — the strip then falls back to generic centering.
    private (int Left, int Top, int Right, int Bottom)? QueryTopCutout()
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
            var r = Window?.DecorView.RootWindowInsets?.DisplayCutout?.BoundingRectTop;
            return r is not null && r.Width() > 0 ? (r.Left, r.Top, r.Right, r.Bottom) : null;
        }
        catch
        {
            return null;
        }
    }

    // Copies the bundled font assets (assets/Fonts/*.ttf) into FontPaths.FontsDirectory once, so the
    // managed rasterizer's file-based loading finds them. Idempotent across launches.
    private void StageFonts()
    {
        var dir = FontPaths.FontsDirectory;
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "DejaVuSans.ttf", "Merida.ttf" })
        {
            var dest = Path.Combine(dir, name);
            if (File.Exists(dest)) continue;
            using var asset = Assets!.Open($"Fonts/{name}");
            using var file = File.Create(dest);
            asset.CopyTo(file);
        }
    }
}
