using Chess.Lib;
using System.Text;

namespace Chess.UI.Windows;

public partial class GameForm : Form
{
    private FormWindowState LastWindowState { get; set; } = FormWindowState.Minimized;

    public GameForm()
    {
        InitializeComponent();
    }

    private void MainForm_ResizeEnd(object sender, EventArgs e)
    {
        if (DesignMode)
        {
            return;
        }

        gamePanel1.NewGameUI();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
        if (DesignMode)
        {
            return;
        }

        // When window state changes
        if (WindowState != LastWindowState)
        {
            LastWindowState = WindowState;

            if (WindowState is FormWindowState.Maximized or FormWindowState.Normal)
            {
                gamePanel1.NewGameUI();
            }
        }
    }

    private void gamePanel1_GameUpdated(object sender, EventArgs e)
    {
        if (sender is GamePanel gamePanel)
        {
            UpdateGameStatus(gamePanel);
        }
    }

    private void GameForm_Shown(object sender, EventArgs e)
    {
        UpdateGameStatus(gamePanel1);
        UpdateMovePanel(tlpMoves);
    }

    private static void UpdateMovePanel(TableLayoutPanel panel)
    {
        panel.HorizontalScroll.Maximum = 0;
        panel.AutoScroll = false;
        panel.VerticalScroll.Visible = false;
        panel.AutoScroll = true;
    }

    private void UpdateGameStatus(GamePanel gamePanel)
    {
        var game    = gamePanel.Game;
        var plies   = game.Plies;

        var sbMove  = new StringBuilder();
        var sbWhite = new StringBuilder();
        var sbBlack = new StringBuilder();

        for (var i = 0; i <  plies.Count; i++)
        {
            var (idxStr, plyStr) = plies.ToPGN(i);
            if (i % 2 == 0)
            {
                sbMove.AppendLine(idxStr);
                sbWhite.AppendLine(plyStr);
            }
            else
            {
                sbBlack.AppendLine(plyStr);
            }
        }

        labelMoveNumber.Text = sbMove.ToString();
        labelPliesWhite.Text = sbWhite.ToString();
        labelPliesBlack.Text = sbBlack.ToString();
    }
}
