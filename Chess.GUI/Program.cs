using Chess.GUI;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using SDL3;
using SdlVulkan.Renderer;
using System.Numerics;

// 1280x800 (1.6:1), not the near-square 1050x830 this opened at, for the same reason the web canvas
// asks for that aspect: GameFrameLayout costs its shapes off the proportions it is handed, and the
// flanked/stacked crossover sits at ~1.32:1. A 1.265:1 window lands just inside stacked, where the
// board is height-bound — so it left half its width as empty margin and had a full-width strip for
// its history instead of a gutter. Resizing still re-chooses; this is only where it starts.
using var sdlWindow = SdlVulkanWindow.Create("Chess", 1280, 800);
sdlWindow.GetSizeInPixels(out var w, out var h);

var ctx = VulkanContext.Create(sdlWindow.Instance, sdlWindow.Surface, (uint)w, (uint)h);
var renderer = new VkRenderer(ctx, (uint)w, (uint)h);

// Continue-game save file (shared Chess.UCI.GameStore format) in the user's local app-data.
var savePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SharpAstro.Chess", "game.uci");
Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

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

// The LAN lobby sits between the menu and the game while the user picks/invites a peer.
VkLanLobby? lobby = null;
// A LAN game can't be resumed later (no peer to reconnect), so it's never written to the save.
var currentGameIsNetwork = false;

// A resumable save = present AND not already finished (a finished game isn't worth resuming).
bool CanContinue() => GameStore.TryLoad(savePath) is { } s && !s.Game.IsFinished;

// Persist the in-progress game so "Continue" can resume it later. Nothing worth resuming is
// dropped: an empty game is skipped, and a finished one deletes any stale save (game over).
void SaveCurrentGame()
{
    if (display is null) return;
    if (currentGameIsNetwork) return; // LAN games aren't resumable — never persist them
    var g = display.UI.Game;
    if (g.PlyCount == 0) return;
    if (g.IsFinished)
    {
        try { System.IO.File.Delete(savePath); } catch { /* best-effort */ }
        return;
    }
    GameStore.Save(savePath, g, currentComputerSide, currentGameMode);
}

// A link waiting to become a game — handed over on argv at boot, or pasted mid-game. Declared up here
// because the local functions below capture it, and a local function may only reach locals declared
// above it.
Game? pendingLinkGame = null;
VkStartupMenu? menu = null;

// The ply the current StatusOverride was raised at, so it can be retired once the game moves on —
// see the clear in OnRender. Without it a transient message ("Link copied") would shadow the derived
// status ("Black to move") for the rest of the game, because StatusOverride wins unconditionally.
var statusPly = -1;

// Builds the display and the loop for ONE game and starts it. Extracted because there are now two
// ways in: the wizard's dispatch below, and a link, which has no wizard to come through at all.
void StartGame(GameMode gameMode, Side computerSide, Side sideToMove, Difficulty difficulty, Game? resumeGame)
{
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

    if (display is null)
    {
        StartPendingLinkGame(); // pasted at the menu: straight into the game
        return;
    }

    // Mid-game: warn when this is a DIFFERENT game rather than the reply we were waiting for. The
    // single save slot means the current game is about to be the one that gets kept (the restart
    // handler saves it first), so the swap should never be silent.
    if (display is { HasGameUI: true } && !GameLinkCodec.IsContinuationOf(display.UI.Game, pasted!))
    {
        Console.Error.WriteLine("[chess] pasted link is a different game; the current one was saved.");
    }

    // Unwind the running game the way F8 does, and let the restart handler pick the link back up.
    // Injecting the existing key beats inventing a second teardown path that could drift from it.
    player.HandleInput(new InputEvent.KeyDown(InputKey.F8, InputModifier.None));
}

