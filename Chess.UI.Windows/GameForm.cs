using Chess.Lib;
using Chess.Lib.UI;
using System.Diagnostics.CodeAnalysis;

namespace Chess.UI.Windows;

public partial class GameForm : Form
{
    private Game Game { get; set; } = new Game();
    private GraphicsGameUI? GameUI { get; set; }
    private FontCache FontCache { get; } = new FontCache();
    private FormWindowState LastWindowState { get; set; } = FormWindowState.Minimized;
    private Position? Selected { get; set; }

    public GameForm()
    {
        InitializeComponent();
    }

    [MemberNotNull(nameof(GameUI))]
    private void NewGameUI(Size size)
    {
        GameUI = new GraphicsGameUI(FontCache, Game, size.Width, size.Height);
    }

    private void MainForm_ResizeEnd(object sender, EventArgs e)
    {
        NewGameUI(Size);
        Invalidate();
    }

    private void MainForm_Paint(object sender, PaintEventArgs e)
    {
        if (GameUI is { } ui)
        {
            var clip = new RectLTRBInt((e.ClipRectangle.Right, e.ClipRectangle.Bottom), (e.ClipRectangle.X, e.ClipRectangle.Y));
            ui.RenderUI(e.Graphics, clip);
            ui.RenderBoard(e.Graphics, clip);

            if (Selected is { } selected)
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
        NewGameUI(Size);

        Invalidate();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        // When window state changes
        if (WindowState != LastWindowState)
        {
            LastWindowState = WindowState;

            if (WindowState == FormWindowState.Maximized)
            {
                NewGameUI(Size);
                Invalidate();
            }
            if (WindowState == FormWindowState.Normal)
            {
                NewGameUI(Size);
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
            if (Selected is { } prev && prev != selected)
            {
                if (Game.TryMove(prev, selected))
                {
                    Selected = default;

                    Invalidate();
                }
            }
            else if (Game.HasValidMoves(selected))
            {
                Selected = selected;

                Invalidate();
            }
        }
    }

    private void GameForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        FontCache.Dispose();
    }
}
