using Chess.Lib.UI;
using Chess.OpenGL;
using Chess.UCI;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

// Explicitly register GLFW platforms to avoid reflection-based discovery (AOT-incompatible).
GlfwWindowing.RegisterPlatform();
GlfwInput.RegisterPlatform();

var cts = new CancellationTokenSource();
var window = OpenGLGameDisplay.CreateWindow();

GL? gl = null;
GlRenderer? renderer = null;
OpenGLStartupMenu? menu = null;
HumanPlayer? player = null;
OpenGLGameDisplay? display = null;
Task? gameTask = null;

window.Load += () =>
{
    gl = window.CreateOpenGL();
    gl.Enable(EnableCap.Blend);
    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

    renderer = new GlRenderer(gl, (uint)window.Size.X, (uint)window.Size.Y);
    menu = new OpenGLStartupMenu();
    player = new HumanPlayer();

    var input = window.CreateInput();
    player.Attach(input);

    foreach (var kb in input.Keyboards)
    {
        kb.KeyDown += (_, key, _) =>
        {
            if (key is Key.F11)
            {
                window.WindowState = window.WindowState == WindowState.Fullscreen
                    ? WindowState.Normal
                    : WindowState.Fullscreen;
                return;
            }

            if (menu is { IsComplete: false })
                menu.HandleKey(key);
        };
    }

    foreach (var mouse in input.Mice)
    {
        mouse.MouseDown += (m, button) =>
        {
            if (button == MouseButton.Left && menu is { IsComplete: false } && renderer is not null)
                menu.HandleClick((int)m.Position.X, (int)m.Position.Y, renderer.Width, renderer.Height);
        };
    }
};

window.Resize += (size) =>
{
    renderer?.Resize((uint)size.X, (uint)size.Y);
};

window.Render += (_) =>
{
    if (renderer is null) return;

    // Game phase — display renders itself via its own Render subscription
    if (display is not null) return;

    // Menu phase
    if (menu is { IsComplete: false })
    {
        menu.Render(renderer);
        return;
    }

    // Transition: menu just completed, start the game
    if (menu is { IsComplete: true } && gameTask is null)
    {
        var (gameMode, computerSide) = menu.Result;
        menu = null;

        display = new OpenGLGameDisplay(window, renderer);
        var timeProvider = TimeProvider.System;
        var enginePath = Path.Combine(AppContext.BaseDirectory, "chess-engine" + (OperatingSystem.IsWindows() ? ".exe" : ""));

        var gameLoop = new GameLoop(
            timeProvider,
            () => display,
            () => player!,
            (cs, tp) => new UciPlayer(enginePath, cs, tp)
        );

        gameTask = gameLoop.RunAsync(gameMode, computerSide, cts.Token);
    }
};

window.Closing += () =>
{
    cts.Cancel();
    display?.Dispose();
    display = null;
    renderer?.Dispose();
    renderer = null;
};

window.Initialize();

while (!window.IsClosing)
{
    window.DoEvents();
    window.DoUpdate();
    window.DoRender();
}

window.DoEvents();
window.Reset();

if (gameTask is not null)
{
    try { await gameTask; } catch (OperationCanceledException) { }
}

return 0;
