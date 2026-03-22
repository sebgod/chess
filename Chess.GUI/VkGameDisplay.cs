using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

public sealed class VkGameDisplay : PixelWidgetBase<VulkanContext>, IGameDisplay
{
    private static readonly RGBAColor32 BackgroundColor = new(0x1a, 0x1a, 0x2e, 0xff);
    private static readonly RGBAColor32 FontColor = new(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor32 StatusBarBg = new(0x24, 0x24, 0x3a, 0xff);
    private static readonly RGBAColor32 HistoryBg = new(0x20, 0x20, 0x34, 0xff);
    private static readonly RGBAColor32 HistoryHeaderColor = new(0xff, 0xce, 0x9e, 0xff);
    private static readonly RGBAColor32 HistorySepColor = new(0x40, 0x40, 0x60, 0xff);
    private static readonly RGBAColor32 HistoryIndexColor = new(0x80, 0x80, 0x98, 0xff);
    private static readonly RGBAColor32 PlaybackHighlightBg = new(0x30, 0x50, 0x90, 0xff);
    private static readonly RGBAColor32 PlaybackHighlightText = new(0xff, 0xd7, 0x00, 0xff);

    private const float HistoryPanelWidthFactor = 18f;
    private const float StatusBarHeightFactor = 2f;

    private readonly string _labelFont;
    private GameUI? _gameUI;
    private volatile bool _hasPendingUpdate;
    private Game? _game;

    public VkGameDisplay(VkRenderer renderer) : base(renderer)
    {
        _labelFont = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    }

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    public bool HasPendingUpdate
    {
        get
        {
            var val = _hasPendingUpdate;
            _hasPendingUpdate = false;
            return val;
        }
    }

    public void RenderInitial(Game game) { _game = game; _hasPendingUpdate = true; }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects)
    {
        _game = game;
        _hasPendingUpdate = true;
    }

    public void HandleResize(Game game) { }

    public void ResetGame(Game game)
    {
        _game = game;
        var (boardW, boardH) = ComputeBoardArea();

        _gameUI = new GameUI(game, (uint)boardW, (uint)boardH,
            mainFontColor: FontColor,
            backgroundColor: BackgroundColor,
            resolveHistoryClick: ResolveHistoryClick);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(boardH);
        _hasPendingUpdate = true;
    }

    public void OnResize(int width, int height)
    {
        if (_gameUI is null) return;

        var (boardW, boardH) = ComputeBoardArea();
        _gameUI = _gameUI.Resize((uint)boardW, (uint)boardH);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(boardH);
    }

    public void Render()
    {
        if (_gameUI is null) return;

        BeginFrame();

        // Use ComputeBoardArea() for the clip rect — must match the dimensions
        // passed to GameUI constructor/Resize to keep overlay sizing consistent.
        var (boardW, boardH) = ComputeBoardArea();

        var totalW = (float)Renderer.Width;
        var totalH = (float)Renderer.Height;
        var layout = new PixelLayout(new RectF32(0, 0, totalW, totalH));

        var statusRect = layout.Dock(PixelDockStyle.Bottom, totalH - boardH);
        var historyRect = layout.Dock(PixelDockStyle.Right, totalW - boardW);
        var boardRect = layout.Fill();

        var boardClip = new RectInt((boardW, boardH), PointInt.Origin);
        _gameUI.Render<VulkanContext, Renderer<VulkanContext>>(Renderer, boardClip);

        RenderHistoryPanel(historyRect);
        RenderStatusBar(statusRect);
    }

    public void Dispose() { }

    private float ChromeFontSize => MathF.Max(13f, (int)Renderer.Height / 40f);
    private float HistoryPanelWidth => ChromeFontSize * HistoryPanelWidthFactor;
    private float StatusBarHeight => ChromeFontSize * StatusBarHeightFactor;

    private (int BoardW, int BoardH) ComputeBoardArea()
    {
        var totalW = (int)Renderer.Width;
        var totalH = (int)Renderer.Height;
        return (totalW - (int)HistoryPanelWidth, totalH - (int)StatusBarHeight);
    }

    private int ComputeHistoryVisibleRows(int boardH)
    {
        var fontSize = ChromeFontSize;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;
        return Math.Max(1, (int)((boardH - headerH) / rowH));
    }