// A game link handed over on the command line — a browser's "open with", a shell, a file manager, and
// later a chess:// click — skips the wizard entirely, exactly as the web does (Play.razor:323).
// Asking someone to choose a game mode for a game that already has one is a question with a wrong
// answer available. TryDecode takes the raw argument: it reduces a page URL, a chess:// URL or a bare
// body itself (GameLinkCodec.ExtractBody), so there is no argv parser here to drift from the web's.
foreach (var arg in args)
{
    var bootResult = GameLinkCodec.TryDecode(arg, out var decoded, out var linkError);
    if (bootResult is GameLinkResult.Ok)
    {
        pendingLinkGame = decoded;
        break;
    }
    // An argument that IS a link but a broken one is worth saying out loud; one that simply isn't a
    // link (GameLinkResult.NoLink) is not an error — it falls through to the menu silently.
    if (bootResult is GameLinkResult.Invalid)
    {
        Console.Error.WriteLine($"[chess] ignoring '{arg}': {linkError}");
    }
}

// No wizard when a link decided the game for us.
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

    OnKeyDown = (inputKey, inputMod) =>
    {
        if (inputKey == InputKey.F11)
        {
            sdlWindow.ToggleFullscreen();
            return true;
        }
        // Ctrl+L / Ctrl+V — the two halves of carrying a correspondence game by hand. They live here
        // rather than in GameUI's keymap because a clipboard is a HOST capability: Chess.Lib has no
        // SDL, and the browser's clipboard is an async JS call, so the shared keymap must not promise
        // keys that two of the three front-ends cannot honour.
        if ((inputMod & InputModifier.Ctrl) != 0 && inputKey is InputKey.L)
        {
            CopyReplyLink();
            return true;
        }
        if ((inputMod & InputModifier.Ctrl) != 0 && inputKey is InputKey.V)
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
        IWidget activeWidget = menu is { IsComplete: false } ? menu : lobby is not null ? lobby : player;
        return activeWidget.HandleInput(new InputEvent.KeyDown(inputKey, inputMod));
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
        IWidget target = menu is { IsComplete: false } ? menu : lobby is not null ? lobby : player;
        return target.HandleInput(evt);
    },

    OnResize = (rw, rh) =>
        display?.OnResize((int)rw, (int)rh),

    CheckNeedsRedraw = () =>
        display is { HasPendingUpdate: true } || gameTask is { IsCompleted: true } || lobby is not null,

    OnRender = () =>
    {
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
        else if (menu is { IsComplete: true } && gameTask is null)
        {
            var (gameMode, computerSide, sideToMove, difficulty) = menu.Result;

            if (gameMode is GameMode.NetworkGame)
            {
                // Hand off to the LAN lobby; the game starts once a peer connects (handled above).
                // ComputerSide is the remote peer's colour, so our preferred colour is the opposite.
                var preferredColor = computerSide == Side.White ? Side.Black : Side.White;
                lobby = new VkLanLobby(renderer, Path.GetDirectoryName(savePath)!, preferredColor);
                menu = null;
            }
            else
            {
                menu = null;

                // Continue: the save (not the wizard) defines the real mode and computer side; load it
                // and hand the loaded game to the loop so its full history drives both display and engine.
                Game? resumeGame = null;
                if (gameMode is GameMode.Continue)
                {
                    if (GameStore.TryLoad(savePath) is { } saved)
                    {
                        resumeGame = saved.Game;
                        computerSide = saved.ComputerSide;
                        sideToMove = saved.Game.CurrentSide;
                        // The save carries the mode; a resumed custom game is already set up, so it
                        // continues as a normal game against the engine rather than re-entering setup.
                        gameMode = saved.Mode is GameMode.CustomGameEmpty or GameMode.CustomGameStandardBoard
                            ? (saved.ComputerSide == Side.None ? GameMode.PlayerVsPlayer : GameMode.PlayerVsComputer)
                            : saved.Mode;
                    }
                    else
                    {
                        gameMode = GameMode.PlayerVsPlayer; // nothing to resume -> plain hot-seat
                    }
                }
                StartGame(gameMode, computerSide, sideToMove, difficulty, resumeGame);
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
    : menu?.InspectorWidget;
using var inspector = DebugInspector.Attach(loop, new DebugInspectorOptions
{
    AppName = "Chess.GUI",
    WindowTitle = () => "Chess",
    GetRegions = () => ActiveInspectorWidget()?.GetRegisteredRegions() ?? [],
    GetLayout = () => ActiveInspectorWidget()?.GetCapturedLayout() ?? [],
    AppState = s =>
    {
        s.Set("screen", display is not null ? "game" : lobby is not null ? "lobby" : "menu");
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
