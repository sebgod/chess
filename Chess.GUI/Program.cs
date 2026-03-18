using Chess.GUI;
using Chess.Lib.UI;
using Chess.UCI;
using SdlVulkan.Renderer;
using static SDL3.SDL;

using var sdlWindow = SdlVulkanWindow.Create("Chess", 1050, 830);
sdlWindow.GetSizeInPixels(out var w, out var h);

var ctx = VulkanContext.Create(sdlWindow.Instance, sdlWindow.Surface, (uint)w, (uint)h);
var renderer = new VkRenderer(ctx, (uint)w, (uint)h);
var menu = new VkStartupMenu();
var player = new HumanPlayer();

var cts = new CancellationTokenSource();
VkGameDisplay? display = null;
Task? gameTask = null;
var needsRedraw = true;

var running = true;
while (running)
{
    Event evt;
    var hadEvent = needsRedraw
        ? PollEvent(out evt)
        : WaitEventTimeout(out evt, 16);

    if (hadEvent)
    {
        do
        {
            switch ((EventType)evt.Type)
            {
                case EventType.Quit:
                    running = false;
                    break;

                case EventType.WindowResized:
                case EventType.WindowPixelSizeChanged:
                    sdlWindow.GetSizeInPixels(out var rw, out var rh);
                    if (rw > 0 && rh > 0)
                    {
                        renderer.Resize((uint)rw, (uint)rh);
                        display?.OnResize(rw, rh);
                    }
                    needsRedraw = true;
                    break;

                case EventType.WindowExposed:
                    needsRedraw = true;
                    break;

                case EventType.KeyDown:
                    var scancode = evt.Key.Scancode;
                    var keymod = evt.Key.Mod;

                    if (scancode == Scancode.F11)
                    {
                        sdlWindow.ToggleFullscreen();
                        break;
                    }

                    if (menu is { IsComplete: false })
                        menu.HandleKey(scancode);
                    else
                        player.EnqueueKeyDown(scancode, keymod);
                    needsRedraw = true;
                    break;

                case EventType.MouseButtonDown:
                    if (evt.Button.Button == 1) // Left
                    {
                        var mx = (int)evt.Button.X;
                        var my = (int)evt.Button.Y;
                        if (menu is { IsComplete: false })
                            menu.HandleClick(mx, my, renderer.Width, renderer.Height);
                        else
                            player.EnqueueMouseDown(mx, my);
                        needsRedraw = true;
                    }
                    break;

                case EventType.MouseWheel:
                    player.EnqueueScroll((int)evt.Wheel.Y);
                    needsRedraw = true;
                    break;
            }
        } while (PollEvent(out evt));
    }

    // Check if the game display has been updated by the game loop thread
    if (display is { HasPendingUpdate: true })
        needsRedraw = true;

    if (!needsRedraw)
        continue;
    needsRedraw = false;

    // Render
    var bgColor = new DIR.Lib.RGBAColor32(0x1a, 0x1a, 0x2e, 0xff);
    if (!renderer.BeginFrame(bgColor))
    {
        sdlWindow.GetSizeInPixels(out var sw, out var sh);
        if (sw > 0 && sh > 0)
            renderer.Resize((uint)sw, (uint)sh);
        needsRedraw = true;
        continue;
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
        var (gameMode, computerSide) = menu.Result;
        menu = null;

        display = new VkGameDisplay(renderer);
        var timeProvider = TimeProvider.System;
        var enginePath = Path.Combine(AppContext.BaseDirectory,
            "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        var gameLoop = new GameLoop(
            timeProvider,
            () => display,
            () => player,
            (cs, tp) => new UciPlayer(enginePath, cs, tp)
        );

        gameTask = gameLoop.RunAsync(gameMode, computerSide, cts.Token);
    }

    renderer.EndFrame();

    // New glyphs were rasterized during DrawText but won't be visible
    // until next frame's Flush — schedule another render
    if (renderer.FontAtlasDirty)
        needsRedraw = true;
}

cts.Cancel();
display?.Dispose();
renderer.Dispose();
ctx.Dispose();

if (gameTask is not null)
{
    try { await gameTask; } catch (OperationCanceledException) { }
}

return 0;
