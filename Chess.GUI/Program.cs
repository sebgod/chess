using Chess.GUI;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using SDL3;
using SdlVulkan.Renderer;
using SharpAstro.AppShell;
using System.Numerics;

// ---- Before any window exists: the two jobs that must not open one. --------------------------

// Registering the scheme is an explicit action with an explicit result, never a silent write on first
// run -- see ProtocolRegistration for why an app that ships as a folder cannot claim a URL scheme on
// the user's behalf.
if (args.Contains("--register-protocol")) return ProtocolRegistration.Register();
if (args.Contains("--unregister-protocol")) return ProtocolRegistration.Unregister();

// A link waiting to become a game -- handed over on argv at boot, arriving from another instance, or
// pasted mid-game. Decoded HERE, before the window, because it may belong to an instance that is
// already running and this one would otherwise flash a window on its way to exiting. TryDecode takes
// the raw argument: it reduces a page URL, a chess:// URL or a bare body itself
// (GameLinkCodec.ExtractBody), so there is no argv parser here to drift from the web's.
string? linkPayload = null;
Game? pendingLinkGame = null;

foreach (var arg in args)
{
    var bootResult = GameLinkCodec.TryDecode(arg, out var decoded, out var bootLinkError);
    if (bootResult is GameLinkResult.Ok)
    {
        linkPayload = arg;
        pendingLinkGame = decoded;
        break;
    }
    // An argument that IS a link but a broken one is worth saying out loud; one that simply isn't a
    // link (GameLinkResult.NoLink) is not an error -- it falls through to the menu silently.
    if (bootResult is GameLinkResult.Invalid)
    {
        Console.Error.WriteLine($"[chess] ignoring '{arg}': {bootLinkError}");
    }
}

// Single instance, but only just. Claiming the channel is what makes THIS window reachable, and
// LOSING the claim is not a reason to exit: two windows on one machine is a scenario chess supports
// on purpose -- Chess.Net mints a per-process peer id precisely so two local windows can find each
// other over LAN, and the inspector workflows want fresh instances too. So the whole policy is:
// always claim, and hand off ONLY when carrying a payload.
var instanceChannel = InstanceGate.ChannelFor("sharpastro-chess");

if (linkPayload is not null
    && InstanceGate.TryHandOff(instanceChannel, linkPayload, TimeSpan.FromSeconds(2)))
{
    // Another instance owns the channel and has taken the link; it surfaces itself. A FAILED hand-off
    // is never fatal -- we fall through and open the link here, because an extra window is a poor
    // outcome and a click that does nothing is an unacceptable one.
    return 0;
}

// Null means somebody else owns the pipe. That is fine: this process simply cannot RECEIVE hand-offs,
// and everything else about it works.
using var instanceGate = InstanceGate.TryClaim(instanceChannel);

// 1280x800 (1.6:1), not the near-square 1050x830 this opened at, for the same reason the web canvas
// asks for that aspect: GameFrameLayout costs its shapes off the proportions it is handed, and the
// flanked/stacked crossover sits at ~1.32:1. A 1.265:1 window lands just inside stacked, where the
// board is height-bound — so it left half its width as empty margin and had a full-width strip for
// its history instead of a gutter. Resizing still re-chooses; this is only where it starts.
using var sdlWindow = SdlVulkanWindow.Create("Chess", 1280, 800);
sdlWindow.GetSizeInPixels(out var w, out var h);

var ctx = VulkanContext.Create(sdlWindow.Instance, sdlWindow.Surface, (uint)w, (uint)h);
var renderer = new VkRenderer(ctx, (uint)w, (uint)h);

// Saved games (shared Chess.UCI format) in the user's local app-data. A DIRECTORY of them since
// phase 2: correspondence play means several games can be in flight at once, and the old single
// slot silently evicted one when you started another.
var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpAstro.Chess");
Directory.CreateDirectory(dataDir);

// One-time move of any pre-inbox game.uci into the inbox. Idempotent, so it costs one File.Exists
// on every later launch and nothing else.
GameInbox.MigrateLegacySave(dataDir, TimeProvider.System.GetUtcNow());

// Where a copied reply link points. The web app, deliberately — see CopyReplyLink.
const string LinkShareBaseUrl = "https://sebgod.github.io/chess/";

var player = new HumanPlayer();
var bus = new SignalBus();

