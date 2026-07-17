using System.Collections.Immutable;
using DIR.Lib;
using Layout = DIR.Lib.Layout;

namespace Chess.Lib.UI;

/// <summary>
/// The surface-type-agnostic face of <see cref="PixelGameDisplay{TSurface}"/> — lets a host that
/// picks its renderer at runtime (Chess.Web: WebGL vs CPU fallback) hold one display reference.
/// </summary>
public interface IPixelGameDisplay : IGameDisplay
{
    /// <inheritdoc cref="PixelGameDisplay{TSurface}.Render"/>
    void Render();

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.OnResize"/>
    void OnResize(int width, int height);

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.StatusOverride"/>
    string? StatusOverride { get; set; }
}

/// <summary>
/// Renderer-agnostic pixel game display: board (via <see cref="GameUI"/>) + move-history panel
/// (right) + status bar (bottom), laid out with <see cref="PixelLayout"/>. Hoisted verbatim from
/// Chess.GUI's VkGameDisplay — nothing here was Vulkan-specific; the desktop display is now a
/// thin <c>PixelGameDisplay&lt;VulkanContext&gt;</c> subclass and Chess.Web drives the same class
/// over WebGlContext/RgbaImage. History rows are a declarative Layout tree, so each ply cell's
/// click region is auto-bound to its drawn rect (draw == hit); <see cref="ResolveHistoryClick"/>
/// feeds GameUI's playback navigation.
/// </summary>
/// <typeparam name="TSurface">The renderer surface type (e.g., VulkanContext, WebGlContext, RgbaImage).</typeparam>
public class PixelGameDisplay<TSurface> : PixelWidgetBase<TSurface>, IPixelGameDisplay
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

    public PixelGameDisplay(Renderer<TSurface> renderer) : base(renderer)
    {
        _labelFont = FontPaths.DejaVuSans;
    }

    /// <summary>The display's canvas background — hosts that clear the surface themselves (e.g.
    /// Chess.Web's per-frame Clear) must use this color so the areas GameUI paints with its own
    /// backgroundColor and the raw-cleared surface don't band.</summary>
    public static RGBAColor32 Background => BackgroundColor;

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    /// <summary>
    /// When set, replaces the derived status-bar text (game status / setup / playback hints) —
    /// used by hosts to surface transient states the display can't infer, e.g. Chess.Web's
    /// "White (AI) thinking…" while the search blocks the UI thread. Null = derived text.
    /// </summary>
    public string? StatusOverride { get; set; }

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
        _gameUI.Render<TSurface, Renderer<TSurface>>(Renderer, boardClip);

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

        var visibleRows = _gameUI.HistoryViewportRows;
        var (moveCount, _, startMove) = _gameUI.HistoryWindow(visibleRows);
        var highlightPly = _gameUI.Mode == GameUIMode.Playback ? _gameUI.PlaybackPlyIndex : (int?)null;

        var rowCount = Math.Min(visibleRows, moveCount - startMove);
        if (rowCount <= 0) return;

        // Build the rows as a declarative Layout tree: an idx column + two proportional
        // ply columns per row. RenderLayout draws each cell AND auto-binds its click region
        // from the same arranged rect, so the history hit-targets cannot drift from what's
        // drawn (replacing the previous hand-mirrored RegisterClickable coordinates).
        var idxColW = fontSize * 3.5f;
        var rows = new Layout.Node[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            var moveIdx = startMove + i;
            var whitePlyIdx = moveIdx * 2;
            var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(whitePlyIdx);
            var hasBlack = whitePlyIdx + 1 < plyCount;

            var idxCell = Layout.Builder
                .Text(idxStr.Trim(), fontSize, HistoryIndexColor, TextAlign.Far, TextAlign.Center)
                .WFixed(idxColW).HStar();

            var whiteCell = HistoryPlyCell(whitePly.ToString(), whitePlyIdx, highlightPly == whitePlyIdx);

            var blackCell = hasBlack
                ? HistoryPlyCell(plies.GetRecordAndPGNIdx(whitePlyIdx + 1).Ply.ToString(),
                    whitePlyIdx + 1, highlightPly == whitePlyIdx + 1)
                : Layout.Builder.Spacer().Stretch();

            rows[i] = Layout.Builder.HStack(idxCell, whiteCell, blackCell).RowH(rowH);
        }

        var contentY = rect.Y + headerH + 4;
        var rowsRect = new RectF32(rect.X, contentY, rect.Width, rect.Height - (contentY - rect.Y));
        RenderLayout(Layout.Builder.VStack(rows), rowsRect, _labelFont, dpiScale: 1f);
    }

    /// <summary>Builds one clickable ply cell for the history tree, highlighting it during playback.</summary>
    private Layout.Node HistoryPlyCell(string ply, int plyIndex, bool highlight)
    {
        var cell = Layout.Builder
            .Text(ply, ChromeFontSize, highlight ? PlaybackHighlightText : FontColor, TextAlign.Near, TextAlign.Center)
            .Stretch()
            .Clickable(new HitResult.ListItemHit("History", plyIndex));
        return highlight ? cell.Bg(PlaybackHighlightBg) : cell;
    }

    private void RenderStatusBar(RectF32 rect)
    {
        var fontSize = ChromeFontSize;

        var status = StatusOverride
            ?? (_game is null || _gameUI is null ? "" : _gameUI.StatusLine());

        RenderTextBar(status, _labelFont,
            rect.X, rect.Y, rect.Width, rect.Height,
            fontSize, StatusBarBg, FontColor,
            horizontalPadding: 8f, alignX: TextAlign.Near, alignY: TextAlign.Center);
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
