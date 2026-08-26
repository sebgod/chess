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

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.HandleHistoryPointer"/>
    bool HandleHistoryPointer(InputEvent evt);

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.HandleDragPointer"/>
    bool HandleDragPointer(InputEvent evt);

    /// <inheritdoc cref="PixelGameDisplay{TSurface}.PageHistory"/>
    void PageHistory(int direction);
}

/// <summary>
/// Renderer-agnostic pixel game display: a centred board (via <see cref="GameUI"/>) flanked by the
/// move history and the captured piles, with a status bar top and bottom — or, on a surface too narrow
/// for gutters, a full-width board with the history stacked below it (see <see cref="ArrangeFrame"/>,
/// which resolves either shape from one declarative <see cref="Layout"/> tree). Originally Chess.GUI's
/// Vulkan display, but nothing here is Vulkan-specific: the desktop GUI, Chess.Droid (Android), and
/// Chess.Web all instantiate this class directly over their surface type (VulkanContext / RgbaImage
/// / WebGlContext). History rows are a declarative Layout tree too, so each ply cell's
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

    private readonly string _labelFont;
    private GameUI? _gameUI;
    private volatile bool _hasPendingUpdate;
    private Game? _game;

    // History-panel scroll model (DIR.Lib 6.15). Owned by the display and mutated ONLY on the
    // render/event thread (SetExtent in Render, HandleHistoryPointer/PageHistory from the host's
    // pointer/key dispatch) — never from the game thread, so it needs no queue funnel. Bottom anchor
    // = tail-follow the latest move; SnapToAtom because a history atom is one whole move row.
    private readonly ListScrollController _historyScroll = new()
    {
        Anchor = ScrollAnchor.Bottom,
        Mode = ScrollBarMode.Interactive,
        SnapToAtom = true,
    };
    private float _historyBarWidthPx;   // scrollbar column width from the last layout (for hit-testing)
    private bool _historyBarDrag;       // a scrollbar thumb/track interaction is mid-drag
    private int _lastSyncedPlaybackPly = -1;
    private GameUIMode _lastSyncedMode = GameUIMode.Playing;

    public PixelGameDisplay(Renderer<TSurface> renderer) : base(renderer)
    {
        _labelFont = FontPaths.DejaVuSans;
    }

    /// <summary>The display's canvas background — hosts that clear the surface themselves (e.g.
    /// Chess.Web's per-frame Clear) must use this color so the areas GameUI paints with its own
    /// backgroundColor and the raw-cleared surface don't band.</summary>
    public static RGBAColor32 Background => BackgroundColor;

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    /// <summary>True once <see cref="ResetGame"/> has created the game UI — hosts that order calls
    /// before the first ResetGame (e.g. setting the renderer transform ahead of the first layout)
    /// must check this before touching <see cref="UI"/>.</summary>
    public bool HasGameUI => _gameUI is not null;

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

    private bool _mirrorChrome;

    /// <summary>
    /// Mirrors the chrome layout in content space: the history panel docks to the LEFT of the board
    /// (landscape) or ABOVE it (portrait), and the board origin shifts to match. Composed with a
    /// 180° renderer <c>ContentTransform</c> this keeps the board and panel at the SAME physical
    /// screen positions while the frame's text turns to face the far player (across-the-table PvP,
    /// where the renderer flips each turn): only the text orientation changes — nothing visibly
    /// jumps sides. Relayouts a live game.
    /// </summary>
    public bool MirrorChrome
    {
        get => _mirrorChrome;
        set
        {
            if (_mirrorChrome == value) return;
            _mirrorChrome = value;
            if (_gameUI is not null)
                OnResize((int)Renderer.Width, (int)Renderer.Height);
            _hasPendingUpdate = true;
        }
    }

    /// <summary>False on touch-only hosts (Chess.Droid): drops keyboard hints ("[Ctrl+Arrows, Esc
    /// exit]") from the status line — there are no keys, and the hints overflow a phone-width bar.
    /// Playback is exited via the history header's "▶ Latest" chip instead.</summary>
    public bool KeyboardHints { get; set; } = true;

    /// <summary>The history list id the setup-mode "▶ Start game" chip binds its click to — its own,
    /// so a tap on it can never be mistaken for a ply.</summary>
    private const string SetupStartListId = "SetupStart";

    /// <summary>
    /// Raised when the setup-mode "▶ Start game" chip is tapped: the touch equivalent of the desktop's
    /// <c>s</c> key, since a custom game otherwise has no way to leave setup on a device with no
    /// keyboard. The host owns what "start" means (clear <c>UI.IsSetupMode</c>, let the engine open if
    /// it has the move), so the display only reports the tap.
    /// </summary>
    // System.Action, not Chess.Lib.Action (the repo's classic name collision).
    public System.Action? SetupStartRequested { get; set; }

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
        var frame = ArrangeFrame();

        _gameUI = new GameUI(game, (uint)frame.Board.Width, (uint)frame.Board.Height,
            mainFontColor: FontColor,
            backgroundColor: BackgroundColor,
            resolveHistoryClick: ResolveHistoryClick,
            topOffset: (int)frame.Board.Y,
            leftOffset: (int)frame.Board.X,
            capturedLayout: frame.CapturedPieces);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(frame.History.Height);
        _hasPendingUpdate = true;
    }

    public void OnResize(int width, int height)
    {
        if (_gameUI is null) return;

        var frame = ArrangeFrame();
        _gameUI = _gameUI.Resize((uint)frame.Board.Width, (uint)frame.Board.Height,
            topOffset: (int)frame.Board.Y, leftOffset: (int)frame.Board.X, capturedLayout: frame.CapturedPieces);
        _gameUI.HistoryViewportRows = ComputeHistoryVisibleRows(frame.History.Height);
    }

    public void Render()
    {
        if (_gameUI is null) return;

        BeginFrame();

        // The paint pass: with the board's drawn width known, the frame's two gutter Stars resolve to
        // the real space beside it — so the chrome always meets the board's actual edge.
        var content = _gameUI.ContentRect;
        var frame = ArrangeFrame(content.Width, capture: true);

        // GameUI owns its own shift inside the area it was handed (draw and hit-test alike), so what it
        // drew IS the clip. Bigger than the 8×8 grid (it includes the label margins), which is what
        // tells GameUI to render the chrome around the board too.
        _gameUI.Render<TSurface, Renderer<TSurface>>(Renderer, content);

        if (frame.UseSideHistory)
        {
            RenderHistoryPanel(PinInGutter(frame.History, HistoryPanelWidth, toInnerEdge: false));
            _gameUI.RenderCapturedColumn<TSurface, Renderer<TSurface>>(Renderer,
                ToRectInt(PinInGutter(frame.Captured, _gameUI.CapturedColumnWidth + ChromeFontSize, toInnerEdge: true)));
        }
        else if (frame.History.Height >= MinPortraitHistoryHeight)
        {
            // Anything shallower isn't a useful history and stays background.
            RenderHistoryPanel(frame.History);
        }

        RenderStatusBar(frame.Status);
        if (_safeAreaInsets.Top > 0)
            RenderTopStrip(new RectF32(0, 0, Renderer.Width, _safeAreaInsets.Top));
    }

    public void Dispose() { }

    private float ChromeFontSize => MathF.Max(13f, (int)Renderer.Height / 40f);
    private GameFrameMetrics ChromeMetrics => GameFrameMetrics.FromChromeFontSize(ChromeFontSize);
    private float HistoryPanelWidth => ChromeMetrics.HistoryPanelWidth;

    /// <summary>Header + two rows — anything shallower isn't a useful history and stays background.</summary>
    private float MinPortraitHistoryHeight => ChromeMetrics.MinStackedHistoryHeight;

    /// <summary>The arranged frame: the shape that was chosen, and every rect this display paints into.</summary>
    private readonly record struct Frame(
        GameFrameShape Shape,
        CapturedPiecesLayout CapturedPieces,
        RectF32 Board,
        RectF32 History,
        RectF32 Captured,
        RectF32 Status)
    {
        /// <summary>True when the chrome flanks the board rather than stacking below it.</summary>
        public bool UseSideHistory => Shape != GameFrameShape.Stacked;
    }

    /// <summary>
    /// Arranges the entire display from the shared <see cref="GameFrameLayout"/> — which picks the shape
    /// (flanked vs stacked, costed in board squares) and hands back the one declarative
    /// <see cref="Layout"/> tree that expresses it — and returns its slots in pixels.
    ///
    /// <para><paramref name="boardContentWidth"/> selects the sizing pass (0) or the paint pass (the width
    /// GameUI actually drew); see <see cref="GameFrameLayout.Build"/> for why the board's aspect forces
    /// two. <see cref="GameFrameLayout.AllowOffCentreBoard"/> is false here because any of these hosts
    /// can turn its frame for across-the-table play, and only a centred board survives that.</para>
    /// </summary>
    private Frame ArrangeFrame(float boardContentWidth = 0f, bool capture = false)
    {
        var frame = new GameFrameLayout(
            Renderer.Width, Renderer.Height, ChromeMetrics,
            _safeAreaInsets, _mirrorChrome, allowOffCentreBoard: false);

        var arranged = ArrangeLayout(
            frame.Build(boardContentWidth),
            frame.SafeArea,
            // Chess sizes its chrome from the surface height already (see ChromeFontSize), so the
            // tree's design units ARE device pixels — same as the history rows' RenderLayout call.
            dpiScale: 1f);

        // Publish the frame to the layout capture buffer -- what the DEBUG inspector's describe_layout
        // reads, and what damage-based repaint diffs against.
        //
        // This is a CAPTURE, spelled as the paint it technically is. Every node in the frame is a Fill
        // leaf (or a Spacer) with no Background and no Hit, so a null drawFill draws nothing and
        // registers nothing; the only effect PaintLayout has on this tree is recording it. The
        // alternative -- a capture-only entry point -- is a DIR.Lib change, and this tree does not need
        // one to be honest about what it is.
        //
        // Why it was missing: the board is arranged here and painted by hand, so it never went through
        // the capture-aware path the history rows use. The game screen therefore described itself to a
        // driver as ONE node (the history panel's title) with no board, no gutters and no status bar --
        // even though the tree naming all four has existed all along.
        //
        // Paint pass only. ArrangeFrame also runs twice for sizing, and capturing those would file
        // three frames in a buffer meant to describe one.
        if (capture) PaintLayout(arranged, dpiScale: 1f);

        static RectF32 Slot(ImmutableArray<Layout.ArrangedNode<float>> arranged, string key)
        {
            var r = GameFrameLayout.Slot(arranged, key);
            return new RectF32(r.X, r.Y, r.Width, r.Height);
        }

        return new Frame(
            frame.Shape, frame.CapturedLayout,
            Slot(arranged, GameFrameLayout.SlotBoard), Slot(arranged, GameFrameLayout.SlotHistory),
            Slot(arranged, GameFrameLayout.SlotCaptured), Slot(arranged, GameFrameLayout.SlotStatus));
    }

    /// <summary>
    /// Narrows a gutter slot to <paramref name="maxWidth"/> and pins it to one edge — the only piece of
    /// the frame the engine can't express, because "as wide as the gutter but no wider than N" needs a
    /// Star whose weight depends on the gutter it lands in. The history hugs the gutter's OUTER (screen)
    /// edge, so an ultra-wide gutter can't stretch two columns of move text across a third of the
    /// screen; the captured trays hug the INNER edge so they read as a tray beside the board. Both
    /// mirror with the chrome, so the flip leaves them on the physical edge they started on.
    /// </summary>
    private RectF32 PinInGutter(RectF32 gutter, float maxWidth, bool toInnerEdge)
    {
        var width = MathF.Min(gutter.Width, maxWidth);
        var isLeftGutter = gutter.X <= _safeAreaInsets.Left;
        var pinFarEdge = isLeftGutter == toInnerEdge; // the left gutter's inner edge is its right one
        return new RectF32(pinFarEdge ? gutter.X + gutter.Width - width : gutter.X, gutter.Y, width, gutter.Height);
    }

    private static RectInt ToRectInt(RectF32 rect) => new(
        ((int)(rect.X + rect.Width), (int)(rect.Y + rect.Height)),
        ((int)rect.X, (int)rect.Y));

    private int ComputeHistoryVisibleRows(float availH)
    {
        var fontSize = ChromeFontSize;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;
        return Math.Max(1, (int)((availH - headerH) / rowH));
    }

    private void RenderHistoryPanel(RectF32 rect)
    {
        var fontSize = ChromeFontSize;
        var headerH = fontSize * 2f;
        var rowH = fontSize * 1.5f;

        FillRect(rect.X, rect.Y, rect.Width, rect.Height, HistoryBg);
        RenderHistoryHeader(new RectF32(rect.X + HistoryPad, rect.Y, rect.Width - HistoryPad * 2f, headerH), fontSize);
        FillRect(rect.X + 4, rect.Y + headerH, rect.Width - 8, 1, HistorySepColor);

        if (_game is null || _gameUI is null) return;

        var plies = _game.Plies;
        var plyCount = plies.Count;
        if (plyCount == 0) return;

        var moveCount = (plyCount + 1) / 2;
        var highlightPly = _gameUI.Mode == GameUIMode.Playback ? _gameUI.PlaybackPlyIndex : (int?)null;

        // Feed the scroll controller this frame's geometry (rows measured in whole move-row atoms),
        // then keep it in step with playback. Chess lays out in device pixels with font-derived
        // sizing, so scale the DPI-independent scrollbar metrics up by the same font factor.
        var contentY = rect.Y + headerH + 4;
        var rowsRect = new RectF32(rect.X, contentY, rect.Width, rect.Height - (contentY - rect.Y));
        var barScale = MathF.Max(1f, fontSize / 13f);
        _historyBarWidthPx = ListScrollController.ScrollBarBaseWidthPx * barScale;
        _historyScroll.SetExtent(rowsRect, rowH, moveCount, barScale);
        SyncHistoryPlayback();

        var first = _historyScroll.FirstVisibleAtom;
        var count = Math.Min(_historyScroll.VisibleAtoms, moveCount - first);
        if (count <= 0) { _historyScroll.DrawScrollBar(FillRect); return; }

        // Build the visible rows as a declarative Layout tree: an idx column + two proportional
        // ply columns per row. RenderLayout draws each cell AND auto-binds its click region from the
        // same arranged rect, so the history hit-targets cannot drift from what's drawn. Rows lay out
        // in the controller's ContentArea, which reserves the scrollbar column when the list overflows.
        // The row itself is HistoryRowLayout's, shared with the terminal's list rows — only the palette
        // and the row height are ours (the row states no sizing, see BuildRow).
        var rows = new Layout.Node[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = HistoryRowLayout
                .BuildRow(plies, first + i, highlightPly, fontSize, HistoryPalette)
                .RowH(rowH);
        }

        RenderLayout(Layout.Builder.VStack(rows), _historyScroll.ContentArea, _labelFont, dpiScale: 1f);
        // Theme colours so the bar reads against the dark history panel (the DIR.Lib defaults are
        // near-black and vanish here): the separator tone for the track, the index grey for the thumb.
        _historyScroll.DrawScrollBar(FillRect, track: HistorySepColor, thumb: HistoryIndexColor);
    }

    /// <summary>The history panel's own side gutter — the inset the header and the separator sit inside.</summary>
    private const float HistoryPad = 8f;

    /// <summary>The history panel's title. Only ever scaled to fit, never reworded.</summary>
    private const string HistoryTitle = "Move History";

    /// <summary>
    /// The header strip: the panel's title, plus — in playback or setup — the chip that is the touch way out
    /// of a mode the desktop leaves by key ("▶ Latest" back to the live game, for Esc; "▶ Start" out of
    /// custom-game setup, for <c>s</c>).
    ///
    /// <para><b>One layout, not two positioners.</b> The chip is a docked strip of its own MEASURED width and
    /// the title takes the remainder, so the two rects are disjoint by construction. They used to be
    /// independent: the title was drawn across the whole panel width while the chip was placed at a
    /// hard-coded 55% of it. Both of those scale with the chrome font — but the panel does NOT. The panel is
    /// the flanking gutter clamped to <see cref="HistoryPanelWidth"/> (18 em), and a gutter squeezed to its
    /// <see cref="GameFrameMetrics.MinSideGutter"/> floor is 11. Every surface aspect between the
    /// flanked/stacked crossover and about 1.53 lands in that squeeze, and there "Move History" (7.6 em) plus
    /// "▶ Latest" (4.5 em) plus the gutters no longer fit the ~12 em the panel actually got — so the chip
    /// drew over the title's tail. A fraction cannot express "beside the chip"; a layout can.</para>
    ///
    /// <para>The title then has to stay inside that remainder, and it says so itself:
    /// <see cref="TextTrim.Shrink"/> scales it to whatever it is handed rather than truncating it, because
    /// "Move Hist…" is a worse header than a slightly smaller whole one. The painter does the fitting — this
    /// is only where the policy is chosen.</para>
    /// </summary>
    /// <param name="strip">The header's rect, already inset by the panel's <see cref="HistoryPad"/> gutter.</param>
    /// <param name="fontSize">The chrome font size; the title is drawn a tenth larger, the chip at it.</param>
    private void RenderHistoryHeader(RectF32 strip, float fontSize)
    {
        if (strip.Width <= 0f) return;

        var chip = HeaderChip();

        // The chip's strip is its own glyph width plus a little: the slack lands on the title side as a gap,
        // and it doubles as touch slop, since on a phone this chip is the only way out of playback.
        var chipStrip = chip is null
            ? 0f
            : Renderer.MeasureText(chip.Value.Label.AsSpan(), _labelFont, fontSize).Width + fontSize * 0.6f;

        // Below a couple of ems the title is unreadable noise beside a control, and a panel full of moves
        // hardly needs telling it holds moves — so the chip gets the strip to itself.
        var title = strip.Width - chipStrip < fontSize * 2f
            ? null
            : Layout.Builder.Text(HistoryTitle, fontSize * 1.1f, HistoryHeaderColor,
                TextAlign.Near, TextAlign.Center, TextTrim.Shrink).Stretch();

        if (chip is null)
        {
            if (title is not null) RenderLayout(title, strip, _labelFont, dpiScale: 1f);
            return;
        }

        // Far-aligned inside its strip, so the label keeps the panel's right gutter whatever it measures.
        var chipNode = Layout.Builder
            .Text(chip.Value.Label, fontSize, PlaybackHighlightText, TextAlign.Far, TextAlign.Center)
            .Stretch()
            .Clickable(chip.Value.Hit);

        RenderLayout(
            Layout.Builder.Dock(title ?? Layout.Builder.Spacer(), Layout.Builder.Right(chipNode, chipStrip)),
            strip, _labelFont, dpiScale: 1f);
    }

    /// <summary>
    /// The header chip's label and what a tap on it means, or null in ordinary play. Both click regions are
    /// auto-bound to the drawn rect by the layout paint: playback claims the index one past the last ply,
    /// which is <see cref="GameUI"/>'s exit-playback sentinel (see its TryHistoryClick), and setup claims
    /// <see cref="SetupStartListId"/> — its own list, so a tap on it can never be read as a ply.
    /// </summary>
    private (string Label, HitResult Hit)? HeaderChip()
    {
        if (_game is null || _gameUI is null) return null;
        return _gameUI.Mode switch
        {
            GameUIMode.Playback => ("▶ Latest",
                (HitResult)new HitResult.ListItemHit(GameUI.HistoryListId, _game.Plies.Count)),
            GameUIMode.Setup => ("▶ Start", new HitResult.ListItemHit(SetupStartListId, 0)),
            _ => null,
        };
    }

    /// <summary>This display's colours for a shared <see cref="HistoryRowLayout"/> row. No row background:
    /// <see cref="RenderHistoryPanel"/> has already filled the panel behind the rows.</summary>
    private static HistoryRowPalette HistoryPalette => new(
        Index: HistoryIndexColor,
        Ply: FontColor,
        Highlight: PlaybackHighlightText,
        HighlightBackground: PlaybackHighlightBg);

    /// <summary>
    /// Keeps the scroll offset in step with GameUI's playback state (read-only here — the sync runs
    /// on the render thread while GameUI is mutated on the game thread; a one-frame-stale int read is
    /// harmless). During playback the current ply's row is scrolled into view; leaving playback snaps
    /// back to the tail (re-arming the Bottom-anchor tail-follow).
    /// </summary>
    private void SyncHistoryPlayback()
    {
        if (_gameUI is null) return;
        var mode = _gameUI.Mode;
        if (mode == GameUIMode.Playback)
        {
            var ply = _gameUI.PlaybackPlyIndex;
            if (ply != _lastSyncedPlaybackPly || _lastSyncedMode != GameUIMode.Playback)
                _historyScroll.EnsureVisible(ply / 2);
            _lastSyncedPlaybackPly = ply;
        }
        else if (_lastSyncedMode == GameUIMode.Playback)
        {
            _historyScroll.EnsureVisible(Math.Max(0, _historyScroll.TotalAtoms - 1));
            _lastSyncedPlaybackPly = -1;
        }
        _lastSyncedMode = mode;
    }

    /// <summary>
    /// Handles history-panel scroll input (mouse wheel over the panel + scrollbar thumb/track drag),
    /// returning true when consumed. Called by the host's pointer dispatch BEFORE the game widget, on
    /// the render/event thread. Row taps are deliberately NOT handled here — they fall through to the
    /// click-to-navigate path (GameUI, game thread) so a tap still selects the exact ply (white vs
    /// black column). Only the scrollbar column starts a drag here.
    /// </summary>
    public bool HandleHistoryPointer(InputEvent evt)
    {
        switch (evt)
        {
            case InputEvent.Scroll(_, var sx, var sy, _):
                if (!_historyScroll.Viewport.Contains(sx, sy) || !_historyScroll.HandleInput(evt))
                    return false;
                _hasPendingUpdate = true;
                return true;

            case InputEvent.MouseDown(var dx, var dy, MouseButton.Left, _, _):
                if (!PointInHistoryScrollBar(dx, dy)) return false;
                _historyBarDrag = _historyScroll.HandleInput(evt);
                if (_historyBarDrag) _hasPendingUpdate = true;
                return _historyBarDrag;

            case InputEvent.MouseMove when _historyBarDrag:
                if (_historyScroll.HandleInput(evt)) _hasPendingUpdate = true;
                return true;

            case InputEvent.MouseUp(_, _, MouseButton.Left) when _historyBarDrag:
                _historyScroll.HandleInput(evt);
                _historyBarDrag = false;
                _hasPendingUpdate = true;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the setup drag ghost, returning true when consumed. Called by the host's pointer dispatch
    /// on the render/event thread, beside <see cref="HandleHistoryPointer"/> and for the reason that
    /// one's comment gives — bypassing the per-move queue so it stays smooth and never races game
    /// state. Safe because in every pixel host the pointer callback and the render run on the same
    /// thread, so the drag point is never read by the game thread.
    ///
    /// <para>It returns FALSE when nothing is in hand rather than swallowing all motion, and that is
    /// load-bearing: the menu and the lobby resolve their hover out of the same event stream, and a
    /// ghost that claimed every move would silently kill their highlights. <see cref="GameUI"/>'s own
    /// gate does the deciding — this only forwards its answer.</para>
    ///
    /// <para>The damage rects are DISCARDED here, unlike on the terminal: <see cref="RenderMove"/>
    /// repaints the whole frame regardless. They are still worth producing, and this is the exact
    /// place a <c>VulkanContext.AddFrameDamage</c> call would take them (SdlVulkan.Renderer 7.25) if a
    /// measured drag ever says the full repaint costs too much.</para>
    /// </summary>
    public bool HandleDragPointer(InputEvent evt)
    {
        if (evt is not InputEvent.MouseMove(var x, var y) || !HasGameUI)
            return false;

        var (response, _) = UI.HandlePointerMove((int)x, (int)y);
        if (!response.HasFlag(UIResponse.NeedsRefresh))
            return false;

        _hasPendingUpdate = true;
        return true;
    }

    /// <summary>Pages the history by (nearly) a viewport; direction -1 = up, +1 = down. Render thread.</summary>
    public void PageHistory(int direction)
    {
        _historyScroll.AtomOffset += direction * Math.Max(1, _historyScroll.VisibleAtoms - 1);
        _hasPendingUpdate = true;
    }

    /// <summary>Whether (x, y) lands in the history scrollbar column — only present when the list overflows.</summary>
    private bool PointInHistoryScrollBar(float x, float y)
    {
        if (_historyScroll.TotalAtoms <= _historyScroll.VisibleAtoms) return false; // fits → no bar
        var v = _historyScroll.Viewport;
        return x >= v.Right - _historyBarWidthPx && x < v.Right && y >= v.Y && y < v.Bottom;
    }

    private void RenderStatusBar(RectF32 rect)
    {
        var status = StatusOverride
            ?? (_game is null || _gameUI is null ? "" : _gameUI.StatusLine(KeyboardHints));

        // The bar doesn't clip: scale a too-long status down rather than overflow the screen edge.
        var fontSize = FitFontSize(status, _labelFont, ChromeFontSize, rect.Width - 16f);

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

        // Both ends are fitted: a long label would otherwise overrun its column and collide with the
        // counter across the camera gap. See FitFontSize.
        void DrawFitted(string text, float x, float w, TextAlign align)
        {
            if (w <= 0) return;
            DrawText(text, _labelFont, x, textY, w, textH,
                FitFontSize(text, _labelFont, fontSize, w), FontColor, align, TextAlign.Center);
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
        if (hit is HitResult.ListItemHit { ListId: GameUI.HistoryListId } historyHit)
            return historyHit.Index;

        // The setup chip rides the same tap path (the host routes every tap through GameUI, which
        // asks us to resolve it), but it isn't a ply — hand it to the host and swallow the click.
        if (hit is HitResult.ListItemHit { ListId: SetupStartListId })
            SetupStartRequested?.Invoke();

        return null;
    }
}
