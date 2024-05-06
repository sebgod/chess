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
            var clip = new RectInt((e.ClipRectangle.Right, e.ClipRectangle.Bottom), (e.ClipRectangle.X, e.ClipRectangle.Y));
            var graphics = e.Graphics;
            ui.RenderUI(graphics, clip);
            ui.RenderBoard(graphics, clip);
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

        if (GameUI is { } gameUI)
        {
            var (needsRefresh, clipRect) = gameUI.TryPerformAction(x, y);
            if (needsRefresh)
            {
                if (clipRect is { } rect)
                {
                    Invalidate(rect.ToRectInt());
                }
                else
                {
                    Invalidate();
                }
            }
        }
    }

    private void GameForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        FontCache.Dispose();
    }
}
