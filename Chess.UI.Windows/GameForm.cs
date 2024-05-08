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
        gamePanel1.NewGameUI();
    }

    private void MainForm_Resize(object sender, EventArgs e)
    {
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
    }

    private void UpdateGameStatus(GamePanel gamePanel)
    {
        var game = gamePanel.Game;
        var currentSide = game.CurrentSide;
        var plies = game.Plies;

        var sbMove  = new StringBuilder();
        var sbWhite = new StringBuilder();
        var sbBlack = new StringBuilder();
        for (var i = 0; i <  plies.Count; i++)
        {
            var ply = plies[i].ToString();
            if (i % 2 == 0)
            {
                sbMove.Append(i / 2 + 1).AppendLine(".");
                sbWhite.AppendLine(ply);
            }
            else
            {
                sbBlack.AppendLine(ply);
            }
        }

        labelMoveNumber.Text = sbMove.ToString();
        labelPliesWhite.Text = sbWhite.ToString();
        labelPliesBlack.Text = sbBlack.ToString();
        labelGameState.Text  = game.GameStatus.ToMessage(currentSide);
    }
}