    private void RenderHistoryPanel(RectF32 rect)
    {
        var fontSize = ChromeFontSize;
        var headerFontSize = fontSize * 1.1f;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;

        FillRect(rect.X, rect.Y, rect.Width, rect.Height, HistoryBg);

        DrawText("Move History", _labelFont,
            rect.X + 8, rect.Y, rect.Width - 8, headerH,
            headerFontSize, HistoryHeaderColor, TextAlign.Near, TextAlign.Center);

        FillRect(rect.X + 4, rect.Y + headerH, rect.Width - 8, 1, HistorySepColor);

        if (_game is null || _gameUI is null) return;

        var plies = _game.Plies;
        var plyCount = plies.Count;
        if (plyCount == 0) return;

        var moveCount = (plyCount + 1) / 2;
        var visibleRows = _gameUI.HistoryViewportRows;
        var startMove = _gameUI.HistoryScrollStart ?? Math.Max(0, moveCount - visibleRows);
        var highlightPly = _gameUI.Mode == GameUIMode.Playback ? _gameUI.PlaybackPlyIndex : (int?)null;

        var contentY = rect.Y + headerH + 4;
        var idxColW = fontSize * 3.5f;
        var plyColW = (rect.Width - idxColW) / 2;

        for (var i = 0; i < visibleRows && startMove + i < moveCount; i++)
        {
            var moveIdx = startMove + i;
            var whitePlyIdx = moveIdx * 2;
            var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(whitePlyIdx);
            var blackPlyStr = whitePlyIdx + 1 < plyCount
                ? plies.GetRecordAndPGNIdx(whitePlyIdx + 1).Ply.ToString()
                : "";

            var rowY = contentY + i * rowH;

            DrawText(idxStr.AsSpan().Trim(), _labelFont,
                rect.X + 4, rowY, idxColW - 4, rowH,
                fontSize, HistoryIndexColor, TextAlign.Far, TextAlign.Center);

            var whiteX = rect.X + idxColW + 4;

            var isHighlightWhite = highlightPly == whitePlyIdx;
            if (isHighlightWhite)
                FillRect(whiteX, rowY, plyColW, rowH, PlaybackHighlightBg);
            DrawText(whitePly.ToString(), _labelFont,
                whiteX, rowY, plyColW, rowH,
                fontSize, isHighlightWhite ? PlaybackHighlightText : FontColor,
                TextAlign.Near, TextAlign.Center);

            // Register clickable region for white ply
            var capturedWhitePly = whitePlyIdx;
            RegisterClickable(whiteX, rowY, plyColW, rowH,
                new HitResult.ListItemHit("History", capturedWhitePly));

            if (blackPlyStr.Length > 0)
            {
                var blackX = whiteX + plyColW + 2;
                var blackW = rect.Width - idxColW - plyColW - 6;

                var isHighlightBlack = highlightPly == whitePlyIdx + 1;
                if (isHighlightBlack)
                    FillRect(blackX, rowY, blackW, rowH, PlaybackHighlightBg);
                DrawText(blackPlyStr, _labelFont,
                    blackX, rowY, blackW, rowH,
                    fontSize, isHighlightBlack ? PlaybackHighlightText : FontColor,
                    TextAlign.Near, TextAlign.Center);

                // Register clickable region for black ply
                var capturedBlackPly = whitePlyIdx + 1;
                RegisterClickable(blackX, rowY, blackW, rowH,
                    new HitResult.ListItemHit("History", capturedBlackPly));
            }
        }
    }

    private void RenderStatusBar(RectF32 rect)
    {
        var fontSize = ChromeFontSize;
        FillRect(rect.X, rect.Y, rect.Width, rect.Height, StatusBarBg);

        string status;
        if (_game is null)
        {
            status = "";
        }
        else if (_gameUI?.Mode == GameUIMode.Playback)
        {
            var plyIdx = _gameUI.PlaybackPlyIndex;
            var plyCount = _game.PlyCount;
            status = $"Playback: ply {plyIdx + 2}/{plyCount + 1}  [Ctrl+Arrows, Esc exit]";
        }
        else if (_gameUI?.IsSetupMode == true)
        {
            var side = _gameUI.PlacementSide;
            var fileInfo = _gameUI.PendingFile is { } f ? $" [{f.ToLabel()}]" : "";
            status = $"Setup: placing {side} pieces [Tab toggle, s start]{fileInfo}";
        }
        else
        {
            var fileInfo = _gameUI?.PendingFile is { } f ? $" [{f.ToLabel()}]" : "";
            status = $"{_game.GameStatus.ToMessage(_game.CurrentSide)}{fileInfo}";
        }

        DrawText(status, _labelFont,
            rect.X + 8, rect.Y, rect.Width - 16, rect.Height,
            fontSize, FontColor, TextAlign.Near, TextAlign.Center);
    }

    private int? ResolveHistoryClick(int px, int py)
    {
        // Use the hit-test system from PixelWidgetBase
        var hit = HitTest(px, py);
        if (hit is HitResult.ListItemHit { ListId: "History" } historyHit)
            return historyHit.Index;

        return null;
    }
}
