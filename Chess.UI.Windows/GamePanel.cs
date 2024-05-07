using Chess.Lib;
using Chess.Lib.UI;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Chess.UI.Windows
{
    public partial class GamePanel : UserControl
    {
        private Game _game = new Game();

        [Browsable(false)]
        public Game Game
        {
            get => _game;
            set
            {
                _game = value;
                NewGameUI();
            }
        }

        [MemberNotNull(nameof(GameUI))]
        private void NewGameUI()
        {
            var size = ClientSize;
            GameUI = new GraphicsGameUI(FontCache, _game, size.Width, size.Height);
            Invalidate();
        }

        [Browsable(false)]
        public GraphicsGameUI? GameUI { get; set; }
        
        [Browsable(false)]
        private FontCache FontCache { get; } = new FontCache();

        [Browsable(true)]
        [Category("Action")]
        [Description("Invoked when the game state changes")]
        public event EventHandler? GameUpdated;

        public GamePanel()
        {
            InitializeComponent();
        }

        private void GamePanel_MouseClick(object sender, MouseEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            if (GameUI is { } gameUI)
            {
                var (needsRefresh, isUpdate, clipRect) = gameUI.TryPerformAction(x, y);

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

                if (isUpdate)
                {
                    GameUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void ResizeEnd()
        {
            NewGameUI();;
        }

        private void GamePanel_Paint(object sender, PaintEventArgs e)
        {
            if (GameUI is { } ui)
            {
                var clip = new RectInt((e.ClipRectangle.Right, e.ClipRectangle.Bottom), (e.ClipRectangle.X, e.ClipRectangle.Y));
                var graphics = e.Graphics;
                ui.RenderUI(graphics, clip);
                ui.RenderBoard(graphics, clip);
            }
        }

        private void GamePanel_Load(object sender, EventArgs e)
        {
            if (!IsAncestorSiteInDesignMode)
            {
                NewGameUI();
            }
        }


        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FontCache.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
