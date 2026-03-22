using Chess.GUI;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;

using var sdlWindow = SdlVulkanWindow.Create("Chess", 1050, 830);
sdlWindow.GetSizeInPixels(out var w, out var h);

var ctx = VulkanContext.Create(sdlWindow.Instance, sdlWindow.Surface, (uint)w, (uint)h);
var renderer = new VkRenderer(ctx, (uint)w, (uint)h);
VkStartupMenu? menu = new();
var player = new HumanPlayer();
var bus = new SignalBus();

var cts = new CancellationTokenSource();
VkGameDisplay? display = null;
Task<bool>? gameTask = null;

// Signal handlers
bus.Subscribe<RequestRestartSignal>(_ =>
{
    display?.Dispose();
    display = null;
    gameTask = null;
    menu = new VkStartupMenu();
});

bus.Subscribe<RequestResetSignal>(_ =>
{
    // Reset is handled inside GameLoop via UIResponse.NeedsReset;
    // the signal is available for future decoupling if needed.
});

var loop = new SdlEventLoop(sdlWindow, renderer)
{
    BackgroundColor = new RGBAColor32(0x1a, 0x1a, 0x2e, 0xff),

    OnKeyDown = (inputKey, inputMod) =>
    {
        if (inputKey == InputKey.F11)
        {
            sdlWindow.ToggleFullscreen();
            return true;
        }
        IWidget activeWidget = menu is { IsComplete: false } ? menu : player;
        return activeWidget.HandleKeyDown(inputKey, inputMod);
    },

    OnMouseDown = (button, x, y, _) =>
    {
        if (button != 1) return false;
        IWidget clickTarget = menu is { IsComplete: false } ? menu : player;
        return clickTarget.HandleMouseDown(x, y);
    },

    OnMouseWheel = (scrollY, _, _) =>
        player.HandleMouseWheel(scrollY, 0, 0),

    OnResize = (rw, rh) =>
        display?.OnResize((int)rw, (int)rh),

    CheckNeedsRedraw = () =>
        display is { HasPendingUpdate: true },

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
        else if (menu is { IsComplete: true } && gameTask is null)
        {
            var (gameMode, computerSide, sideToMove) = menu.Result;
            menu = null;

            display = new VkGameDisplay(renderer) { Bus = bus };
            var timeProvider = TimeProvider.System;
            var enginePath = Path.Combine(AppContext.BaseDirectory,
                "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : ""));

            var gameLoop = new GameLoop(
                timeProvider,
                () => display,
                () => player,
                (cs, tp) => new UciPlayer(enginePath, cs, tp)
            );

            gameTask = gameLoop.RunAsync(gameMode, computerSide, sideToMove, cts.Token);
        }
    },

    OnPostFrame = () =>
    {
        bus.ProcessPending();
    }
};

loop.Run(cts.Token);

cts.Cancel();
display?.Dispose();
renderer.Dispose();
ctx.Dispose();

if (gameTask is not null)
{
    try { await gameTask; } catch (OperationCanceledException) { }
}

return 0;