var cts = new CancellationTokenSource();
PixelGameDisplay<VulkanContext>? display = null;

// The display draws a "▶ Start" chip in its history header whenever GameUI is in setup mode, and
// that is every PIXEL host — not just the touch one it was added for. The desktop never subscribed,
// so the chip rendered as a button with no handler and setup could only be left by pressing s,
// which is the very thing a visible button exists to spare you from having to know.
void FinishSetup()
{
    // HasGameUI first: UI throws before the first ResetGame, and a property pattern short-circuits.
    if (display is not { HasGameUI: true, UI.IsSetupMode: true }) return;
    // Same one line the s key runs. GameLoop is parked in its setup pump polling this exact flag,
    // so lowering it here is all the hand-off there is.
    display.UI.IsSetupMode = false;
}
Task<bool>? gameTask = null;
var currentComputerSide = Side.None;
// Saved alongside the game: PvP and across-the-table are both engine-less, so the computer side alone
// can't say which mode to resume into.
var currentGameMode = GameMode.PlayerVsPlayer;

// Which inbox entry the running game is saved as. Minted per game by StartGame, or carried over
// from the picker when resuming, so every save of one game overwrites that game rather than
// accumulating a new file per move.
var currentGameId = "";
// When the running game last actually MOVED, and how many plies it had when we opened it. Saving
// must not restamp a game just because it was looked at: "last move" drives both the picker's
// "today / 3 weeks ago" and the staleness that eventually retires a game, so opening an abandoned
// game to glance at it would report it as freshly played and keep it alive forever.
var currentGameLastMove = DateTimeOffset.MinValue;
var currentGamePlyAtOpen = 0;
// When the running game began. Never changes once set, because it is half of the game's NAME
// ("Ada - You (12 Sep)") and a name that moves is not a name.
var currentGameStarted = DateTimeOffset.MinValue;
// The opponent's display name, when we have one. Link play does not exchange names (nothing in the
// fragment carries one), so it stays empty and the picker falls back to describing the mode.
var currentOpponent = "";

// The game picker sits between the menu and the game while the user chooses which save to resume.
VkGamePicker? picker = null;

// The LAN lobby sits between the menu and the game while the user picks/invites a peer.
VkLanLobby? lobby = null;
// A LAN game can't be resumed later (no peer to reconnect), so it's never written to the save.
var currentGameIsNetwork = false;

// Anything in the inbox is worth offering, finished games included: the picker labels them, and
// reviewing a game you just lost is a reason to open it. Staleness (not this) is what eventually
// stops a game being listed.
bool CanContinue() => GameInbox.Load(dataDir).Count > 0;

// Persist the in-progress game so "Continue" can resume it later. Nothing worth resuming is
// dropped: an empty game is skipped, and a finished one deletes any stale save (game over).
void SaveCurrentGame()
{
    if (display is null) return;
    if (currentGameIsNetwork) return; // LAN games aren't resumable — never persist them
    var g = display.UI.Game;
    if (g.PlyCount == 0) return;

    // A finished game is KEPT now, where the single-slot store deleted it. With one slot that was
    // the only way to stop a dead game blocking the next one; with an inbox it would throw away the
    // record of a game you might want to look at, and staleness already retires it in time.
    // Stamp "now" only if a ply was actually committed this session; otherwise keep what the entry
    // already said.
    var stamp = g.PlyCount > currentGamePlyAtOpen ? TimeProvider.System.GetUtcNow() : currentGameLastMove;

    GameInbox.Save(dataDir, currentGameId, g, currentComputerSide, currentGameMode, currentOpponent,
        stamp, started: currentGameStarted);
}

VkStartupMenu? menu = null;

// Set by the drain in CheckNeedsRedraw, consumed by the next OnRender. The two are separate because
// the drain runs in a phase where nothing may touch the display -- and because a minimized window
// reaches the drain but not the render, which is the entire point of the arrangement below.
var handoffArrived = false;

// The ply the current StatusOverride was raised at, so it can be retired once the game moves on —
// see the clear in OnRender. Without it a transient message ("Link copied") would shadow the derived
// status ("Black to move") for the rest of the game, because StatusOverride wins unconditionally.
var statusPly = -1;

