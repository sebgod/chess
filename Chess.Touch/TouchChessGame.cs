using System.Numerics;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.Net.Cloud;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.Touch;

/// <summary>
/// The chess game on a touch screen, shared by the tablet heads (Chess.Droid and Chess.iOS). The
/// head's SDL host brings up SDL3 + Vulkan and calls <see cref="Attach"/>, where we mount the shared
/// startup menu (<see cref="StartupWizard"/> via DIR.Lib's <see cref="PixelMenuWidget{TSurface}"/>)
/// and then the game display (<see cref="PixelGameDisplay{TSurface}"/>), routing touches into
/// whichever is active. What the OS does differently comes through <see cref="ITouchPlatform"/>.
///
/// Player-vs-Computer runs the engine IN-PROCESS (<see cref="LocalEnginePlayer"/> over
/// <see cref="AiEngine"/>) — there is no engine child process on a phone or tablet, exactly like
/// Chess.Web. It searches synchronously on the SDL thread, which is why <see cref="Difficulty"/>
/// stops at a depth that stays answerable here. Custom games open in setup mode (tap a square, pick
/// a piece from the popup) and start on the display's "▶ Start" chip, the touch stand-in for the
/// desktop's s key.
///
/// Turn handling is the shared <see cref="GameSession"/>, ticked from <see cref="Render"/> rather than
/// from a loop of its own: taps go to a <see cref="QueuedInputPlayer"/> and the session applies them.
/// This is the same model Chess.Console and Chess.GUI drive through <see cref="GameLoop"/>; only the
/// thing doing the driving differs.
/// </summary>
/// <param name="platform">What the head's OS does for the game.</param>
/// <param name="cloudConfig">This build's backend config (<c>firebase-config.json</c>), or null when
/// it has none -- which is the normal state of a fork, a PR build and any checkout that has not opted
/// in. Absent means the "Online game" entry never appears and nothing else changes.</param>
public sealed class TouchChessGame(ITouchPlatform platform, string? cloudConfig)
{
    // Chosen in the startup wizard. Capped by Difficulty's own ladder, which stops at depth 4 partly
    // because this host searches on the UI thread — see DifficultyExtensions.ToSearchDepth.
    private Difficulty _difficulty = Difficulty.Normal;

    private VkRenderer _renderer = null!;
    private SdlVulkanWindow _window = null!;
    private StartupWizard? _wizard;
    private PixelMenuWidget<VulkanContext>? _menu;
    private PixelGameDisplay<VulkanContext>? _display;
    private Game? _game;

    // The shared turn model, ticked from Render (see AdvanceSession). Taps are handed to _input rather
    // than applied to GameUI directly, which is what lets this front-end use the same session the
    // desktop's GameLoop drives. Null while a menu or the lobby is up.
    private GameSession? _session;
    private readonly QueuedInputPlayer _input = new();

    // Player-vs-Computer state. The engine runs in-process and SYNCHRONOUSLY right after the human's
    // move: across the offered difficulty levels the search is tens of milliseconds to a few hundred,
    // so it doesn't need a background thread (and a struct move can't be handed across threads via
    // volatile — that'd need a lock, only worth it for a slower/stronger engine later). It's the AI's
    // turn while it runs, so no input races. This is one of the reasons Difficulty stops at depth 4.
    private bool _vsComputer;
    private Side _humanSide;

    // Across-the-table PvP (GameMode.AcrossTheTable): the two players sit OPPOSITE each other at a
    // flat tablet, and the frame turns 180° to face whoever is to move. Same-seat pass-and-play
    // (Player vs Player) never flips — the seating is the user's explicit menu choice, not a guess.
    private bool _acrossTheTable;
    // The mode the current game was started in — persisted with the save so Continue can restore it.
    private GameMode _mode = GameMode.PlayerVsPlayer;

