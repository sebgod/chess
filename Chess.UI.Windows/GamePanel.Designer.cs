namespace Chess.UI.Windows
{
    partial class GamePanel
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // GamePanel
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;
            Name = "GamePanel";
            Size = new Size(529, 515);
            Load += GamePanel_Load;
            Paint += GamePanel_Paint;
            MouseClick += GamePanel_MouseClick;
            ResumeLayout(false);
        }

        #endregion
    }
}