// Builds the display and the loop for ONE game and starts it. Extracted because there are now two
// ways in: the wizard's dispatch below, and a link, which has no wizard to come through at all.
void StartGame(GameMode gameMode, Side computerSide, Side sideToMove, Difficulty difficulty,
    Game? resumeGame, string? resumeId = null, DateTimeOffset? resumeLastMove = null,
    DateTimeOffset? resumeStarted = null)
{
    // A resumed game keeps its id so it saves back over itself; a fresh one gets a new slot.
    currentGameId = resumeId ?? GameInbox.NewId(TimeProvider.System.GetUtcNow());
    currentGamePlyAtOpen = resumeGame?.PlyCount ?? 0;
    currentGameLastMove = resumeLastMove ?? TimeProvider.System.GetUtcNow();
    currentGameStarted = resumeStarted ?? TimeProvider.System.GetUtcNow();
    currentGameIsNetwork = false;
    currentComputerSide = computerSide;
    currentGameMode = gameMode;

    display = new PixelGameDisplay<VulkanContext>(renderer) { Bus = bus, SetupStartRequested = FinishSetup };
    statusPly = -1;

    // Play by Link needs no branch of its own: GameSession builds an opponent only for the modes that
    // have one (PvC, custom, LAN), so the engine factory below is simply never called for a link game
    // — no chess-engine process is spawned — and the session arms the one-local-move gate itself.
    // Continue resumes one too: GameStore already persists the mode and the correspondent's colour,
    // which is all a link game is.
    var gameLoop = new GameLoop(
        TimeProvider.System,
        () => display,
        () => player,
        (cs, tp) => new UciPlayer(UciPlayer.DefaultEnginePath, cs, tp, difficulty)
    );

    gameTask = gameLoop.RunAsync(gameMode, computerSide, sideToMove, cts.Token, resumeGame);
}

// Turns a decoded link into the running game. "Receiving a link means it's your turn", so the
// correspondent is whoever is NOT to move — GameSession.CorrespondentSideFor states that rule once
// rather than letting each front-end re-derive the inversion.
void StartPendingLinkGame()
{
    if (pendingLinkGame is not { } linked) return;
    pendingLinkGame = null;
    menu = null;
    StartGame(GameMode.PlayByLink, GameSession.CorrespondentSideFor(linked), linked.CurrentSide,
        Difficulty.Normal, linked);
}

// Nudge the display into repainting after something only the host knows about changed (a status
// message). HasPendingUpdate is read-and-clear and RenderInitial is how the rest of the app raises
// it; neither paints here — the next OnRender does.
void RequestStatus(string message)
{
    // No board on screen (the menu) means no status bar to write to, so say it where it can still be
    // diagnosed rather than swallowing it — a paste that reports nothing at all is the worst outcome.
    if (display is not { HasGameUI: true })
    {
        Console.Error.WriteLine($"[chess] {message}");
        return;
    }

    display.StatusOverride = message;
    statusPly = display.UI.Game.PlyCount;
    display.RenderInitial(display.UI.Game);
}

// Copy the reply link for the game as it stands. Deliberately the WEB url, not a chess:// one: the
// person receiving it may have nothing installed, and sebgod.github.io/chess plays the same game in
// any browser — while this app reads that shape back happily (GameLinkCodec.ExtractBody). A link that
// only works for people who already have the app is not a correspondence link.
void CopyReplyLink()
{
    if (display is not { HasGameUI: true } || currentGameMode is not GameMode.PlayByLink) return;

    var url = LinkShareBaseUrl + GameLinkCodec.EncodeFragment(display.UI.Game);
    RequestStatus(SDL.SetClipboardText(url)
        ? "Link copied — send it to your opponent"
        : "Couldn't reach the clipboard");
}

// Take the opponent's reply (or a fresh invitation) off the clipboard. Always restarts the session
// from the decoded game rather than applying the missing plies to the live board: the link IS the
// whole game, replaying it re-validates every ply through the rules engine, and the one-local-move
// gate would refuse a correspondent's move anyway. Chess.Web does exactly the same thing.
void PasteLink()
{
    if (!SDL.HasClipboardText())
    {
        RequestStatus("Clipboard is empty");
        return;
    }

    var result = GameLinkCodec.TryDecode(SDL.GetClipboardText(), out var pasted, out var linkError);

    if (result is GameLinkResult.NoLink)
    {
        RequestStatus("That doesn't look like a game link");
        return;
    }

    if (result is GameLinkResult.Invalid)
    {
        RequestStatus($"Invalid link: {linkError}");
        return;
    }

    pendingLinkGame = pasted;
    AcceptPendingLink("pasted");
}

