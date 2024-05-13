namespace Chess.UI.Windows
{
    partial class GameForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            Lib.Game game1 = new Lib.Game();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GameForm));
            gamePanel1 = new GamePanel();
            SuspendLayout();
            // 
            // gamePanel1
            // 
            gamePanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            gamePanel1.Dock = DockStyle.Fill;
            gamePanel1.Game = game1;
            gamePanel1.Location = new Point(0, 0);
            gamePanel1.Name = "gamePanel1";
            gamePanel1.Size = new Size(1058, 732);
            gamePanel1.TabIndex = 0;
            // 
            // GameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ControlLight;
            ClientSize = new Size(1058, 732);
            Controls.Add(gamePanel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "GameForm";
            SizeGripStyle = SizeGripStyle.Hide;
            Text = "Chess";
            ResizeEnd += MainForm_ResizeEnd;
            Resize += MainForm_Resize;
            ResumeLayout(false);
        }

        #endregion

        private GamePanel gamePanel1;
    }
}
