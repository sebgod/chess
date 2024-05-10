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
            tlpMain = new TableLayoutPanel();
            tlpMoves = new TableLayoutPanel();
            labelPliesWhite = new Label();
            labelPliesBlack = new Label();
            labelMoveNumber = new Label();
            tlpMain.SuspendLayout();
            tlpMoves.SuspendLayout();
            SuspendLayout();
            // 
            // gamePanel1
            // 
            gamePanel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gamePanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            gamePanel1.Game = game1;
            gamePanel1.Location = new Point(3, 3);
            gamePanel1.Name = "gamePanel1";
            gamePanel1.Size = new Size(736, 726);
            gamePanel1.TabIndex = 0;
            gamePanel1.GameUpdated += gamePanel1_GameUpdated;
            // 
            // tlpMain
            // 
            tlpMain.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tlpMain.ColumnCount = 2;
            tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70.1810455F));
            tlpMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29.8189564F));
            tlpMain.Controls.Add(tlpMoves, 1, 0);
            tlpMain.Controls.Add(gamePanel1, 0, 0);
            tlpMain.Dock = DockStyle.Fill;
            tlpMain.Location = new Point(0, 0);
            tlpMain.Name = "tlpMain";
            tlpMain.RowCount = 1;
            tlpMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tlpMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tlpMain.Size = new Size(1058, 732);
            tlpMain.TabIndex = 1;
            // 
            // tlpMoves
            // 
            tlpMoves.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tlpMoves.ColumnCount = 3;
            tlpMoves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tlpMoves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            tlpMoves.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            tlpMoves.Controls.Add(labelPliesWhite, 1, 0);
            tlpMoves.Controls.Add(labelPliesBlack, 2, 0);
            tlpMoves.Controls.Add(labelMoveNumber, 0, 0);
            tlpMoves.Dock = DockStyle.Fill;
            tlpMoves.Location = new Point(745, 3);
            tlpMoves.Name = "tlpMoves";
            tlpMoves.RowCount = 1;
            tlpMoves.RowStyles.Add(new RowStyle());
            tlpMoves.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tlpMoves.Size = new Size(310, 726);
            tlpMoves.TabIndex = 6;
            // 
            // labelPliesWhite
            // 
            labelPliesWhite.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelPliesWhite.AutoSize = true;
            labelPliesWhite.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelPliesWhite.Location = new Point(65, 0);
            labelPliesWhite.Name = "labelPliesWhite";
            labelPliesWhite.Size = new Size(118, 726);
            labelPliesWhite.TabIndex = 3;
            labelPliesWhite.Text = "White";
            // 
            // labelPliesBlack
            // 
            labelPliesBlack.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelPliesBlack.AutoSize = true;
            labelPliesBlack.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelPliesBlack.Location = new Point(189, 0);
            labelPliesBlack.Name = "labelPliesBlack";
            labelPliesBlack.Size = new Size(118, 726);
            labelPliesBlack.TabIndex = 2;
            labelPliesBlack.Text = "Black";
            // 
            // labelMoveNumber
            // 
            labelMoveNumber.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelMoveNumber.AutoSize = true;
            labelMoveNumber.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelMoveNumber.Location = new Point(3, 0);
            labelMoveNumber.Name = "labelMoveNumber";
            labelMoveNumber.Size = new Size(56, 726);
            labelMoveNumber.TabIndex = 5;
            labelMoveNumber.Text = "No";
            labelMoveNumber.TextAlign = ContentAlignment.TopRight;
            // 
            // GameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ControlLight;
            ClientSize = new Size(1058, 732);
            Controls.Add(tlpMain);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "GameForm";
            SizeGripStyle = SizeGripStyle.Hide;
            Text = "Chess";
            Shown += GameForm_Shown;
            ResizeEnd += MainForm_ResizeEnd;
            Resize += MainForm_Resize;
            tlpMain.ResumeLayout(false);
            tlpMoves.ResumeLayout(false);
            tlpMoves.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GamePanel gamePanel1;
        private TableLayoutPanel tlpMain;
        private TableLayoutPanel tlpMoves;
        private Label labelPliesWhite;
        private Label labelPliesBlack;
        private Label labelMoveNumber;
    }
}