// Put whatever is in pendingLinkGame on screen, from wherever it came -- the clipboard, argv, or
// another instance handing it over. Shared because the three arrivals differ only in how the link
// reached the process; what has to happen to the running game is identical, and a second teardown
// path is a second thing to drift.
void AcceptPendingLink(string source)
{
    if (pendingLinkGame is not { } incoming) return;

    if (display is null)
    {
        StartPendingLinkGame(); // at the menu: straight into the game
        return;
    }

    // Mid-game: warn when this is a DIFFERENT game rather than the reply we were waiting for. The
    // single save slot means the current game is about to be the one that gets kept (the restart
    // handler saves it first), so the swap should never be silent.
    if (display is { HasGameUI: true } && !GameLinkCodec.IsContinuationOf(display.UI.Game, incoming))
    {
        Console.Error.WriteLine($"[chess] {source} link is a different game; the current one was saved.");
    }

    // Unwind the running game the way F8 does, and let the restart handler pick the link back up.
    // Injecting the existing key beats inventing a second teardown path that could drift from it.
    player.HandleInput(new InputEvent.KeyDown(InputKey.F8, InputModifier.None));
}

// A link on argv skips the wizard entirely, exactly as the web does (Play.razor:323). Asking someone
// to choose a game mode for a game that already has one is a question with a wrong answer available.
menu = pendingLinkGame is null ? new(CanContinue()) : null;

// Map a pointer event's pixel coordinates from device space into content space through the renderer's
// ContentTransform — the inverse of what the projection applies, so draw and hit-test can never drift
// (the whole-frame analogue of GameUI's DisplayCell/LogicalCell). Identity — all the desktop sets
// today — returns the event untouched; this exists so any front-end that wires a whole-frame rotation
// (the Android across-the-table flip) stays correct by construction.
InputEvent MapPointerToContent(InputEvent evt)
{
    var m = renderer.ContentTransform;
    if (m.IsIdentity) return evt;
    switch (evt)
    {
        case InputEvent.MouseDown e:
        {
            var p = m.Invert(new Vector2(e.X, e.Y));
            return e with { X = p.X, Y = p.Y };
        }
        case InputEvent.MouseUp e:
        {
            var p = m.Invert(new Vector2(e.X, e.Y));
            return e with { X = p.X, Y = p.Y };
        }
        case InputEvent.MouseMove e:
        {
            var p = m.Invert(new Vector2(e.X, e.Y));
            return e with { X = p.X, Y = p.Y };
        }
        case InputEvent.Scroll e:
        {
            var p = m.Invert(new Vector2(e.X, e.Y));
            return e with { X = p.X, Y = p.Y };
        }
        case InputEvent.Pinch e:
        {
            var p = m.Invert(new Vector2(e.X, e.Y));
            return e with { X = p.X, Y = p.Y };
        }
        default:
            return evt; // KeyDown / TextInput / PinchEnd carry no coordinates
    }
}

