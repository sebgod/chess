using Chess.Lib;
using ImageMagick;
using System.Drawing.Imaging;

namespace Chess.UI.Windows;

public partial class GameForm : Form
{
    private Game Game { get; set; } = new Game();
    private GameUI? GameUI { get; set; }

    private Bitmap? _buffer;
    private CachedBitmap? _cachedBitmap;
    private FormWindowState _lastWindowState = FormWindowState.Minimized;

    private Position? _selected;

    public GameForm()
    {
        InitializeComponent();
    }

    private static GameUI NewGameUI(Game game, Size size)
    {
        var ui = new GameUI(game, size.Width, size.Height);
        ui.RenderUI();
        ui.RenderBoard();

        return ui;
    }

    private void RecreateBuffer()
    {
        if (GameUI is { } ui)
        {
            _buffer?.Dispose();
            _buffer = ui.Image.ToBitmap();

            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
        }
    }

    private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        GameUI?.Dispose();
        GameUI = null;

        _buffer?.Dispose();
        _buffer = null;

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }

    private void MainForm_ResizeEnd(object sender, EventArgs e)
    {
        GameUI = NewGameUI(Game, Size);
        RecreateBuffer();
        Invalidate();
    }

    private void MainForm_Paint(object sender, PaintEventArgs e)
    {
        if (_buffer is { } buffer && GameUI is { } ui)
        {
            var cachedBitmap = _cachedBitmap ??= new CachedBitmap(buffer, e.Graphics);
            e.Graphics.DrawCachedBitmap(cachedBitmap, 0, 0);

            if (_selected is { } selected)
            {
                var (x, y) = ui.SquarePos(selected);
                var sq = ui.SquareSize;
                using var pen = new Pen(Color.IndianRed, 3.5f);
                e.Graphics.DrawRectangle(pen, new Rectangle(x, y, sq, sq));
            }
        }
    }

    private void MainForm_Shown(object sender, EventArgs e)
    {
        GameUI = NewGameUI(Game, Size);
        RecreateBuffer();

        BackColor = GameUI.BackgroundColor.ToColor();
        Invalidate();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        // When window state changes
        if (WindowState != _lastWindowState)
        {
            _lastWindowState = WindowState;

            if (WindowState == FormWindowState.Maximized)
            {
                GameUI = NewGameUI(Game, Size);
                RecreateBuffer();
                Invalidate();
            }
            if (WindowState == FormWindowState.Normal)
            {
                GameUI = NewGameUI(Game, Size);
                RecreateBuffer();
                Invalidate();
            }
        }
    }

    private void MainForm_MouseClick(object sender, MouseEventArgs e)
    {
        var x = e.X;
        var y = e.Y;

        if (GameUI is { } gameUI && gameUI.FindSelected(x, y) is { } selected)
        {
            if (_selected is { } prev && prev != selected)
            {
                if (Game.TryMove(prev, selected))
                {
                    _selected = default;
                    GameUI.RenderBoard();
                    RecreateBuffer();
                    Invalidate();
                }
            }
            else if (Game.HasValidMoves(selected))
            {
                _selected = selected;

                Invalidate();
            }
        }
    }
}
