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

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.SafeAreaInsets"/>
    (int Left, int Top, int Right, int Bottom) SafeAreaInsets { get; set; }

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.TopStripLabel"/>
    string? TopStripLabel { get; set; }
}

/// <summary>
/// Renderer-agnostic pixel game display: board (via <see cref="GameUI"/>) + move-history panel
/// (right) + status bar (bottom), laid out with <see cref="PixelLayout"/>. Originally Chess.GUI's
/// Vulkan display, but nothing here is Vulkan-specific: the desktop GUI, Chess.Droid (Android), and
/// Chess.Web all instantiate this class directly over their surface type (VulkanContext / RgbaImage
/// / WebGlContext). History rows are a declarative Layout tree, so each ply cell's
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

    // GameUI's natural aspect (height:width) — matches the web board canvas (760x840 == 9.5:10.5).
    private const float BoardAspect = 10.5f / 9.5f;

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

    private (int Left, int Top, int Right, int Bottom) _safeAreaInsets;

    /// <summary>
    /// Safe-area insets in pixels: keeps the board, history, and status bar clear of display
    /// cutouts, rounded screen corners, and system bars (phones; zero on desktop/web). The top
    /// inset becomes a stats strip (<see cref="TopStripLabel"/> left, derived move counter right)
    /// flanking the centered camera (portrait). Hosts must re-set this on every resize — the
    /// cutout moves to a SIDE inset in landscape, where the board shifts right of it instead.
    /// Setting it relayouts a live game.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom) SafeAreaInsets
    {
        get => _safeAreaInsets;
        set
        {
            if (_safeAreaInsets == value) return;
            _safeAreaInsets = value;
            if (_gameUI is not null)
                OnResize((int)Renderer.Width, (int)Renderer.Height);
            _hasPendingUpdate = true;
        }
    }

    /// <summary>Left-side text of the notch stats strip (e.g. the game mode: "You vs AI"). The
    /// right side is the derived move counter. Drawn only when <see cref="SafeAreaInsets"/>.Top is
    /// deep enough for legible text.</summary>
    public string? TopStripLabel { get; set; }

    /// <summary>False on touch-only hosts (Chess.Droid): drops keyboard hints ("[Ctrl+Arrows, Esc
    /// exit]") from the status line — there are no keys, and the hints overflow a phone-width bar.
    /// Playback is exited via the history header's "▶ Latest" chip instead.</summary>
    public bool KeyboardHints { get; set; } = true;

    /// <summary>
    /// Exact bounds of the top display cutout (the camera punch-hole) in pixels, when the host can
    /// query them (Android: <c>DisplayCutout.BoundingRectTop</c>). The notch strip then centers its
    /// text on the camera's row — the safe-area top inset is deeper than the cutout, so strip-center
    /// text would sit visibly below the camera — and keeps out of its real horizontal span. Null =
    /// generic strip-centered layout.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom)? TopCutout { get; set; }

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
            resolveHistoryClick: ResolveHistoryClick,
            topOffset: _safeAreaInsets.Top,
            leftOffset: _safeAreaInsets.Left);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(boardH);
        _hasPendingUpdate = true;
    }

    public void OnResize(int width, int height)
    {
        if (_gameUI is null) return;

        var (boardW, boardH) = ComputeBoardArea();
        _gameUI = _gameUI.Resize((uint)boardW, (uint)boardH,
            topOffset: _safeAreaInsets.Top, leftOffset: _safeAreaInsets.Left);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(boardH);
    }

    public void Render()
    {
        if (_gameUI is null) return;

        BeginFrame();

        // Use ComputeBoardArea() for the clip rect — must match the dimensions
        // passed to GameUI constructor/Resize to keep overlay sizing consistent.
        var (boardW, boardH) = ComputeBoardArea();

        var (l, t, r, b) = _safeAreaInsets;
        var totalW = (float)Renderer.Width;
        var totalH = (float)Renderer.Height;
        // Chrome lays out inside the safe area: the status bar lands above the gesture-bar/rounded
        // bottom, and the top inset is drawn as the stats strip below. Insets are zero on desktop.
        var layout = new PixelLayout(new RectF32(l, t, totalW - l - r, totalH - t - b));

        // Status bar: full-width bottom strip in every layout.
        var statusRect = layout.Dock(PixelDockStyle.Bottom, StatusBarHeight);

        // History panel: right of the board in landscape; in portrait (phones) a fixed-width side
        // panel can't share a narrow screen without squeezing the board to nothing (see
        // ComputeBoardArea), so the history takes the leftover strip BELOW the board instead — the
        // full-width board at its natural aspect leaves a tall gap above the status bar.
        var showSideHistory = !IsPortrait;
        var historyRect = showSideHistory ? layout.Dock(PixelDockStyle.Right, HistoryPanelWidth) : default;

        // The board's shift inside the safe area lives INSIDE GameUI (top/leftOffset, draw and
        // hit-test alike), so the clip spans from the surface origin through the shifted board.
        var boardClip = new RectInt((l + boardW, t + boardH), PointInt.Origin);
        _gameUI.Render<TSurface, Renderer<TSurface>>(Renderer, boardClip);

        if (showSideHistory)
        {
            RenderHistoryPanel(historyRect);
        }
        else
        {
            var histTop = t + boardH;
            var histH = statusRect.Y - histTop;
            if (histH >= MinPortraitHistoryHeight)
                RenderHistoryPanel(new RectF32(l, histTop, totalW - l - r, histH));
        }
        RenderStatusBar(statusRect);
        if (t > 0)
            RenderTopStrip(new RectF32(0, 0, totalW, t));
    }

    public void Dispose() { }

    private float ChromeFontSize => MathF.Max(13f, (int)Renderer.Height / 40f);
    private float HistoryPanelWidth => ChromeFontSize * HistoryPanelWidthFactor;
    private float StatusBarHeight => ChromeFontSize * StatusBarHeightFactor;

    /// <summary>Portrait when the surface is taller than wide — phones, where the desktop
    /// board-left / history-right split doesn't fit: the history panel is a width derived from
    /// ChromeFontSize (a function of height), so on a tall narrow screen it exceeds the whole width
    /// and the board area would go negative.</summary>
    private bool IsPortrait => Renderer.Height > Renderer.Width;

    private (int BoardW, int BoardH) ComputeBoardArea()
    {
        // Layout math runs on the safe-area dimensions; the unsafe strips are chrome-free (the top
        // inset hosts the stats strip, the rest stays background).
        var (l, t, r, b) = _safeAreaInsets;
        var totalW = (int)Renderer.Width - l - r;
        var totalH = (int)Renderer.Height - t - b;

        if (IsPortrait)
        {
            // Board spans the full width at its natural aspect; the history panel is dropped and the
            // status bar keeps the bottom strip. Clamp so the board never exceeds the space above it.
            var availH = totalH - (int)StatusBarHeight;
            var boardH = Math.Min(availH, (int)(totalW * BoardAspect));
            return (totalW, boardH);
        }

        return (totalW - (int)HistoryPanelWidth, totalH - (int)StatusBarHeight);
    }

    /// <summary>Header + two rows — anything shallower isn't a useful history and stays background.</summary>
    private float MinPortraitHistoryHeight => ChromeFontSize * 5f;

    /// <summary>Height available to the portrait below-board history: from the board's bottom edge
    /// down to the top of the status bar (both already inside the safe area).</summary>
    private float PortraitHistoryHeight(int boardH)
    {
        var (_, t, _, b) = _safeAreaInsets;
        return (int)Renderer.Height - b - StatusBarHeight - (t + boardH);
    }

    private int ComputeHistoryVisibleRows(int boardH)
    {
        var fontSize = ChromeFontSize;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;
        // Landscape: the side panel is board-height. Portrait: the below-board strip's height.
        var availH = IsPortrait ? PortraitHistoryHeight(boardH) : boardH;
        return Math.Max(1, (int)((availH - headerH) / rowH));
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

        // During playback, a "▶ Latest" chip in the header is the touch path back to the live game
        // (desktop has Esc). Its click region is auto-bound by RenderLayout; the index one past the
        // last ply is GameUI's exit-playback sentinel (see TryHistoryClick).
        if (_gameUI.Mode == GameUIMode.Playback)
        {
            var chip = Layout.Builder
                .Text("▶ Latest", fontSize, PlaybackHighlightText, TextAlign.Far, TextAlign.Center)
                .Stretch()
                .Clickable(new HitResult.ListItemHit("History", _game.Plies.Count));
            RenderLayout(Layout.Builder.HStack(chip),
                new RectF32(rect.X + rect.Width * 0.55f, rect.Y, rect.Width * 0.45f - 8, headerH),
                _labelFont, dpiScale: 1f);
        }

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
            ?? (_game is null || _gameUI is null ? "" : _gameUI.StatusLine(KeyboardHints));

        // The bar doesn't clip: scale a too-long status down rather than overflow the screen edge.
        var available = rect.Width - 16f;
        var measured = Renderer.MeasureText(status.AsSpan(), _labelFont, fontSize).Width;
        if (measured > available && available > 0)
            fontSize = MathF.Max(10f, fontSize * (available / measured));

        RenderTextBar(status, _labelFont,
            rect.X, rect.Y, rect.Width, rect.Height,
            fontSize, StatusBarBg, FontColor,
            horizontalPadding: 8f, alignX: TextAlign.Near, alignY: TextAlign.Center);
    }

    /// <summary>
    /// The notch row (the safe-area top inset): filled like the status bar so the cutout reads as a
    /// deliberate top bar, with the host label (game mode) left and the derived move counter right.
    /// Text hugs the edges so the centered camera punch-hole stays clear, and the side padding scales
    /// with the strip height to clear the rounded screen corners.
    /// </summary>
    private void RenderTopStrip(RectF32 rect)
    {
        FillRect(rect.X, rect.Y, rect.Width, rect.Height, StatusBarBg);

        // Notch-row text reads as system chrome, not content: status-bar-small (well under half the
        // strip height), hugging the left/right edges. The centered camera halves the usable run —
        // each side gets from the corner padding to the keep-out, so ~40% of the width apiece.
        var fontSize = MathF.Min(ChromeFontSize * 0.75f, rect.Height * 0.32f);
        if (fontSize < 9f) return; // too shallow for legible text — keep the bar, skip the stats

        var pad = MathF.Max(12f, rect.Height * 0.5f); // corners intrude ~half the strip at mid-height

        // With the real cutout known, line the text row up with the camera (the strip is deeper than
        // the cutout, so strip-centering sits visibly below it) and keep out of its true span plus a
        // text-sized gap. Otherwise fall back to strip-centered text and a generic middle keep-out.
        float textY, textH, leftEnd, rightStart;
        if (TopCutout is var (cl, ct, cr, cb) && cr > cl)
        {
            textH = MathF.Min(rect.Height, fontSize * 1.5f);
            textY = MathF.Max(rect.Y, (ct + cb) / 2f - textH / 2f);
            var gap = fontSize;
            leftEnd = cl - gap;
            rightStart = cr + gap;
        }
        else
        {
            textY = rect.Y;
            textH = rect.Height;
            leftEnd = rect.X + pad + (rect.Width - 2 * pad) * 0.4f;
            rightStart = rect.X + rect.Width - pad - (rect.Width - 2 * pad) * 0.4f;
        }
        var leftW = leftEnd - (rect.X + pad);
        var rightW = rect.X + rect.Width - pad - rightStart;

        // DrawText does NOT clip to the given width, so a long label would overrun its column and
        // collide with the counter across the camera gap — measure and scale down to fit instead
        // (same approach as DIR.Lib's PixelMenuWidget width cap).
        void DrawFitted(string text, float x, float w, TextAlign align)
        {
            if (w <= 0) return;
            var fs = fontSize;
            var measured = Renderer.MeasureText(text.AsSpan(), _labelFont, fs).Width;
            if (measured > w)
                fs = MathF.Max(10f, fs * (w / measured));
            DrawText(text, _labelFont, x, textY, w, textH,
                fs, FontColor, align, TextAlign.Center);
        }

        if (!string.IsNullOrEmpty(TopStripLabel))
            DrawFitted(TopStripLabel, rect.X + pad, leftW, TextAlign.Near);
        if (_game is not null)
            DrawFitted($"Move {_game.Plies.Count / 2 + 1}", rightStart, rightW, TextAlign.Far);
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