var loop = new SdlEventLoop(sdlWindow, renderer)
{
    // Same background the game display paints with — a re-typed literal here would band.
    BackgroundColor = PixelGameDisplay<VulkanContext>.Background,

    // SdlVulkan.Renderer 7.33 collapsed the two arguments into the event itself, which also carries
    // whether the OS is auto-repeating a held key.
    OnKeyDown = keyEvent =>
    {
        var (inputKey, inputMod) = keyEvent;
        var isCtrl = (inputMod & InputModifier.Ctrl) != 0;

        // Everything THIS host handles directly is a one-shot, and a repeat of one is never wanted:
        // held F11 would strobe the window between fullscreen and not, and a repeating Ctrl+V would
        // restart the game several times a second. Named precisely rather than blocking all repeats
        // with a modifier held — Ctrl+Arrow is history navigation, a STEP, and stepping is exactly
        // what auto-repeat is for. PageUp/PageDown and the widgets below are steps too, so they fall
        // through untouched.
        if (keyEvent.Repeat && (inputKey is InputKey.F11 || (isCtrl && inputKey is InputKey.L or InputKey.V)))
        {
            return true;
        }

        if (inputKey == InputKey.F11)
        {
            sdlWindow.ToggleFullscreen();
            return true;
        }
        // Ctrl+L / Ctrl+V — the two halves of carrying a correspondence game by hand. They live here
        // rather than in GameUI's keymap because a clipboard is a HOST capability: Chess.Lib has no
        // SDL, and the browser's clipboard is an async JS call, so the shared keymap must not promise
        // keys that two of the three front-ends cannot honour.
        if (isCtrl && inputKey is InputKey.L)
        {
            CopyReplyLink();
            return true;
        }
        if (isCtrl && inputKey is InputKey.V)
        {
            PasteLink();
            return true;
        }
        // Page the history panel directly on this thread (its scroll model lives in the display).
        if (display is not null && inputKey is InputKey.PageUp or InputKey.PageDown)
        {
            display.PageHistory(inputKey == InputKey.PageUp ? -1 : 1);
            return true; // display.HasPendingUpdate (set by PageHistory) drives the redraw
        }
        IWidget activeWidget = menu is { IsComplete: false } ? menu
            : lobby is not null ? lobby
            : picker is not null ? picker
            : player;
        return activeWidget.HandleInput(keyEvent);
    },

    // One unified pointer path (SdlVulkan.Renderer 6.28) replaces the separate OnMouseDown +
    // OnMouseWheel callbacks. Every press/move/release/scroll arrives here as an InputEvent and is
    // forwarded whole to the active screen (menu → lobby → player). The wheel now carries its real
    // (x, y) — it used to arrive as (0, 0), so the history panel had no position to hit-test.
    OnPointerInput = evt =>
    {
        // Device → content: every consumer below (history panel, menu, board) hit-tests in content
        // space, so map the event before any dispatch. Identity transform = returned untouched.
        evt = MapPointerToContent(evt);
        // The UI only acts on the primary button, matching the old `button != 1` guard.
        if (evt is InputEvent.MouseDown(_, _, not MouseButton.Left, _, _)
                or InputEvent.MouseUp(_, _, not MouseButton.Left))
        {
            return false;
        }
        // History scroll (wheel over the panel + scrollbar thumb/track drag) is served directly by
        // the display on this thread — bypassing the per-move queue so it stays smooth and never
        // races game state. Row taps aren't claimed here; they fall through to click-to-navigate.
        if (display is not null && display.HandleHistoryPointer(evt))
        {
            return true; // display.HasPendingUpdate (set on consume) drives the redraw
        }
        // The setup drag ghost: same thread, same reasoning as the history drag above. It claims
        // motion ONLY while a piece is in hand, so the menu and the lobby keep the hover stream they
        // resolve out of these same events.
        if (display is not null && display.HandleDragPointer(evt))
        {
            return true;
        }
        IWidget target = menu is { IsComplete: false } ? menu
            : lobby is not null ? lobby
            : picker is not null ? picker
            : player;
        return target.HandleInput(evt);
    },

    OnResize = (rw, rh) =>
        display?.OnResize((int)rw, (int)rh),

    // This is the drain, and its placement is the non-obvious part of the whole feature.
    //
    // SdlEventLoop computes `anyNeedsRedraw` EXCLUDING minimized windows and then parks in
    // WaitEventTimeout -- so a minimized window never renders, and OnRender/OnBeforeFrame/OnPostFrame
    // never run. A minimized window is precisely the state a hand-off exists to rescue, so draining
    // in the render path would apply the link whenever the user next happened to click the app: from
    // their point of view, never. CheckNeedsRedraw is the one public per-iteration hook that runs
    // regardless (OnLoopIteration is internal AND #if DEBUG, so it is compiled out of a shipping
    // build). Activating the window is what lets the render resume and the payload be applied.
    CheckNeedsRedraw = () =>
    {
        // TryDequeue is the gate's only reader -- there is no HasPending -- so this has to take the
        // payload and stash it rather than peek and leave it for the render.
        if (instanceGate is not null && instanceGate.TryDequeue(out var handoff))
        {
            if (GameLinkCodec.TryDecode(handoff.Payload, out var handed, out var handoffError)
                is GameLinkResult.Ok)
            {
                pendingLinkGame = handed;
                handoffArrived = true;
            }
            else
            {
                Console.Error.WriteLine($"[chess] ignoring handed-off link: {handoffError}");
            }

            // Come forward whatever the payload turned out to be: somebody clicked a link expecting
            // this app, and a window that stays buried is indistinguishable from a click that did
            // nothing. Activate restores ONLY when actually minimized, so a maximised window keeps
            // its size.
            sdlWindow.Activate();
            return true;
        }

        return display is { HasPendingUpdate: true } || gameTask is { IsCompleted: true }
            || lobby is not null || picker is not null;
    },

    OnRender = () =>
    {
        // A link that arrived from another instance, stashed by the drain above.
        if (handoffArrived)
        {
            handoffArrived = false;
            AcceptPendingLink("handed-off");
        }

        // Check if game requested restart (back to menu)
        if (gameTask is { IsCompleted: true } completed)
        {
            var restart = false;
            try { restart = completed.Result; } catch (AggregateException) { }

            if (restart)
            {
                bus.Post(new RequestRestartSignal());
            }
            else
            {
                gameTask = null;
            }
        }

        if (display is not null)
        {
            // Retire a transient host message once the game has moved past the ply it belonged to.
            if (display is { HasGameUI: true, StatusOverride: not null } && display.UI.Game.PlyCount != statusPly)
            {
                display.StatusOverride = null;
            }

            display.Render();
        }
        else if (menu is { IsComplete: false })
        {
            menu.Render(renderer);
        }
        else if (lobby is not null)
        {
            if (lobby.IsConnected)
            {
                // A peer connected: take the session (its socket outlives the lobby) and start a LAN
                // game — the local human relays each move, the remote peer IS the "engine" opponent.
                var session = lobby.Session!;
                lobby.Dispose();
                lobby = null;

                currentGameIsNetwork = true;
                display = new PixelGameDisplay<VulkanContext>(renderer) { Bus = bus, SetupStartRequested = FinishSetup };

                var (netLoop, computerSide, sideToMove) =
                    NetworkGame.CreateLoop(TimeProvider.System, () => display, () => player, session);
                currentComputerSide = computerSide;
                currentGameMode = GameMode.NetworkGame;

                gameTask = netLoop.RunAsync(GameMode.NetworkGame, computerSide, sideToMove, cts.Token);
            }
            else if (lobby.IsAborted)
            {
                lobby.Dispose();
                lobby = null;
                menu = new VkStartupMenu(CanContinue());
                // Paint the fresh menu in THIS frame: once lobby is null the CheckNeedsRedraw
                // predicate goes false, so SDL parks in WaitEventTimeout and no further frame comes
                // until the next input — without this the menu would stay invisible (blank window)
                // until the user pressed a key. (The earlier menu branch already ran this frame with
                // menu still null, so it won't repaint on its own.)
                menu.Render(renderer);
            }
            else
            {
                lobby.Render(renderer);
            }
        }
        else if (picker is not null)
        {
            if (picker.Picked is { } chosen)
            {
                // The entry carries everything the loop needs: the replayed game, whose colour the
                // other player has, and the mode it was started in. A resumed custom game is already
                // set up, so it continues as a normal game rather than re-entering piece placement.
                var resumedMode = chosen.Mode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard
                    ? (chosen.ComputerSide == Side.None ? GameMode.PlayerVsPlayer : GameMode.PlayerVsComputer)
                    : chosen.Mode;

                currentOpponent = chosen.Opponent;
                picker = null;
                StartGame(resumedMode, chosen.ComputerSide, chosen.Game.CurrentSide, Difficulty.Normal,
                    chosen.Game, chosen.Id, chosen.LastMove, chosen.Started);
            }
            else if (picker.IsAborted)
            {
                picker = null;
                menu = new VkStartupMenu(CanContinue());
                // Paint the fresh menu in THIS frame — same reason as the lobby's abort path: once
                // picker is null the redraw predicate goes false and SDL parks until the next input.
                menu.Render(renderer);
            }
            else
            {
                picker.Render(renderer);
            }
        }
        else if (menu is { IsComplete: true } && gameTask is null)
        {
            var (gameMode, computerSide, sideToMove, difficulty) = menu.Result;

            if (gameMode is GameMode.NetworkGame)
            {
                // Hand off to the LAN lobby; the game starts once a peer connects (handled above).
                // ComputerSide is the remote peer's colour, so our preferred colour is the opposite.
                var preferredColor = computerSide == Side.White ? Side.Black : Side.White;
                lobby = new VkLanLobby(renderer, dataDir, preferredColor);
                menu = null;
            }
            else if (gameMode is GameMode.Continue)
            {
                // Which save to resume is now a choice, so it gets a screen of its own rather than
                // the wizard silently loading the only slot there used to be.
                picker = new VkGamePicker(dataDir, TimeProvider.System);
                menu = null;
            }
            else
            {
                menu = null;
                StartGame(gameMode, computerSide, sideToMove, difficulty, resumeGame: null);
            }
        }
    },

    OnPostFrame = () =>
    {
        bus.ProcessPending();
    }
};

