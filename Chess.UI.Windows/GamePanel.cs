using Chess.Lib;
using Chess.Lib.UI;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Chess.UI.Windows
{
    public partial class GamePanel : UserControl
    {
        private GraphisRenderer? _renderer;
        private Game _game = new Game();
        private Point? _mouseLeftDown;

        [Browsable(false)]
        [Bindable(BindableSupport.No)]
        public Game Game
        {
            get => _game;
            set
            {
                _game = value;
                if (!IsAncestorSiteInDesignMode)
                {
                    NewGameUI();
                }
            }
        }

        [MemberNotNull(nameof(GameUI))]
        public void NewGameUI()
        {
            var size = ClientSize;
            var squareSize = GameUI.CalculateSquareSize(size.Width, size.Height);
            Position? selected;
            Position? pendingPromotion;
            if (GameUI is { } ui)
            {
                selected = ui.Selected;
                pendingPromotion = ui.PendingPromotion;

                // clear cache due to font size change
                if (ui.SquareSize != squareSize)
                {
                    FontCache.ClearCachedFonts();
                }
            }
            else
            {
                selected = default;
                pendingPromotion = default;
            }

            GameUI = new GameUI(_game, size.Width, size.Height, selected, pendingPromotion);
            Invalidate();
        }

        [Browsable(false)]
        [Bindable(BindableSupport.No)]
        public GameUI? GameUI { get; private set; }

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
                var (uiResponse, clipRects) = gameUI.TryPerformAction(x, y);

                if (uiResponse.HasFlag(UIResponse.NeedsRefresh))
                {
                    if (clipRects.Length > 0)
                    {
                        for (var i = 0; i < clipRects.Length; i++)
                        {
                            Invalidate(clipRects[i].ToRectInt());
                        }
                    }
                    else
                    {
                        Invalidate();
                    }

                }

                if (uiResponse.HasFlag(UIResponse.IsUpdate))
                {
                    GameUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void GamePanel_Paint(object sender, PaintEventArgs e)
        {
            if (GameUI is { } ui)
            {
                var renderer = _renderer ??= new GraphisRenderer(FontCache);
                var clip = new RectInt((e.ClipRectangle.Right, e.ClipRectangle.Bottom), (e.ClipRectangle.X, e.ClipRectangle.Y));
                var graphics = e.Graphics;
                ui.Render(renderer, graphics, clip);
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

        private void GamePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button is MouseButtons.Left)
            {
                _mouseLeftDown = e.Location;
            }
        }

        private void GamePanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button is MouseButtons.Left)
            {
                _mouseLeftDown = null;
            }
        }

        private void GamePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_mouseLeftDown is { } prev && GameUI is { } ui)
            {
                var deltaY = e.Location.Y - prev.Y;
                ui.ScrollPliesDelta = deltaY;
            }
        }
    }
}
