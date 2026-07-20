using Chess.GUI;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;

using var sdlWindow = SdlVulkanWindow.Create("Chess", 1050, 830);
sdlWindow.GetSizeInPixels(out var w, out var h);

var ctx = VulkanContext.Create(sdlWindow.Instance, sdlWindow.Surface, (uint)w, (uint)h);
var renderer = new VkRenderer(ctx, (uint)w, (uint)h);

// Continue-game save file (shared Chess.UCI.GameStore format) in the user's local app-data.
var savePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SharpAstro.Chess", "game.uci");
Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

var player = new HumanPlayer();
var bus = new SignalBus();

var cts = new CancellationTokenSource();
PixelGameDisplay<VulkanContext>? display = null;
Task<bool>? gameTask = null;
var currentComputerSide = Side.None;

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
    GameStore.Save(savePath, g, currentComputerSide);
}

VkStartupMenu? menu = new(CanContinue());

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
        IWidget activeWidget = menu is { IsComplete: false } ? menu : lobby is not null ? lobby : player;
        return activeWidget.HandleInput(new InputEvent.KeyDown(inputKey, inputMod));
    },

    OnMouseDown = (button, x, y, _, _) =>
    {
        if (button != 1) return false;
        IWidget clickTarget = menu is { IsComplete: false } ? menu : lobby is not null ? lobby : player;
        return clickTarget.HandleInput(new InputEvent.MouseDown(x, y));
    },

    OnMouseWheel = (scrollY, _, _) =>
        player.HandleInput(new InputEvent.Scroll(scrollY, 0, 0)),

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
                currentComputerSide = session.RemoteSide;
                display = new PixelGameDisplay<VulkanContext>(renderer) { Bus = bus };

                var netLoop = new GameLoop(
                    TimeProvider.System,
                    () => display,
                    () => new LocalNetworkPlayer(player, session),
                    (_, _) => new NetworkPlayer(session)
                );

                gameTask = netLoop.RunAsync(GameMode.NetworkGame, session.RemoteSide, Side.White, cts.Token);
            }
            else if (lobby.IsAborted)
            {
                lobby.Dispose();
                lobby = null;
                menu = new VkStartupMenu(CanContinue());
            }
            else
            {
                lobby.Render(renderer);
            }
        }
        else if (menu is { IsComplete: true } && gameTask is null)
        {
            var (gameMode, computerSide, sideToMove) = menu.Result;

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
                currentGameIsNetwork = false;

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
                        gameMode = saved.ComputerSide == Side.None ? GameMode.PlayerVsPlayer : GameMode.PlayerVsComputer;
                    }
                    else
                    {
                        gameMode = GameMode.PlayerVsPlayer; // nothing to resume -> plain hot-seat
                    }
                }
                currentComputerSide = computerSide;

                display = new PixelGameDisplay<VulkanContext>(renderer) { Bus = bus };
                var timeProvider = TimeProvider.System;

                var gameLoop = new GameLoop(
                    timeProvider,
                    () => display,
                    () => player,
                    (cs, tp) => new UciPlayer(UciPlayer.DefaultEnginePath, cs, tp)
                );

                gameTask = gameLoop.RunAsync(gameMode, computerSide, sideToMove, cts.Token, resumeGame);
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