// Signal handlers (placed after `loop` so they can poke it for a redraw).
bus.Subscribe<RequestRestartSignal>(_ =>
{
    // Save before tearing down so "Continue" on the menu can resume this exact game. The game task
    // has completed by now (F8/Esc -> NeedsRestart -> RunAsync returned), so reading its final
    // state here is race-free.
    SaveCurrentGame();
    display?.Dispose();
    display = null;
    gameTask = null;

    // A link pasted mid-game unwound us through this same path; resume into IT, not the menu.
    if (pendingLinkGame is not null)
    {
        StartPendingLinkGame();
        loop.RequestRedraw();
        return;
    }

    menu = new VkStartupMenu(CanContinue());
    // Display→menu state swap happens during OnPostFrame, after this frame's
    // render. Without an explicit nudge, SDL would park in WaitEventTimeout
    // until the next input event, leaving the menu invisible until then.
    loop.RequestRedraw();
});

bus.Subscribe<RequestResetSignal>(_ =>
{
    // Reset is handled inside GameLoop via UIResponse.NeedsReset;
    // the signal is available for future decoupling if needed.
});

#if DEBUG
// Live UI debug inspector (DEBUG only — compiled out of Release, and the renderer only carries
// DebugInspector in its own DEBUG build). Exposes this process to the SdlVulkan.Renderer.Inspector
// MCP sidecar / any TCP driver: read the widget tree (describe/describeLayout), screenshot, inject
// input, and read a curated state snapshot. Mirrors tianwen's wiring — the machinery lives in the
// framework; this block is the only glue, aggregating the active screen's regions + captured layout.
PixelWidgetBase<VulkanContext>? ActiveInspectorWidget() =>
    display is not null ? display
    : lobby is not null ? lobby.InspectorWidget
    : picker is not null ? picker.InspectorWidget
    : menu?.InspectorWidget;
