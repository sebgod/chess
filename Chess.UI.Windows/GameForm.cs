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
}