    // Network play (Chess.Net), in two couriers behind one ILobby: the LAN (UDP discovery + a TCP
    // invite) and the cloud (a database row, so the opponent need not be awake). Both end in a
    // NetworkSession that the shared GameSession drives through a NetworkPlayer, exactly as the
    // engine is driven -- the earlier note here about taps sending and a DrainNetworkMoves applying
    // the peer's move describes the hand-rolled path that migration replaced.
    //
    // The lobby runs only while browsing. The platform's LAN discovery hold (Android's multicast
    // lock, which is what lets it receive UDP broadcast at all) is the LAN's alone; the cloud needs no
    // permission beyond internet. Either stack is built on the SDL thread via the _pendingLobby*
    // flags, so the name dialog (which may answer on another thread) never touches renderer objects
    // across threads.
    private LanPlayStack? _netLan;
    private CloudPlayStack? _netCloud;
    private ILobby? _netLobby;
    private PixelMenuWidget<VulkanContext>? _lobbyMenu;
    private NetworkSession? _netSession;
    private Side _netLocalSide;
    private volatile bool _pendingLobbyStart;
    private volatile bool _pendingShowMenu;
    private volatile bool _pendingLobbyCloud;
    private string _pendingLobbyName = "";
    private Side _pendingLobbyPreferred;

    private string _lobbyShownKey = "";
    private LobbyPeer[] _lobbyPeers = [];

    // Tap-vs-drag tracking for the menus (see OnMouseDown): where the finger went down, and where it
    // was last seen. How far it may travel and still count as a tap — a finger's worth of wobble,
    // scaled to the surface so it means the same on a phone and a tablet.
    private Vector2? _pointerDown;
    private Vector2 _pointerLast;
    private volatile bool _menuNeedsRedraw;

    /// <summary>Consumes the "a released menu tap changed something" flag (see OnMouseUp).</summary>
    private bool TakeMenuRedraw()
    {
        var pending = _menuNeedsRedraw;
        _menuNeedsRedraw = false;
        return pending;
    }
    private float TapSlop => MathF.Max(16f, _renderer.Height * 0.02f);

    // A menu (startup wizard or LAN lobby) is on screen, so taps are committed on release.
    private bool IsMenuUp => (_menu is not null && _wizard is not null) || (_lobbyMenu is not null && _netLobby is not null);

