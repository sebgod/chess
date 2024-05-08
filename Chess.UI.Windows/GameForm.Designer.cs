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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GameForm));
            gamePanel1 = new GamePanel();
            tableLayoutPanel1 = new TableLayoutPanel();
            labelMoveNumber = new Label();
            label1 = new Label();
            flowLayoutPanel1 = new FlowLayoutPanel();
            labelGameState = new Label();
            labelPliesWhite = new Label();
            labelPliesBlack = new Label();
            tableLayoutPanel1.SuspendLayout();
            flowLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // gamePanel1
            // 
            gamePanel1.Dock = DockStyle.Fill;
            gamePanel1.Location = new Point(3, 35);
            gamePanel1.Name = "gamePanel1";
            tableLayoutPanel1.SetRowSpan(gamePanel1, 2);
            gamePanel1.Size = new Size(705, 694);
            gamePanel1.TabIndex = 0;
            gamePanel1.GameUpdated += gamePanel1_GameUpdated;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 4;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 5F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10F));
            tableLayoutPanel1.Controls.Add(labelMoveNumber, 0, 2);
            tableLayoutPanel1.Controls.Add(label1, 2, 0);
            tableLayoutPanel1.Controls.Add(gamePanel1, 0, 1);
            tableLayoutPanel1.Controls.Add(flowLayoutPanel1, 0, 0);
            tableLayoutPanel1.Controls.Add(labelPliesWhite, 2, 2);
            tableLayoutPanel1.Controls.Add(labelPliesBlack, 3, 2);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle());
            tableLayoutPanel1.Size = new Size(949, 732);
            tableLayoutPanel1.TabIndex = 1;
            // 
            // labelMoveNumber
            // 
            labelMoveNumber.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelMoveNumber.AutoSize = true;
            labelMoveNumber.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelMoveNumber.Location = new Point(714, 52);
            labelMoveNumber.Name = "labelMoveNumber";
            labelMoveNumber.Size = new Size(41, 680);
            labelMoveNumber.TabIndex = 5;
            labelMoveNumber.Text = "No";
            labelMoveNumber.TextAlign = ContentAlignment.TopRight;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label1.Location = new Point(761, 0);
            label1.Name = "label1";
            label1.Size = new Size(67, 32);
            label1.TabIndex = 4;
            label1.Text = "Moves";
            label1.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // flowLayoutPanel1
            // 
            flowLayoutPanel1.Controls.Add(labelGameState);
            flowLayoutPanel1.Dock = DockStyle.Fill;
            flowLayoutPanel1.Location = new Point(3, 3);
            flowLayoutPanel1.Name = "flowLayoutPanel1";
            flowLayoutPanel1.Size = new Size(705, 26);
            flowLayoutPanel1.TabIndex = 1;
            // 
            // labelGameState
            // 
            labelGameState.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            labelGameState.AutoSize = true;
            labelGameState.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelGameState.Location = new Point(3, 0);
            labelGameState.Name = "labelGameState";
            labelGameState.Size = new Size(115, 25);
            labelGameState.TabIndex = 0;
            labelGameState.Text = "Current side";
            labelGameState.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // labelPliesWhite
            // 
            labelPliesWhite.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelPliesWhite.AutoSize = true;
            labelPliesWhite.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelPliesWhite.Location = new Point(761, 52);
            labelPliesWhite.Name = "labelPliesWhite";
            labelPliesWhite.Size = new Size(88, 680);
            labelPliesWhite.TabIndex = 3;
            labelPliesWhite.Text = "White";
            // 
            // labelPliesBlack
            // 
            labelPliesBlack.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelPliesBlack.AutoSize = true;
            labelPliesBlack.Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            labelPliesBlack.Location = new Point(855, 52);
            labelPliesBlack.Name = "labelPliesBlack";
            labelPliesBlack.Size = new Size(91, 680);
            labelPliesBlack.TabIndex = 2;
            labelPliesBlack.Text = "Black";
            // 
            // GameForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.ControlLight;
            ClientSize = new Size(949, 732);
            Controls.Add(tableLayoutPanel1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "GameForm";
            SizeGripStyle = SizeGripStyle.Hide;
            Text = "Chess";
            Shown += GameForm_Shown;
            ResizeEnd += MainForm_ResizeEnd;
            Resize += MainForm_Resize;
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            flowLayoutPanel1.ResumeLayout(false);
            flowLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GamePanel gamePanel1;
        private TableLayoutPanel tableLayoutPanel1;
        private FlowLayoutPanel flowLayoutPanel1;
        private Label labelGameState;
        private Label labelPliesBlack;
        private Label labelPliesWhite;
        private Label label1;
        private Label labelMoveNumber;
    }
}
