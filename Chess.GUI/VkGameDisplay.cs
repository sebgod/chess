using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

public sealed class VkGameDisplay : IGameDisplay
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

    private readonly VkRenderer _renderer;
    private readonly string _labelFont;
    private GameUI? _gameUI;
    private volatile bool _hasPendingUpdate;
    private Game? _game;

    public VkGameDisplay(VkRenderer renderer)
    {
        _renderer = renderer;
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

        var (boardW, boardH) = ComputeBoardArea();
        var totalW = (int)_renderer.Width;
        var totalH = (int)_renderer.Height;

        var boardClip = new RectInt((boardW, boardH), PointInt.Origin);
        _gameUI.Render<VulkanContext, Renderer<VulkanContext>>(_renderer, boardClip);

        var historyRect = new RectInt((totalW, boardH), (boardW, 0));
        RenderHistoryPanel(historyRect);

        var statusRect = new RectInt((totalW, totalH), (0, boardH));
        RenderStatusBar(statusRect);
    }

    public void Dispose() { }

    private float ChromeFontSize => MathF.Max(13f, (int)_renderer.Height / 40f);
    private int HistoryPanelWidth => (int)(ChromeFontSize * HistoryPanelWidthFactor);
    private int StatusBarHeight => (int)(ChromeFontSize * StatusBarHeightFactor);

    private (int BoardW, int BoardH) ComputeBoardArea()
    {
        var totalW = (int)_renderer.Width;
        var totalH = (int)_renderer.Height;
        return (totalW - HistoryPanelWidth, totalH - StatusBarHeight);
    }

    private int ComputeHistoryVisibleRows(int boardH)
    {
        var fontSize = ChromeFontSize;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;
        return Math.Max(1, (int)((boardH - headerH) / rowH));
    }

    private void RenderHistoryPanel(in RectInt rect)
    {
        var fontSize = ChromeFontSize;
        var headerFontSize = fontSize * 1.1f;
        var headerH = (int)(fontSize * 2f);
        var rowH = fontSize * 1.5f;

        _renderer.FillRectangle(rect, HistoryBg);

        var headerRect = new RectInt(
            (rect.LowerRight.X, rect.UpperLeft.Y + headerH),
            (rect.UpperLeft.X + 8, rect.UpperLeft.Y));
        _renderer.DrawText("Move History", _labelFont, headerFontSize,
            HistoryHeaderColor, headerRect, TextAlign.Near, TextAlign.Center);

        var sepRect = new RectInt(
            (rect.LowerRight.X - 4, rect.UpperLeft.Y + headerH + 1),
            (rect.UpperLeft.X + 4, rect.UpperLeft.Y + headerH));
        _renderer.FillRectangle(sepRect, HistorySepColor);

        if (_game is null || _gameUI is null) return;

        var plies = _game.Plies;
        var plyCount = plies.Count;
        if (plyCount == 0) return;

        var moveCount = (plyCount + 1) / 2;
        var visibleRows = _gameUI.HistoryViewportRows;
        var startMove = _gameUI.HistoryScrollStart ?? Math.Max(0, moveCount - visibleRows);
        var highlightPly = _gameUI.Mode == GameUIMode.Playback ? _gameUI.PlaybackPlyIndex : (int?)null;

        var contentY = rect.UpperLeft.Y + headerH + 4;
        var idxColW = (int)(fontSize * 3.5f);
        var plyColW = (rect.LowerRight.X - rect.UpperLeft.X - idxColW) / 2;

        for (var i = 0; i < visibleRows && startMove + i < moveCount; i++)
        {
            var moveIdx = startMove + i;
            var whitePlyIdx = moveIdx * 2;
            var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(whitePlyIdx);
            var blackPlyStr = whitePlyIdx + 1 < plyCount
                ? plies.GetRecordAndPGNIdx(whitePlyIdx + 1).Ply.ToString()
                : "";

            var rowY = (int)(contentY + i * rowH);
            var rowH2 = (int)rowH;

            var idxRect = new RectInt(
                (rect.UpperLeft.X + idxColW, rowY + rowH2),
                (rect.UpperLeft.X + 4, rowY));
            _renderer.DrawText(idxStr.AsSpan().Trim(), _labelFont, fontSize,
                HistoryIndexColor, idxRect, TextAlign.Far, TextAlign.Center);

            var whiteX = rect.UpperLeft.X + idxColW + 4;
            var whiteRect = new RectInt(
                (whiteX + plyColW, rowY + rowH2),
                (whiteX, rowY));

            var isHighlightWhite = highlightPly == whitePlyIdx;
            if (isHighlightWhite)
                _renderer.FillRectangle(whiteRect, PlaybackHighlightBg);
            _renderer.DrawText(whitePly.ToString(), _labelFont, fontSize,
                isHighlightWhite ? PlaybackHighlightText : FontColor,
                whiteRect, TextAlign.Near, TextAlign.Center);

            if (blackPlyStr.Length > 0)
            {
                var blackX = whiteX + plyColW + 2;
                var blackRect = new RectInt(
                    (rect.LowerRight.X - 4, rowY + rowH2),
                    (blackX, rowY));

                var isHighlightBlack = highlightPly == whitePlyIdx + 1;
                if (isHighlightBlack)
                    _renderer.FillRectangle(blackRect, PlaybackHighlightBg);
                _renderer.DrawText(blackPlyStr, _labelFont, fontSize,
                    isHighlightBlack ? PlaybackHighlightText : FontColor,
                    blackRect, TextAlign.Near, TextAlign.Center);
            }
        }
    }

    private void RenderStatusBar(in RectInt rect)
    {
        var fontSize = ChromeFontSize;
        _renderer.FillRectangle(rect, StatusBarBg);

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

        var textRect = new RectInt(
            (rect.LowerRight.X - 8, rect.LowerRight.Y),
            (rect.UpperLeft.X + 8, rect.UpperLeft.Y));
        _renderer.DrawText(status, _labelFont, fontSize, FontColor,
            textRect, TextAlign.Near, TextAlign.Center);
    }

    private int? ResolveHistoryClick(int px, int py)
    {
        var (boardW, boardH) = ComputeBoardArea();

        if (px < boardW || py < 0 || py >= boardH)
            return null;

        var fontSize = ChromeFontSize;
        var headerH = (int)(fontSize * 2f);

        if (py < headerH)
            return null;

        var rowH = (int)(fontSize * 1.5f);
        var row = (py - headerH - 4) / rowH;

        if (_game is null || _gameUI is null) return null;

        var plyCount = _game.PlyCount;
        var moveCount = (plyCount + 1) / 2;
        var visibleRows = _gameUI.HistoryViewportRows;
        var startMove = _gameUI.HistoryScrollStart ?? Math.Max(0, moveCount - visibleRows);
        var moveIdx = startMove + row;
        var whitePlyIdx = moveIdx * 2;

        if (whitePlyIdx >= plyCount)
            return null;

        var idxColW = (int)(fontSize * 3.5f);
        var plyColW = (HistoryPanelWidth - idxColW) / 2;
        var midX = boardW + idxColW + plyColW;
        if (px >= midX && whitePlyIdx + 1 < plyCount)
            return whitePlyIdx + 1;

        return whitePlyIdx;
    }
}
