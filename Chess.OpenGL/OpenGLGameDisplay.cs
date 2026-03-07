using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

using File = Chess.Lib.File;

namespace Chess.OpenGL;



/// <summary>
/// An <see cref="IGameDisplay"/> implementation that renders the chess board
/// in an OpenGL window using Silk.NET for windowing and input.
/// </summary>
public sealed class OpenGLGameDisplay : IGameDisplay
{
    private static readonly RGBAColor32 BackgroundColor = new(0x1a, 0x1a, 0x2e, 0xff);
    private static readonly RGBAColor32 FontColor = new(0xff, 0xff, 0xff, 0xff);

    private readonly IWindow _window;
    private readonly GlRenderer _renderer;
    private GameUI? _gameUI;

    /// <summary>
    /// Creates the display with a pre-initialised window and renderer.
    /// The renderer and GL context are owned externally (e.g. by the application host).
    /// </summary>
    /// <param name="window">A running Silk.NET window.</param>
    /// <param name="renderer">An already-initialised <see cref="GlRenderer"/>.</param>
    public OpenGLGameDisplay(IWindow window, GlRenderer renderer)
    {
        _window = window;
        _renderer = renderer;
        _window.Render += OnRender;
        _window.Resize += OnResize;
    }

    /// <inheritdoc />
    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    /// <summary>
    /// Creates a Silk.NET window with default options suitable for the chess game.
    /// </summary>
    public static IWindow CreateWindow()
    {
        var options = WindowOptions.Default with
        {
            Title = "Chess",
            Size = new Vector2D<int>(800, 800),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3))
        };

        return Window.Create(options);
    }

    /// <inheritdoc />
    public void RenderInitial(Game game)
    {
    }

    /// <inheritdoc />
    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile = null)
    {
    }

    /// <inheritdoc />
    public void HandleResize(Game game)
    {
        // Handled by the Silk.NET Resize callback
    }

    /// <inheritdoc />
    public void ResetGame(Game game)
    {
        _gameUI = new GameUI(game, _renderer.Width, _renderer.Height,
            mainFontColor: FontColor,
            backgroundColor: BackgroundColor);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _window.Render -= OnRender;
        _window.Resize -= OnResize;
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_gameUI is null) return;

        _renderer.Resize((uint)size.X, (uint)size.Y);
        _gameUI = _gameUI.Resize((uint)size.X, (uint)size.Y);
    }

    private void OnRender(double deltaTime)
    {
        if (_gameUI is null) return;

        _renderer.Clear(BackgroundColor);

        var clip = new RectInt(((int)_renderer.Width, (int)_renderer.Height), PointInt.Origin);
        _gameUI.Render<GL, Renderer<GL>>(_renderer, clip);
    }
}