    /// <summary>
    /// Mounts the game on a ready renderer and wires the loop's callbacks. Called once, from the
    /// head's <c>OnRendererReady</c>, after the head has done what its OS needs first (a log sink,
    /// the fonts where <see cref="FontPaths"/> looks).
    /// </summary>
    public void Attach(VkRenderer renderer, SdlEventLoop loop, SdlVulkanWindow window)
    {
        _renderer = renderer;
        _window = window;

        // Resume an unfinished game across a process kill (a mobile OS reclaims backgrounded apps
        // freely); otherwise open the startup menu. A background->foreground return keeps the
        // in-memory state, so this only runs on a cold launch.
        if (TryLoadGame() is { } saved && IsResumable(saved))
            StartGame(saved.Game, saved.ComputerSide, saved.Mode, setUp: IsMidSetup(saved));
        else
            ShowMenu();

        loop.OnRender = Render;
        loop.OnResize = (w, h) =>
        {
            if (_display is null) return;
            // Re-fit the across-the-table transform to the new surface size (its CenteredRotation
            // translation depends on w/h); this also re-queries the safe area on every resize — the
            // cutout/gesture-bar insets move with rotation and can change on fold/resume.
            UpdateAcrossTheTableTransform();
            _display.OnResize((int)w, (int)h);
        };
        // SDL synthesizes mouse-button events from single-finger touches, so a tap arrives here as a
        // left button-down. Device → content: the renderer folds the across-the-table rotation into
        // its projection, so the tap comes back through its inverse before anything hit-tests it —
        // draw and hit-test can never drift.
        //
        // A MENU tap is committed on release, not on touch-down, and only if the finger stayed put:
        // menu items are big and close together, so acting on touch-down made a finger that slid even
        // slightly (or the start of a scroll-ish gesture) pick an item the user hadn't decided on.
        // A drag now just ends without a click. The BOARD keeps acting on touch-down — a chess move is
        // already a deliberate two-tap sequence and immediate feedback is what makes it feel direct.
        loop.OnMouseDown = (button, x, y, _, _) =>
        {
            if (button != 1) return false;
            var p = _renderer.ContentTransform.Invert(new Vector2(x, y));
            _pointerDown = p;
            _pointerLast = p;
            if (IsMenuUp) return true; // deferred to OnMouseUp
            return HandleTap((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
        };
        loop.OnMouseMove = (x, y) =>
        {
            _pointerLast = _renderer.ContentTransform.Invert(new Vector2(x, y));
            // Nothing hovers, so this mostly just tracks the finger for the tap-vs-drag test. What it
            // now also does is carry the setup drag ghost, served here on the event thread rather
            // than queued through _input — a per-finger-position queue would tick a session per
            // motion event. Returning true is how a handler asks the loop for the frame.
            return !IsMenuUp
                && (_display?.HandleDragPointer(new InputEvent.MouseMove(_pointerLast.X, _pointerLast.Y)) ?? false);
        };
        loop.OnMouseUp = _ =>
        {
            // OnMouseUp carries no coordinates, hence the tracked position.
            if (_pointerDown is not { } down) { _pointerDown = null; return; }
            _pointerDown = null;
            var up = _pointerLast;
            if (!IsMenuUp)
            {
                // The board commits on touch-DOWN, so the release only ever adds the one thing a tap
                // cannot say: that a setup drag ended on a different square. Queued rather than
                // applied here because every other board input goes through the session tick, and
                // CheckNeedsRedraw already wakes the loop for pending input. No slop test — the
                // squares are far apart and GameUI ignores a release on the square it started from,
                // which is precisely the wobble case the menus need TapSlop for.
                _input.ReleasePointer((int)MathF.Round(up.X), (int)MathF.Round(up.Y));
                return;
            }
            if (Vector2.Distance(down, up) > TapSlop) return; // dragged — not a tap, so not a click
            HandleTap((int)MathF.Round(up.X), (int)MathF.Round(up.Y));
            // A handler that RETURNS true tells the loop to repaint; OnMouseUp returns nothing, so a
            // menu committed on release has to ask for the frame itself — without this the wizard
            // advances invisibly and the next screen only appears on the following event.
            _menuNeedsRedraw = true;
        };
        // The menu is static between taps (each tap already forces a redraw), so only the in-play
        // display needs the external-update poll. The lobby (live peer list), a pending lobby/menu
        // transition (set from the name dialog's thread), and an incoming network move all need the
        // ~16ms WaitEventTimeout poll to drive a redraw with no input event.
        loop.CheckNeedsRedraw = () =>
            TakeMenuRedraw()
            || (_display?.HasPendingUpdate ?? false)
            || _pendingLobbyStart || _pendingShowMenu || _netLobby is not null
            || (_netSession is { } s && (s.HasIncomingMove || s.PeerLeft))
            // A tap queued for the session needs a frame to be applied in. Taps already force one, so
            // this is belt-and-braces for input that arrives by any other route.
            || _input.HasPendingInput;
        // Android's back button/gesture: SDL traps it before the activity's onBackPressed and
        // delivers it as a key (AC_BACK -> InputKey.Escape), already on the SDL thread. Desktop Esc
        // semantics, staged: playback -> live game -> menu (state is saved move-by-move) -> launcher.
        // SdlVulkan.Renderer 7.33 hands the event over whole rather than (key, modifiers), which is
        // what makes the repeat check below possible. iOS has no back gesture; an iPad with a
        // keyboard attached reaches the same ladder through Esc.
        loop.OnKeyDown = keyEvent =>
        {
            if (keyEvent.Key != InputKey.Escape) return false;
            // Back is a one-shot. Held, the OS repeats it, and each repeat would take another step
            // down this ladder — a long press would walk playback, game and menu in one gesture and
            // drop the player at the launcher, which is not what holding a button means.
            if (keyEvent.Repeat) return true;
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
            platform.LeaveToHome(); // menu: hand back to the launcher
            return true;
        };
    }

    private void ShowMenu()
    {
        // The session goes FIRST, and _netSession is released to it: for a LAN game the opponent is a
        // NetworkPlayer, and closing the socket is its DisposeAsync's job. Letting CleanupNetwork
        // dispose the same NetworkSession afterwards would be a double close. Fire-and-forget matches
        // the discovery stack below, and is honest here — every opponent this front-end builds tears
        // down synchronously (the in-process engine holds nothing at all).
        if (_session is not null) { _ = _session.DisposeAsync(); _session = null; }
        _netSession = null;

        CleanupNetwork(); // tear down any lobby/discovery/lock before returning to the menu
        _display = null;
        // The menu renders through the same projection — never let it inherit a turned frame.
        _renderer.ContentTransform = ContentTransform.Identity;
        // No Play-by-Link on a touch head (no link driver), but both network entries are on where
        // they can work: "Network game" always (the app can open sockets), "Online game" only when
        // this build carries a backend config. "Continue game" appears whenever an unfinished save
        // exists (back button mid-game, or a cold launch with one on disk) — returning to the menu
        // must never cost the game; only starting a new one overwrites it.
        var canContinue = TryLoadGame() is { } s && IsResumable(s);
        _wizard = new StartupWizard(
            (canContinue ? StartupWizardOptions.Continue : StartupWizardOptions.None)
            | StartupWizardOptions.NetworkPlay
            | (cloudConfig is null ? StartupWizardOptions.None : StartupWizardOptions.OnlinePlay)
            | StartupWizardOptions.AcrossTheTable);
        _menu = new PixelMenuWidget<VulkanContext>(_renderer, FontPaths.DejaVuSans);
        var (title, prompt, items) = _wizard.Current;
        _menu.Reset(title, prompt, [.. items]);
    }

    private void Render()
    {
        // Transitions requested from the name dialog are actioned here on the SDL thread, so no
        // renderer object is ever touched across threads.
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

        AdvanceSession();

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
                    var (mode, computerSide, sideToMove, difficulty) = _wizard.Result;
                    // Continue/network keep whatever is already set; the wizard only asks when an
                    // engine is actually involved.
                    _difficulty = difficulty;
                    _menu = null;
                    _wizard = null;
                    if (mode == GameMode.Continue)
                    {
                        // The menu only offers Continue when the save parsed moments ago; if it
                        // fails NOW, re-showing the menu (without Continue) is the safe move — a
                        // silent fresh StartGame would overwrite the very game being continued.
                        if (TryLoadGame() is { } saved)
                            StartGame(saved.Game, saved.ComputerSide, saved.Mode, setUp: IsMidSetup(saved));
                        else
                            ShowMenu();
                    }
                    else if (mode is GameMode.NetworkGame or GameMode.OnlineGame)
                        EnterNetworkLobby(computerSide, cloud: mode == GameMode.OnlineGame);
                    else if (mode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard)
                    {
                        // Both wizard answers are honoured: the board it starts from and who moves
                        // first. Then setup mode, exactly like the desktop's GameLoop does it.
                        var board = mode == GameMode.CustomGameEmpty ? new Board() : Board.StandardBoard;
                        StartGame(new Game(board, sideToMove, []), computerSide, mode, setUp: true);
                    }
                    else
                        StartGame(new Game(), mode == GameMode.AcrossTheTable ? Side.None : computerSide, mode);
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

        // In-game (including LAN): hand the tap to the session's input player. Render applies it and
        // everything that follows from it — the engine's or peer's reply, the save, the relay, the
        // frame turn — on the frame this tap has already scheduled. Tapping out of turn is a no-op
        // (the rules reject it); the engine declines during setup, so a half-built position is never
        // played from.
        _input.PressPointer(x, y);
        return true;
    }

    // ── Across-the-table PvP (docs/across-the-table-flip.md) ─────────────────────────────────────

    // True when the current game is an across-the-table PvP game: two players opposite each other
    // at a flat tablet, the frame turning to face the player to move. Chosen explicitly in the menu
    // (GameMode.AcrossTheTable) — same-seat pass-and-play (Player vs Player) never qualifies, and
    // neither do vs-AI / LAN (they orient via GameUI.FlipBoard for their single local side).
    private bool IsAcrossTheTable => _display is not null && _acrossTheTable && _netSession is null;

    // Sets the renderer's whole-frame content transform for the current state and reapplies the safe
    // area through it. While Black is to move the frame turns 180° (identity on White's turn) so the
    // player opposite always reads history, status and text upright. Three things track the same
    // flag: the ContentTransform (turns the frame), FlipBoard (counter-turns the BOARD so the armies
    // keep their physical sides — a real board never swaps them), and MirrorChrome (swaps the chrome
    // layout's side in content space so the board and panel keep their physical POSITIONS on screen
    // under the turn — only the text orientation changes, nothing visibly jumps). Driven by the
    // COMMITTED live side only, so scrubbing through playback never turns the frame.
    private void UpdateAcrossTheTableTransform()
    {
        var flip = IsAcrossTheTable && _game is { CurrentSide: Side.Black };
        _renderer.ContentTransform = flip
            ? ContentTransform.CenteredRotation(Rotation90.Half, _renderer.Width, _renderer.Height)
            : ContentTransform.Identity;
        // StartGame calls this BEFORE ResetGame so the first layout already honors the transform;
        // FlipBoard tracking has to wait for the UI to exist (the post-ResetGame line covers it).
        if (_display is { HasGameUI: true } shown && IsAcrossTheTable) shown.UI.FlipBoard = flip;
        if (_display is not null) _display.MirrorChrome = flip;
        ApplyDeviceInsets();
    }

    // Safe-area insets and the camera cutout are reported by the OS in DEVICE space; the display lays
    // out in CONTENT space. Map them through the current transform — under the 180° flip the notch
    // lands on the content's bottom edge (top↔bottom, left↔right swap), and the layout needs no
    // special case. The SafeAreaInsets setter relayouts only when the value actually changes.
    private void ApplyDeviceInsets()
    {
        if (_display is null) return;
        var m = _renderer.ContentTransform;
        _display.SafeAreaInsets = DeviceContentMapping.ToContentInsets(_window.GetSafeAreaInsets(), m);
        _display.TopCutout = platform.QueryTopCutout() is { } cutout
            ? DeviceContentMapping.ToContentRect(cutout, m)
            : null;
    }

    /// <param name="setUp">Custom game: open in setup mode so the player can place pieces, and don't
    /// let the engine move until they tap "▶ Start" (the touch stand-in for the desktop's s key).</param>
    private void StartGame(Game game, Side computerSide, GameMode mode, bool setUp = false)
    {
        _game = game;
        _mode = mode;
        _vsComputer = computerSide != Side.None;
        _acrossTheTable = mode == GameMode.AcrossTheTable;
        _humanSide = computerSide == Side.White ? Side.Black : Side.White;

        _menu = null;
        _wizard = null;
        var display = new PixelGameDisplay<VulkanContext>(_renderer);
        _display = display;
        // Transform + safe area BEFORE ResetGame so the first layout already clears the notch and the
        // rounded bottom (and a resumed PvP game with Black to move opens already flipped); the notch
        // strip shows the mode left and the move counter right of the camera.
        UpdateAcrossTheTableTransform();
        // Short labels: the notch strip is status-bar-sized chrome, not a headline.
        display.TopStripLabel = _vsComputer ? $"vs AI ({_humanSide})" : _acrossTheTable ? "Across the table" : "PvP";
        // Touch has no s key, so the display offers a "▶ Start" chip while setting up.
        display.SetupStartRequested = FinishSetup;
        // Touch-only: no keyboard hints in the status bar; playback exits via the history chip.
        display.KeyboardHints = false;

        // The shared turn model (Chess.Lib.UI.GameSession) — the same one the desktop's GameLoop
        // drives, ticked here from Render instead of from a loop of its own. It calls ResetGame, so
        // this replaces the explicit call that used to sit here. beginInSetup is passed rather than
        // derived: a RESUMED custom game is still GameMode.Custom* but is long past placing pieces.
        _session = GameSession.Create(
            display,
            mode,
            computerSide,
            game.CurrentSide,
            () => _input,
            _vsComputer ? (side, _) => new LocalEnginePlayer(side, _difficulty) : null,
            resumeGame: game,
            beginInSetup: setUp);

        // Orient the board to the local player (their pieces at the bottom) vs the AI; PvP stays
        // White-at-bottom (_humanSide is White there). Across the table, FlipBoard tracks the frame
        // turn (see UpdateAcrossTheTableTransform — it can't reach the UI before ResetGame, so the
        // resume-consistent value is set here).
        display.UI.FlipBoard = IsAcrossTheTable ? _game is { CurrentSide: Side.Black } : _humanSide == Side.Black;

        // A game that isn't being set up can start playing at once; a custom one starts after its
        // "▶ Start" chip, when Tick reports SetupFinished (see AdvanceSession).
        if (!setUp)
        {
            StartSession(_session, display);
        }

        SaveGame();
    }

    // Brings the session up and restores the orientation, since StartAsync sets FlipBoard for the
    // desktop's single-local-side convention and the touch heads' across-the-table rule differs.
    private void StartSession(GameSession session, PixelGameDisplay<VulkanContext> display)
    {
        session.Start(TimeProvider.System);
        display.UI.FlipBoard = IsAcrossTheTable ? _game is { CurrentSide: Side.Black } : _humanSide == Side.Black;
    }

    // Leaves custom-game setup and starts play: the pieces on the board become the starting position
    // (Game.SetPiece keeps it as ply -1, so the save replays from it). Reached from the display's
    // "▶ Start" chip, which fires re-entrantly from inside GameUI's tap handling — so this only flips
    // the flag, and the session notices on its next tick. That indirection is also what stopped the
    // old code advancing the engine twice for this one tap.
    private void FinishSetup()
    {
        if (_display is null || !_display.HasGameUI || !_display.UI.IsSetupMode) return;
        _display.UI.CancelPlacement(); // a picker left open would keep its scrim over the live board
        _display.UI.IsSetupMode = false;
    }

    // Advances the shared session as far as it will go this frame, and applies the effects that are
    // this front-end's own: persistence, and turning the frame to face whoever is now to move.
    //
    // Looping to Idle rather than ticking once reproduces what PlayAiReply used to do — the touch
    // heads' engine is in-process and synchronous, so the human's move and the reply land in the same
    // frame. Every player here is self-limiting (the queued input is one event deep; the engine
    // declines once it isn't its turn), so this terminates on its own; the bound is belt-and-braces.
    private void AdvanceSession()
    {
        if (_session is not { } session || _display is not { } display) return;

        for (var guard = 0; guard < 64; guard++)
        {
            var tick = session.Tick();

            switch (tick.Outcome)
            {
                case SessionOutcome.Idle:
                    return;

                case SessionOutcome.SetupFinished:
                    // The session rebuilt the game from the placed board — pick up the new instance.
                    _game = session.Game;
                    StartSession(session, display);
                    SaveGame();
                    break;

                case SessionOutcome.NeedsRestart:
                    ShowMenu();
                    return;

                case SessionOutcome.Moved:
                    if (tick.PlyCommitted)
                    {
                        RelayIfOurs();
                        SaveGame();
                        // A committed move turns the frame to face the player now to move.
                        UpdateAcrossTheTableTransform();
                    }
                    break;

                case SessionOutcome.NeedsReset:
                    // Touch offers no reset affordance; nothing can raise this today.
                    return;
            }
        }
    }

    // ── LAN network play (Chess.Net) ────────────────────────────────────────────────────────────

    // A network game was chosen: ask for a display name (native dialog), then hand off to StartLobby
    // on the SDL thread via the _pendingLobby* flags — the dialog callbacks may run on another thread
    // and must not touch renderer objects. The name is the same one either courier announces, and is
    // remembered across both: it is who you are, not which network you are on.
    private void EnterNetworkLobby(Side computerSide, bool cloud)
    {
        _menu = null;
        _wizard = null;
        var profile = LanProfile.Load(platform.DataDirectory);
        _pendingLobbyCloud = cloud;
        _pendingLobbyPreferred = computerSide == Side.White ? Side.Black : Side.White;
        var current = string.IsNullOrWhiteSpace(profile.Name) ? platform.DefaultPlayerName : profile.Name;
        platform.PromptName(
            cloud ? "Online game — your name" : "Network game — your name",
            current,
            name =>
            {
                _pendingLobbyName = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
                _pendingLobbyStart = true; // picked up by Render on the SDL thread
            },
            () => _pendingShowMenu = true);
    }

    // Builds the chosen Chess.Net stack on the SDL thread (all renderer/socket objects live here).
    // From the next line on, nothing here knows which courier it got: that is what ILobby is for.
    private void StartLobby()
    {
        var name = _pendingLobbyName;
        new LanProfile(name).Save(platform.DataDirectory);

        if (_pendingLobbyCloud)
        {
            // No discovery hold: the cloud is an HTTPS conversation, not a broadcast. TryCreate can
            // only fail here if the config went missing since the menu was built, since the entry is
            // gated on it — but "no backend" is a state this app returns to the menu for, never an
            // exception.
            _netCloud = CloudPlayStack.TryCreate(cloudConfig, name, _pendingLobbyPreferred);
            _netLobby = _netCloud?.Lobby;
            if (_netLobby is null) { ShowMenu(); return; }
        }
        else
        {
            platform.AcquireLanDiscovery();
            _netLan = new LanPlayStack(name, _pendingLobbyPreferred, TimeProvider.System);
            _netLobby = _netLan.Lobby;
        }

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
                var cloud = _netCloud is not null;
                title = cloud ? "Online Lobby" : "LAN Lobby";
                prompt = _lobbyPeers.Length > 0
                    ? (cloud ? "Tap a game to join:" : "Tap a player to invite:")
                    // The cloud's own posting is already up by the time this shows, so the wait is
                    // not a search — somebody may take it in ten seconds or tomorrow morning.
                    : cloud ? "Your game is posted. Waiting for an opponent…" : "Searching for players…";
                items = [.. _lobbyPeers.Select(p => p.Label), "Back"];
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
        var session = _netLobby?.Session
            ?? throw new InvalidOperationException("The lobby reported Connected with no session.");
        _netSession = session;
        _netLocalSide = session.LocalSide;

        var cloud = _netCloud is not null;
        _netLobby = null;
        _lobbyMenu = null;
        // Tearing the lobby down does NOT close the game's channel: a LAN session owns its socket and
        // a cloud one its subscription, both of which outlive the lobby that produced them.
        if (_netLan is not null) { _ = _netLan.DisposeAsync(); _netLan = null; }
        if (_netCloud is not null) { _ = _netCloud.DisposeAsync(); _netCloud = null; }
        platform.ReleaseLanDiscovery(); // discovery is done; the game socket stays open

        _game = new Game();
        _mode = cloud ? GameMode.OnlineGame : GameMode.NetworkGame;
        _vsComputer = false;
        _acrossTheTable = false; // a LAN game has a single local side — never turns the frame
        var display = new PixelGameDisplay<VulkanContext>(_renderer);
        _display = display;
        UpdateAcrossTheTableTransform(); // identity here + insets/cutout
        display.TopStripLabel = $"{(cloud ? "Online" : "LAN")} ({_netLocalSide})";
        display.KeyboardHints = false;

        // The peer is just another opponent in the shared session: NetworkPlayer drains its moves on
        // its turn and reports a departure as NeedsRestart. Create resets the display, so the UI
        // settings below must follow it.
        _session = GameSession.Create(
            display,
            _mode,
            session.RemoteSide,
            _game.CurrentSide,
            () => _input,
            (_, _) => new NetworkPlayer(session),
            resumeGame: _game); // share the instance — SaveGame and the relay both read _game
        _session.Start(TimeProvider.System);

        // Deliberately NOT MoveLockSide, tempting as it looks now that HandleTap's own turn check is
        // gone. That lock also gates TryPerformAction, which is how NetworkPlayer applies the PEER's
        // move — setting it would reject every move the opponent sends. The desktop's LAN path leaves
        // it unset for the same reason; the lock exists for Chess.Web's link play, where the remote's
        // move arrives as a freshly decoded game rather than through the board. Moving out of turn is
        // already impossible: the rules reject it, and a piece whose side isn't to move won't select.
        display.UI.FlipBoard = _netLocalSide == Side.Black; // local player's pieces at the bottom
    }

    // Sends a just-committed ply to the peer, if it was ours to send. The session applies moves from
    // both sides through the same path, so "ours" is decided after the fact: once our move lands it
    // is the remote's turn. A move the peer sent us is already theirs and must not be echoed back.
    private void RelayIfOurs()
    {
        if (_netSession is null || _game is null || _game.Plies.Count == 0) return;
        if (_game.CurrentSide != _netSession.RemoteSide) return;

        _netSession.SendMove(UciMove.FormatPly(_game.Plies[^1]));
    }

    private void CleanupNetwork()
    {
        _netSession?.Dispose();
        _netSession = null;
        _netLobby = null;
        _lobbyMenu = null;
        if (_netLan is not null) { _ = _netLan.DisposeAsync(); _netLan = null; }
        if (_netCloud is not null) { _ = _netCloud.DisposeAsync(); _netCloud = null; }
        platform.ReleaseLanDiscovery();
        _pendingLobbyStart = false;
        _pendingShowMenu = false;
        _lobbyShownKey = "";
    }

    // The game is persisted to app-internal storage: a header line (mode marker: the computer's side)
    // then the UCI move list. Replaying the moves rebuilds the full position AND history (castling /
    // en-passant rights included) that a bare FEN snapshot would lose.
    private string GamePath => Path.Combine(platform.DataDirectory, "game.uci");

    // Persistence lives in the shared Chess.UCI.GameStore so every front-end (desktop GUI too) uses
    // the same file format and replay logic; these wrappers just supply the path, the computer side
    // (derived from this game's mode state), and the head's log sink.
    private SavedGame? TryLoadGame()
        => GameStore.TryLoad(GamePath, m => SdlEventLoop.DiagnosticLog?.Invoke(m));

    // A custom game saved before its first move is still being SET UP: the position is half-built, so
    // it resumes straight back into setup mode (and the save carries the pieces placed so far).
    private static bool IsMidSetup(SavedGame s) =>
        s.Mode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard && s.Game.PlyCount == 0;

    // Worth resuming when the game isn't over — or when it is still being set up, where "finished"
    // means nothing: a board with no pieces on it has no legal moves and reads as a dead position.
    private static bool IsResumable(SavedGame s) => !s.Game.IsFinished || IsMidSetup(s);

    private void SaveGame()
    {
        if (_game is null) return;
        // A LAN game is never persisted — half of it lives on the peer, so "Continue" could only ever
        // resume a game with nobody on the other end. This used to be implicit (the network tap path
        // simply never called here); now that every mode advances through the same session, it has to
        // be said. The desktop guards the same way, with currentGameIsNetwork.
        //
        // An ONLINE game is skipped too, and that one is a gap rather than a rule: the row IS the
        // whole game, so resuming it is exactly what correspondence play is for. What is missing is
        // somewhere to keep the game id — the save carries a position and a mode, not a courier's
        // handle — and resuming a board with no channel behind it would be worse than not offering
        // it. See docs/correspondence-play.md.
        if (_mode is GameMode.NetworkGame or GameMode.OnlineGame) return;
        var computerSide = _vsComputer ? (_humanSide == Side.White ? Side.Black : Side.White) : Side.None;
        // The mode goes in the save too: across-the-table and plain PvP are both engine-less, so
        // without it a resumed across-the-table game came back as hot-seat and stopped turning.
        GameStore.Save(GamePath, _game, computerSide, _mode, m => SdlEventLoop.DiagnosticLog?.Invoke(m));
    }
}