using var inspector = DebugInspector.Attach(loop, new DebugInspectorOptions
{
    AppName = "Chess.GUI",
    WindowTitle = () => "Chess",
    GetRegions = () => ActiveInspectorWidget()?.GetRegisteredRegions() ?? [],
    GetLayout = () => ActiveInspectorWidget()?.GetCapturedLayout() ?? [],
    AppState = s =>
    {
        s.Set("screen",
            display is not null ? "game"
            : lobby is not null ? "lobby"
            : picker is not null ? "picker"
            : "menu");
        if (picker is not null)
        {
            s.Set("pickerRows", picker.RowCount);
        }
        if (lobby is not null)
        {
            s.Set("lobbyState", lobby.State.ToString());
            s.Set("peers", string.Join(", ", lobby.Peers.Select(p => p.DisplayName)));
        }
        if (display is not null)
        {
            var g = display.UI.Game;
            s.Set("sideToMove", g.CurrentSide.ToString());
            s.Set("plyCount", g.PlyCount);
            s.Set("finished", g.IsFinished);
            s.Set("networkGame", currentGameIsNetwork);
        }
    },
});
#endif

// The link that came in on argv, now that the loop and its handlers exist. "Receiving a link means
// it's your turn", so the correspondent is whoever is NOT to move — GameSession states that rule once
// (CorrespondentSideFor) rather than each front-end re-deriving the inversion.
StartPendingLinkGame();

loop.Run(cts.Token);

// Persist an in-progress game on exit too, so closing the window and relaunching offers Continue.
SaveCurrentGame();
cts.Cancel();
lobby?.Dispose(); // tears down discovery/sockets if the user quit while still in the lobby
display?.Dispose();
renderer.Dispose();
ctx.Dispose();

if (gameTask is not null)
{
    try { await gameTask; } catch (OperationCanceledException) { }
}

return 0;
