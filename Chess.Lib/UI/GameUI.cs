using System.Collections.Immutable;
using DIR.Lib;

namespace Chess.Lib.UI;

public class GameUI
{
    private const string FontDejaVuSans = "Fonts/DejaVuSans.ttf";
    private const string FontMerida     = "Fonts/Merida.ttf";

    private static readonly RGBAColor32 FontColorBlack      = new RGBAColor32(0, 0, 0, 0xff);
    private static readonly RGBAColor32 FontColorWhite      = new RGBAColor32(0xfd, 0xfd, 0xfd, 0xff);
    private static readonly RGBAColor32 FontColorGrey       = new RGBAColor32(0x70, 0x70, 0x70, 0xff);
    private static readonly RGBAColor32 BlackSquareFill     = new RGBAColor32(0xD1, 0x8B, 0x47, 0xff);
    private static readonly RGBAColor32 WhiteSquareFill     = new RGBAColor32(0xFF, 0xCE, 0x9E, 0xff);
    private static readonly RGBAColor32 OverlayFill         = new RGBAColor32(0xFF, 0xCE, 0x9E, 0xCC);
    private static readonly RGBAColor32 SelectedSquareFill  = new RGBAColor32(0xCD, 0x5C, 0x5C, 0xff);
    private static readonly RGBAColor32 CheckSquareFill     = new RGBAColor32(0xE9, 0xD5, 0x02, 0xff);
    private static readonly RGBAColor32 LastMoveBorderColor = new RGBAColor32(0x48, 0xA0, 0x48, 0xff);

    /// <summary>
    /// The last move was a CAPTURE — a border on its destination square, and the arrow that points at it.
    /// <para>
    /// Violet specifically, and not another red. This used to borrow <see cref="SelectedSquareFill"/>, which
    /// made a captured-on square look like a square you had picked up: the only thing distinguishing them was
    /// border-versus-fill, which is not a distinction anyone should have to make at a glance. It cost a real
    /// debugging session, where a capture highlight was read as a stuck selection.
    /// </para>
    /// <para>
    /// The warm half of the wheel was the obvious place to go and the wrong one: selection red, both check
    /// colours, the illegal-move red and a sequence orange all live there, and the BOARD itself is tan and
    /// cream, so a warm accent has the least contrast available. Green is what a capture marker has to differ
    /// from, and blue reads as playback chrome in the console (PixelGameDisplay's PlaybackHighlightBg). Violet
    /// is the free slot and it is cool against a warm board. Its only near neighbour is the magenta in
    /// <see cref="SequenceArrowColors"/>, and those two can never appear together — <see cref="LastMoveFull"/>
    /// returns early when <see cref="ExplicitArrows"/> is non-empty.
    /// </para>
    /// </summary>
    private static readonly RGBAColor32 CaptureBorderColor = new RGBAColor32(0x8A, 0x4F, 0xD0, 0xff);

    /// <summary><see cref="CaptureBorderColor"/> at the arrows' alpha, matching
    /// <see cref="LastMoveArrowColor"/>'s relationship to <see cref="LastMoveBorderColor"/>.</summary>
    private static readonly RGBAColor32 CaptureArrowColor = new RGBAColor32(0x8A, 0x4F, 0xD0, 0xCC);
    private static readonly RGBAColor32 RedCrossFill        = new RGBAColor32(0xDD, 0x00, 0x00, 0xFF);

    // Drawing primitive overlay colors
    private static readonly RGBAColor32 LegalMoveDotColor     = new RGBAColor32(0x00, 0x00, 0x00, 0x60);
    private static readonly RGBAColor32 LegalCaptureRingColor = new RGBAColor32(0x00, 0x00, 0x00, 0x80);
    private static readonly RGBAColor32 LastMoveArrowColor    = new RGBAColor32(0x48, 0xA0, 0x48, 0xCC);
    private static readonly RGBAColor32[] SequenceArrowColors = [
        new RGBAColor32(0x48, 0xA0, 0x48, 0xCC), // green   (1st ply)
        new RGBAColor32(0x30, 0x80, 0xE0, 0xCC), // blue    (2nd ply)
        new RGBAColor32(0xE0, 0x90, 0x20, 0xCC), // orange  (3rd ply)
        new RGBAColor32(0xC0, 0x40, 0xC0, 0xCC), // magenta (4th ply)
    ];
    private static readonly RGBAColor32 CheckRingColor        = new RGBAColor32(0xFF, 0xA5, 0x00, 0xFF);
    private static readonly RGBAColor32 SelectionRingColor    = new RGBAColor32(0xCD, 0x5C, 0x5C, 0xFF);

    private const float LegalDotRadiusFraction = 0.22f;
    private const float OverlayStrokeWidth = 3f;

    /// <summary>Opacity of the piece being dragged, and of what is left of it on the square it was
    /// lifted from. Both are mask alphas for <see cref="RGBAColor32.WithAlpha"/>, which PREMULTIPLIES
    /// rather than replaces, so a glyph colour that is already translucent stays proportionally so —
    /// and 0xff is exactly a no-op, which is why the undragged path can go through the same call.
    /// The origin is dimmed rather than emptied on purpose: an empty square under the picked-up tint
    /// reads as DELETED rather than lifted, and in setup mode Del genuinely does delete.</summary>
    private const byte GhostAlpha = 0xD9;

    /// <inheritdoc cref="GhostAlpha"/>
    private const byte LiftedOriginAlpha = 0x59;

    /// <summary>The mask that changes nothing: <c>WithAlpha(0xff)</c> is exact, not approximate.</summary>
    private const byte OpaqueAlpha = 0xff;

    /// <summary>White-on-black palette for chrome-less board rendering (terminal displays, MCP
    /// board snapshots) — pass as mainFontColor/backgroundColor. The ctor defaults are the
    /// inverse (black-on-white).</summary>
    public static readonly RGBAColor32 PlainFontColor = new(0xff, 0xff, 0xff, 0xff);

    /// <inheritdoc cref="PlainFontColor"/>
    public static readonly RGBAColor32 PlainBackgroundColor = new(0x00, 0x00, 0x00, 0xff);

    /// <summary>
    /// The colours whose MEANING must survive palette quantization, for hosts whose encoder has a
    /// colour budget (Sixel's 255 slots). A chess frame paints ~2000 unique colours — three flats
    /// cover ~95% of the area and the rest is glyph anti-aliasing — so a frequency-ranked palette is
    /// always oversubscribed, and an accent that loses the cut snaps to the nearest survivor: board
    /// tan or cream. It does not degrade, it disappears. Console.Lib 4.2 grew reserved palette slots
    /// for exactly this; this list is what chess asks it to hold. (Declared after every colour it
    /// names: static fields initialize in declaration order, and a forward reference would silently
    /// read as transparent black.)
    ///
    /// <para>Only OPAQUE accents belong here. The translucent ones (arrows, legal-move dots, the
    /// promotion overlay) composite into per-background blends before the encoder ever sees them, so
    /// reserving their constants would hold slots for colours no pixel carries. And greys may snap to
    /// neighbouring greys harmlessly — the list is accents whose loss changes meaning, plus the four
    /// flats as insurance (they win on frequency anyway; reserving them costs one declaration each).</para>
    /// </summary>
    public static readonly RGBAColor32[] ReservedPaletteColors =
    [
        SelectedSquareFill,   // == SelectionRingColor; the "picked up" tint
        CheckSquareFill,
        CheckRingColor,
        LastMoveBorderColor,
        CaptureBorderColor,   // the violet marker — the accent that prompted the 4.2 measurement
        RedCrossFill,
        PlainBackgroundColor, // == FontColorBlack
        PlainFontColor,
        FontColorWhite,
        WhiteSquareFill,
        BlackSquareFill,
    ];

    private readonly int _squareSize;
    private readonly int _topOffset;
    // The leftOffset the host passed, BEFORE the centering slack was folded into the content origin —
    // the x-side counterpart of _topOffset. Resize round-trips this one, not the computed offset, or
    // the centering would be added again on every resize and walk the board off the right edge.
    private readonly int _leftInset;
    // Every square, label and captured-band rect this class draws or hit-tests. The ctor decides WHERE
    // the content box goes (centring slack, host offsets, cell alignment); the box's interior is the
    // layout tree's, and nothing here recomputes a rect it could ask for.
    private readonly BoardLayout _layout;
    private readonly int _capturedCellHeight;
    private readonly CapturedPiecesLayout _capturedLayout;
    private readonly (uint X, uint Y)? _alignment;

    private readonly string _labelFont;
    private readonly string _pieceFont;
    private readonly float _labelFontSize;
    private readonly float _keymapFontSize;
    private readonly float _pieceFontSize;
    private readonly float _capturedFontSize;

    private readonly RGBAColor32 _mainFontColor;
    private readonly RGBAColor32 _backgroundColor;
    private readonly RGBAColor32 _capturedAreaColor;

    /// <summary>
    /// The captured gutter's rect as the host last handed it to
    /// <see cref="RenderCapturedColumn{TSurface, TRenderer}"/>; empty until the first frame, and unused
    /// by <see cref="CapturedPiecesLayout.Strips"/>, whose bands the board layout owns.
    /// </summary>
    private RectInt _capturedColumn;

    public static readonly string KeymapText =
        "Keyboard Controls\n" +
        "\n" +
        "Gameplay\n" +
        "  a-h    Select file\n" +
        "  1-8    Select rank\n" +
        "  Ctrl+F Flip board\n" +
        "  Esc    Cancel, then back to menu\n" +
        "\n" +
        "Playback\n" +
        "  Ctrl+Arrow  Navigate history\n" +
        "  PgUp/PgDn   Scroll history\n" +
        "  Esc         Exit playback\n" +
        "\n" +
        "Promotion\n" +
        "  n/b/r/q  Select piece\n" +
        "\n" +
        "Custom Setup\n" +
        "  a-h,1-8      Empty square: pick piece type\n" +
        "               Occupied: take it, then a square to move it\n" +
        "  p/n/b/r/q/k  Place piece\n" +
        "  Tab    Toggle side\n" +
        "  Del    Clear square\n" +
        "  Esc    Put the piece back\n" +
        "  s      Start game\n" +
        "\n" +
        "F1       Toggle this help\n" +
        "F8       Back to menu\n" +
        "F9       New game\n" +
        "F11      Toggle fullscreen";

    private const int PieceTypeStride = 7;

    /// <summary>How faint the bin's cross sits while nothing is in hand to drop in it.</summary>
    private const byte BinIdleAlpha = 0x66;

    /// <summary>The wash over the whole bin area while a release would take the piece — see
    /// <see cref="BinIsHot"/>. Red because the action is destructive, translucent because the pile it
    /// covers stays worth reading.</summary>
    private static readonly RGBAColor32 BinHotFill = new RGBAColor32(0xDD, 0x00, 0x00, 0x59);
    private const int LastMoveBorderWidth = 3;
    // Horizontal: margin(0.5) + 8 board squares + margin(0.5) = 9, plus padding
    private const float SquaresNeededX = 9.5f;
    // Vertical: topMargin(~0.6) + margin(0.5) + 8 board squares + margin(0.5) + capturedHeight(~0.6) = ~10.2, plus padding
    private const float SquaresNeededY = 10.5f;
    // Same, minus the two captured strips (CapturedPiecesLayout.External): margin(0.5) + 8 board
    // squares + margin(0.5) = 9, plus padding. No topMargin term — with no strip to keep on-screen
    // the content simply centres in whatever it is given (see minTopMargin below).
    private const float SquaresNeededYNoStrips = 9.2f;

    public GameUI(
        Game game,
        uint uiSizeX,
        uint uiSizeY,
        Position? selected = null,
        Position? pendingPromotion = null,
        string labelFont = FontDejaVuSans,
        string pieceFont = FontMerida,
        RGBAColor32? mainFontColor = null,
        RGBAColor32? backgroundColor = null,
        (uint X, uint Y)? alignment = null,
        Func<int, int, int?>? resolveHistoryClick = null,
        int topOffset = 0,
        int leftOffset = 0,
        CapturedPiecesLayout capturedLayout = CapturedPiecesLayout.Strips)
    {
        Game = game;
        ResolveHistoryClick = resolveHistoryClick;
        _alignment = alignment;
        _topOffset = topOffset;
        _leftInset = leftOffset;
        _capturedLayout = capturedLayout;
        _squareSize = CalculateSquareSize(uiSizeX, uiSizeY, capturedLayout);

        int margin;
        int stripHeight;
        int topMargin;
        if (alignment is (var alignX, var alignY))
        {
            var unit = Lcm(alignX, alignY);
            _squareSize = AlignDown(_squareSize, unit);
            margin = AlignDown(_squareSize / 2, unit);
            _capturedCellHeight = CapturedCellHeight(_squareSize);
            // Only the in-board strips take vertical space; External hands them to the host, and the
            // board grows into the space they no longer occupy.
            stripHeight = capturedLayout == CapturedPiecesLayout.Strips ? _capturedCellHeight : 0;
            var minTopMargin = stripHeight > 0 ? AlignUp(Math.Max(_squareSize / 2, stripHeight), unit) : 0;
            var contentHeight = 8 * _squareSize + 2 * margin + 2 * stripHeight;
            topMargin = Math.Max(minTopMargin, AlignDown(((int)uiSizeY - contentHeight) / 2 + stripHeight, unit));
        }
        else
        {
            margin = _squareSize / 2;
            _capturedCellHeight = CapturedCellHeight(_squareSize);
            stripHeight = capturedLayout == CapturedPiecesLayout.Strips ? _capturedCellHeight : 0;
            // A floor only where something has to stay on-screen above the board — the top captured
            // strip. Without it (External) the floor is 0, so the content centres cleanly instead of
            // being pushed down past the bottom of the area it was given.
            var minTopMargin = stripHeight > 0 ? Math.Max((int)(_squareSize * 0.5), stripHeight) : 0;
            var contentHeight = 8 * _squareSize + 2 * margin + 2 * stripHeight;
            topMargin = Math.Max(minTopMargin, ((int)uiSizeY - contentHeight) / 2 + stripHeight);
        }

        // Every y-coordinate (draw AND hit-test) is placed off the content origin, so folding the offset
        // in here shifts the whole board uniformly — pushes content below a phone's display cutout
        // (PixelGameDisplay's safe-area top inset) without per-site coordinate changes. An aligned
        // host (sixel cells) should pass a cell-aligned offset to keep square boundaries aligned.
        topMargin += topOffset;

        // Horizontal centering, the symmetric counterpart to topMargin's vertical centering: if the
        // given width has slack beyond the board+labels content, fold half of it into the origin. The
        // whole tree hangs off that one point — squares, labels, bands, and the hit test that reads the
        // same rects — so this addition centres all of them together and draw==hit-test is preserved.
        // When the host hands a surface-centered area this puts the board on the surface centre, which
        // is invariant under the across-the-table 180° flip (so the board no longer drifts each turn).
        // Left-aligned when there is no slack.
        var contentWidth = 8 * _squareSize + 2 * margin; // left rank-label margin + 8 squares + right margin
        var leftCentering = ((int)uiSizeX - contentWidth) / 2;
        if (leftCentering > 0)
        {
            if (alignment is (var alignCenterX, var alignCenterY))
                leftCentering = AlignDown(leftCentering, Lcm(alignCenterX, alignCenterY));
            leftOffset += leftCentering;
        }

        // topMargin is the LABEL margin's top edge; the content box starts one captured band above it.
        _layout = new BoardLayout(_squareSize, margin, stripHeight, (leftOffset, topMargin - stripHeight));

        _mainFontColor = mainFontColor ?? FontColorBlack;
        _backgroundColor = backgroundColor ?? FontColorWhite;
        _capturedAreaColor = ComputeCapturedAreaColor(_backgroundColor, _mainFontColor);
        _labelFont = Path.Combine(AppContext.BaseDirectory, labelFont);
        _labelFontSize = _squareSize * 0.3f;
        _keymapFontSize = _squareSize * 0.265f;
        _pieceFont = Path.Combine(AppContext.BaseDirectory, pieceFont);
        _pieceFontSize = _squareSize * 0.8f;
        _capturedFontSize = _squareSize * 0.4f;

        Selected = selected;
        PendingPromotion = pendingPromotion;
    }

    /// <summary>
    /// The square size that fits a board (plus its label margins and, with
    /// <see cref="CapturedPiecesLayout.Strips"/>, the captured strips) into the given area. Hosts
    /// also use it to COST a candidate layout in board squares before committing to one — see
    /// <c>PixelGameDisplay.UseSideHistory</c>.
    /// </summary>
    public static int CalculateSquareSize(uint uiSizeX, uint uiSizeY,
        CapturedPiecesLayout capturedLayout = CapturedPiecesLayout.Strips)
    {
        var neededY = capturedLayout == CapturedPiecesLayout.Strips ? SquaresNeededY : SquaresNeededYNoStrips;
        return (int)MathF.Min(uiSizeX / SquaresNeededX, uiSizeY / neededY);
    }

    /// <summary>The captured piles' row height for a given square size — one line of the captured
    /// font (0.4 square) with its line spacing.</summary>
    private static int CapturedCellHeight(int squareSize) => (int)MathF.Round(squareSize * 0.4f * 1.4f);

    public Game Game { get; }

    public Position? Selected { get; private set; }

    public Position? PendingPromotion { get; private set; }

    public GameUIMode Mode { get; set; } = GameUIMode.Playing;

    /// <summary>
    /// The board as setup mode found it. The removed-pieces pile is this minus the live board, so
    /// this snapshot is the whole of that feature's stored state — see <see cref="CountRemoved"/>.
    /// </summary>
    private Board _setupStartBoard;

    public bool IsSetupMode
    {
        get => Mode == GameUIMode.Setup;
        set
        {
            // Taken on the way IN, because the pile is measured against the board setup started from
            // rather than against a full army: a custom game begun on the EMPTY board has nothing to
            // be missing, and its pile stays blank however much is placed and cleared. Resize copies
            // the field, since it assigns Mode directly and never runs this setter.
            if (value && Mode != GameUIMode.Setup)
            {
                _setupStartBoard = Game.Board;
            }

            // Leaving setup drops any half-finished placement. The palette's render branch is keyed
            // on PendingPlacement ALONE (not on the mode), so a pending square used to survive the
            // transition as a ghost popup floating over the live game — reachable by pressing s with
            // the palette open, since the s check runs before the palette branch. A picked-up square
            // would likewise survive as a phantom selection that the first real click "moved" from.
            if (!value && Mode == GameUIMode.Setup)
            {
                PendingPlacement = default;
                Selected = default;
                PendingFile = null;
            }
            Mode = value ? GameUIMode.Setup : GameUIMode.Playing;
        }
    }

    public int PlaybackPlyIndex { get; private set; }

    /// <summary>
    /// Number of visible data rows in the history panel (set by the display on init/resize).
    /// </summary>
    public int HistoryViewportRows { get; set; }

    /// <summary>
    /// First move index shown in the history panel. <c>null</c> means auto (pinned to latest).
    /// </summary>
    public int? HistoryScrollStart { get; private set; }

    /// <summary>
    /// Optional delegate set by the display to resolve pixel coordinates to a ply index in the history panel.
    /// Returns null if the click is outside the history area.
    /// </summary>
    public Func<int, int, int?>? ResolveHistoryClick { get; }

    /// <summary>
    /// The list id a history ply cell claims its click under. Shared because both display families now
    /// author that cell as a <c>Layout.Node</c> and resolve the hit off the arranged tree — the pixel one
    /// through <c>PixelWidgetBase.HitTest</c>, the terminal one through <c>ScrollableList.DispatchRowHit</c>
    /// — so the id is part of one contract rather than a literal repeated per front-end.
    /// </summary>
    public const string HistoryListId = "History";

    /// <summary>
    /// Returns the board to display: historical board during playback, current board otherwise.
    /// </summary>
    public Board DisplayBoard => Mode == GameUIMode.Playback
        ? Game.BoardAtPly(PlaybackPlyIndex)
        : Game.Board;

    public Side PlacementSide { get; set; } = Side.White;

    /// <summary>
    /// When set, restricts move commitment to this side only — correspondence ("Play by Link")
    /// mode's one-local-move gate. Because <see cref="Chess.Lib.Game.CurrentSide"/> flips after
    /// a committed ply, the host sets this once to the local player's fixed colour and the gate
    /// self-locks after that one move — no separate "has moved" bookkeeping. Null (default) =
    /// unrestricted, the hot-seat behaviour every other mode uses. Only the two
    /// <see cref="TryPerformAction(Position)"/>/<see cref="TryPerformAction(Action)"/> commit
    /// paths check it; selection highlights on your own turn, history playback, and the keymap
    /// overlay are unaffected.
    /// </summary>
    public Side? MoveLockSide { get; set; }

    public Position? PendingPlacement { get; private set; }

    /// <summary>
    /// The square whose piece is "picked up" for relocation in setup mode, if any — set by
    /// designating an occupied square, cleared when it is dropped. Distinct from
    /// <see cref="Selected"/> because the palette state sets <see cref="Selected"/> too (to tint
    /// the square it refers to under the scrim); a piece is only in hand while NO palette is open.
    /// </summary>
    public Position? PickedUp => IsSetupMode && PendingPlacement is null ? Selected : null;

    private PointInt? _dragPoint;

    /// <summary>
    /// Where the pointer is during a setup drag, in CONTENT space — the space every host has already
    /// mapped its pointer into, so the ghost cannot drift away from the finger under the Android
    /// across-the-table rotation while every other hit-test stays right. Null means no ghost.
    ///
    /// <para>Derived through <see cref="PickedUp"/> rather than cleared at each exit: a drop, a
    /// palette opening, a cancel and leaving setup mode are four different code paths that all end
    /// the drag, and a fifth will be added one day. Gating the read is the whole of that rule; the
    /// backing field is reset when a piece is picked up so a stale point can never resurface as a
    /// ghost that appears before the pointer has moved.</para>
    ///
    /// <para>This is a pure RENDER HINT. The model still has the piece on <see cref="PickedUp"/> and
    /// the drop is decided by <see cref="HandlePointerUp"/> alone, so losing it loses the ghost and
    /// nothing else.</para>
    /// </summary>
    public PointInt? DragPoint => PickedUp is null ? null : _dragPoint;

    /// <summary>
    /// Pointer minus square origin, captured at pick-up. Preserving it is what stops the piece
    /// JUMPING under the cursor on the first motion event: grab a knight near its bottom-right
    /// corner and it stays held there rather than snapping its centre to the pointer.
    /// </summary>
    public PointInt GrabOffset { get; private set; }

    /// <summary>
    /// The square-sized rect the dragged piece is drawn into, or null when no ghost is showing.
    ///
    /// <para>It is EXACTLY one square, and that is a constraint to preserve rather than an
    /// observation: a one-square rect at an arbitrary sub-square offset straddles at most a 2x2
    /// block, which is what bounds a drag's repaint to four squares (eight for a moving ghost, old
    /// plus new). Scaling the dragged piece up — the obvious touch-UI embellishment — silently makes
    /// that 3x3 = nine, so it is a change to the damage model, not to a draw call.</para>
    /// </summary>
    public RectInt? GhostRect
    {
        get
        {
            if (DragPoint is not { } point)
                return null;

            var x = point.X - GrabOffset.X;
            var y = point.Y - GrabOffset.Y;
            // (LowerRight, UpperLeft) — the same order SquareRect builds, and the opposite of the one
            // that reads naturally. Reversing it yields an INVERTED rect whose Width/Height are
            // absolute differences, so it reports a plausible size and trips no guard anywhere.
            return new RectInt((x + _squareSize, y + _squareSize), (x, y));
        }
    }

    /// <summary>
    /// The board square the last press landed on, so <see cref="HandlePointerUp"/> can tell a drag
    /// from a click. Not a drag state machine: the press has already run the full grammar by the
    /// time this is set, and a release only ever adds the one thing a click cannot say — that the
    /// gesture ENDED somewhere else.
    /// </summary>
    private Position? _pressedSquare;

    public File? PendingFile { get; set; }

    public bool ShowingKeymap { get; set; }

    /// <summary>
    /// When true the board is drawn rotated 180° — Black's pieces at the bottom, files h→a shown
    /// left→right. Hosts auto-set this to the local player's colour (their pieces at the bottom) in
    /// games with a single local side (vs-computer, network); the Ctrl+F key toggles it at runtime.
    /// Every square↔pixel mapping (<see cref="SquareRect"/>, <see cref="FindSelected"/>, the
    /// coordinate labels, and the promotion/placement popups) honours it, so draw and hit-test can't
    /// disagree about orientation. Preserved across <see cref="Resize"/>.
    /// </summary>
    public bool FlipBoard { get; set; }

    /// <summary>
    /// The destination square of the last completed move, derived from game history.
    /// During playback, returns the ply at the current playback index.
    /// </summary>
    public (Position To, bool IsCapture)? LastMove
    {
        get
        {
            var plies = Game.Plies;
            if (Mode == GameUIMode.Playback)
            {
                // In playback, only show the marker for the ply at the current index.
                // PlaybackPlyIndex == -1 means "before the first move" — no marker.
                if (PlaybackPlyIndex < 0 || PlaybackPlyIndex >= plies.Count) return null;
                var ply = plies[PlaybackPlyIndex];
                return (ply.To, ply.Result.IsCapture());
            }
            return plies is [.., var last]
                ? (last.To, last.Result.IsCapture())
                : null;
        }
    }

    /// <summary>
    /// Explicit arrow overlays (from, to, isCapture). When non-empty, takes precedence over game ply arrows.
    /// Used by CLI/MCP rendering to show solution moves — multiple arrows render as a sequence with cycling colors.
    /// </summary>
    public IReadOnlyList<(Position From, Position To, bool IsCapture)> ExplicitArrows { get; set; } = [];

    /// <summary>
    /// Convenience setter for a single arrow. Replaces <see cref="ExplicitArrows"/> with a one-element list.
    /// </summary>
    public (Position From, Position To, bool IsCapture)? ExplicitArrow
    {
        get => ExplicitArrows.Count > 0 ? ExplicitArrows[0] : null;
        set => ExplicitArrows = value is { } arrow ? [arrow] : [];
    }

    /// <summary>
    /// Returns both the source and destination of the last completed move.
    /// During playback, returns the ply at the current playback index.
    /// </summary>
    private (Position From, Position To, bool IsCapture)? LastMoveFull
    {
        get
        {
            if (ExplicitArrows.Count > 0)
                return ExplicitArrows[0];

            var plies = Game.Plies;
            if (Mode == GameUIMode.Playback)
            {
                // In playback, only show the arrow for the ply at the current index.
                // PlaybackPlyIndex == -1 means "before the first move" — no arrow.
                if (PlaybackPlyIndex < 0 || PlaybackPlyIndex >= plies.Count) return null;
                var ply = plies[PlaybackPlyIndex];
                return (ply.From, ply.To, ply.Result.IsCapture());
            }
            return plies is [.., var last]
                ? (last.From, last.To, last.Result.IsCapture())
                : null;
        }
    }

    public int SquareSize => _squareSize;

    /// <summary>The natural width of a captured tray — a [count][piece] row pair. Hosts size the
    /// gutter slice they hand <see cref="RenderCapturedColumn{TSurface, TRenderer}"/> with it.</summary>
    public int CapturedColumnWidth => 2 * _capturedCellHeight;

    /// <summary>
    /// The drawn board's bounding box in surface coordinates: the 8×8 grid, its rank/file label
    /// margins, and — with <see cref="CapturedPiecesLayout.Strips"/> — the captured strips above and
    /// below. Hosts lay their chrome against this instead of re-deriving the square math, so a panel
    /// always meets the board's real edge however much centering slack the board absorbed.
    /// </summary>
    public RectInt ContentRect => _layout.Content;

    /// <summary>
    /// Creates a new <see cref="GameUI"/> with the given dimensions, preserving game state, selection,
    /// and style. Pass <paramref name="topOffset"/> when the safe-area top inset changed with the
    /// resize (rotation moves the cutout); null keeps the current offset. Pass
    /// <paramref name="capturedLayout"/> when the resize crosses a layout boundary (a rotation that
    /// gains or loses the side gutters that host the piles); null keeps the current one.
    /// </summary>
    public GameUI Resize(uint uiSizeX, uint uiSizeY, int? topOffset = null, int? leftOffset = null,
        CapturedPiecesLayout? capturedLayout = null)
    {
        var resized = new GameUI(
            Game, uiSizeX, uiSizeY,
            selected: Selected,
            pendingPromotion: PendingPromotion,
            labelFont: _labelFont,
            pieceFont: _pieceFont,
            mainFontColor: _mainFontColor,
            backgroundColor: _backgroundColor,
            alignment: _alignment,
            resolveHistoryClick: ResolveHistoryClick,
            topOffset: topOffset ?? _topOffset,
            leftOffset: leftOffset ?? _leftInset,
            capturedLayout: capturedLayout ?? _capturedLayout);
        resized.Mode = Mode;
        resized.PlaybackPlyIndex = PlaybackPlyIndex;
        resized.PlacementSide = PlacementSide;
        resized.PendingPlacement = PendingPlacement;
        resized._setupStartBoard = _setupStartBoard;
        resized.ShowingKeymap = ShowingKeymap;
        resized.HistoryViewportRows = HistoryViewportRows;
        resized.HistoryScrollStart = HistoryScrollStart;
        resized.PendingFile = PendingFile;
        // Not copying this would silently unlock the board on a window resize mid-link-game
        // (the web host rebuilds GameUI through Resize on every canvas metrics change).
        resized.MoveLockSide = MoveLockSide;
        // A window resize must not silently snap the board back to White-at-bottom (the web host
        // rebuilds GameUI via Resize on every canvas metrics change).
        resized.FlipBoard = FlipBoard;
        // Both halves of a drag in flight. Selected — and so PickedUp — is carried through the
        // constructor above, so dropping the drag point alone would leave a piece in hand with no
        // ghost: an INVISIBLE drag, which is worse than no ghost at all. The offset is rescaled
        // because a resize can change the square size, and an offset in old pixels would hold the
        // piece off-centre until the next motion event.
        resized._dragPoint = _dragPoint;
        resized.GrabOffset = _squareSize == resized._squareSize
            ? GrabOffset
            : new PointInt(
                GrabOffset.X * resized._squareSize / _squareSize,
                GrabOffset.Y * resized._squareSize / _squareSize);
        return resized;
    }

    public void Render<TSurface, TRenderer>(TRenderer renderer, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
        // board
        RenderBoard<TRenderer, TSurface>(renderer, clip);

        var boardRect = _layout.Board;

        // If the clip is entirely within the board area, skip chrome rendering
        if (clip.IsContainedWithin(boardRect))
        {
            return;
        }

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            // Column `idx` (left→right) and row `idx` (top→bottom) keep their pixel positions — the
            // layout gives all four margin cells for that column and row — and only which file/rank
            // label sits there flips. Unflipped: col idx = file idx, top row = rank 8.
            var fileLabelIdx = FlipBoard ? 7 - idx : idx;
            var rankLabelIdx = FlipBoard ? idx : 7 - idx;

            var fileText = Position.FromIndex((byte)fileLabelIdx, 0).File.ToLabel();
            var rankText = Position.FromIndex(0, (byte)rankLabelIdx).Rank.ToLabel();

            var top = _layout.FileLabel(idx, bottom: false);
            var bottom = _layout.FileLabel(idx, bottom: true);

            var left = _layout.RankLabel(idx, right: false);
            var right = _layout.RankLabel(idx, right: true);

            renderer.DrawText(fileText, _labelFont, _labelFontSize, _mainFontColor, top, vertAlignment: TextAlign.Center);
            renderer.DrawText(fileText, _labelFont, _labelFontSize, _mainFontColor, bottom, vertAlignment: TextAlign.Center);
            renderer.DrawText(rankText, _labelFont, _labelFontSize, _mainFontColor, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            renderer.DrawText(rankText, _labelFont, _labelFontSize, _mainFontColor, right, TextAlign.Center, vertAlignment: TextAlign.Center);
        }

        var currentSide = Game.CurrentSide;

        // The off-board piles: captures while playing, and the pieces setup has taken OFF while
        // setting up. The bands used to be skipped outright in setup mode, which left the one region
        // reserved for pieces that are not on the board blank exactly when the user is making some —
        // and left the bin with nowhere to live. Skipped only when the host draws the piles itself
        // (CapturedPiecesLayout.External; see RenderCapturedColumn, which does all of this again for
        // a gutter).
        if (_capturedLayout == CapturedPiecesLayout.Strips)
        {
#if DEBUG
            Span<byte> pileCounts = new byte[2 * PieceTypeStride];
#else
            Span<byte> pileCounts = stackalloc byte[2 * PieceTypeStride];
#endif
            var setup = Mode == GameUIMode.Setup;
            if (setup)
            {
                CountRemoved(pileCounts);
            }
            else
            {
                CountCaptured(pileCounts);
            }

            var (topStripSide, bottomStripSide) = CapturedStripSides();

            var bottomStrip = _layout.CapturedTray(bottom: true);
            if (clip.Contains(bottomStrip.UpperLeft.X, bottomStrip.UpperLeft.Y))
            {
                var bottomUsed = DrawPileBand<TRenderer, TSurface>(renderer, PileOf(pileCounts, bottomStripSide),
                    PilePieceSide(bottomStripSide), bottomStrip);
                if (setup) DrawBin<TRenderer, TSurface>(renderer,
                    BinArea(bottomStrip, bottomUsed, default, horizontal: true));
            }

            var topStrip = _layout.CapturedTray(bottom: false);
            if (clip.Contains(topStrip.UpperLeft.X, topStrip.UpperLeft.Y))
            {
                var topUsed = DrawPileBand<TRenderer, TSurface>(renderer, PileOf(pileCounts, topStripSide),
                    PilePieceSide(topStripSide), topStrip);
                if (setup) DrawBin<TRenderer, TSurface>(renderer,
                    BinArea(topStrip, topUsed, default, horizontal: true));
            }
        }

        // keymap overlay (F1)
        if (ShowingKeymap)
        {
            renderer.DrawScrim(boardRect, OverlayFill);

            renderer.DrawText(KeymapText, _labelFont, _keymapFontSize, FontColorBlack, boardRect,
                horizAlignment: TextAlign.Near, vertAlignment: TextAlign.Far);
        }
        // piece placement selection box (setup mode)
        else if (PendingPlacement is { } placementPos)
        {
            renderer.DrawScrim(boardRect, OverlayFill);

            var box = PieceTypeSelectionBox(placementPos);
            var offX = box.UpperLeft.X;
            var offY = box.UpperLeft.Y;

            for (var i = 0; i < 7; i++)
            {
                var squareRect = new RectInt((offX + _squareSize * (i + 1), offY + _squareSize), (offX + _squareSize * i, offY));
                renderer.FillRectangle(squareRect, i % 2 == 0 ? WhiteSquareFill : BlackSquareFill);

                if (i < 6)
                {
                    DrawPiece<TRenderer, TSurface>(renderer, new Piece((PieceType)(i + (int)PieceType.Pawn), PlacementSide), squareRect, _pieceFontSize);

                    if (Game[placementPos] is { } existingPiece && existingPiece.Side == PlacementSide && existingPiece.PieceType == (PieceType)(i + (int)PieceType.Pawn))
                    {
                        renderer.DrawText("\u2715", _labelFont, _pieceFontSize, RedCrossFill, squareRect, vertAlignment: TextAlign.Center);
                    }
                }
                else
                {
                    // Toggle-side button: show ⇄ symbol with half-and-half pieces
                    var oppositeSide = PlacementSide.ToOpposite();
                    DrawPiece<TRenderer, TSurface>(renderer, new Piece(PieceType.Pawn, oppositeSide), squareRect, _capturedFontSize);
                    renderer.DrawText("\u21C4", _labelFont, _labelFontSize, LastMoveBorderColor, squareRect, vertAlignment: TextAlign.Center);
                }
            }
        }
        // promote piece type selection box
        else if (PendingPromotion is { })
        {
            renderer.DrawScrim(boardRect, OverlayFill);

            var box = PromotePieceTypeSelectionBox(currentSide);
            var offX = box.UpperLeft.X;
            var offY = box.UpperLeft.Y;

            for (var i = 0; i < 4; i++)
            {
                var squareRect = new RectInt((offX + _squareSize * (i + 1), offY + _squareSize), (offX + _squareSize * i, offY));
                renderer.FillRectangle(squareRect, i % 2 == 0 ? WhiteSquareFill : BlackSquareFill);

                DrawPiece<TRenderer, TSurface>(renderer, new Piece((PieceType)(i + (int)PieceType.Knight), currentSide), squareRect, _pieceFontSize);
            }
        }
        // game-over banner — Playing only: a half-placed Setup board legitimately evaluates as
        // stalemate, and Playback positions carry the final game's status.
        else if (Mode == GameUIMode.Playing && Game is { GameStatus: GameStatus.Checkmate or GameStatus.Stalemate })
        {
            renderer.DrawScrim(boardRect, OverlayFill);

            var message = Game.GameStatus.ToMessage(Game.IsFinished ? Game.Winner : Game.CurrentSide);
            renderer.DrawText(message, _labelFont, _capturedFontSize, _mainFontColor, boardRect, vertAlignment: TextAlign.Center);
        }
    }

    /// <summary>
    /// Tallies both sides' captures (indexed <c>(side - 1) * PieceTypeStride + pieceType</c>) up to
    /// the ply on display — during playback that's the scrubbed position's piles, not the game's
    /// final ones.
    /// </summary>
    internal void CountCaptured(Span<byte> capturedPieceCounts)
    {
        var plies = Game.Plies;
        var plyCount = Mode == GameUIMode.Playback ? PlaybackPlyIndex + 1 : plies.Count;

        for (var plyIdx = 0; plyIdx < plyCount; plyIdx++)
        {
            var (_, ply) = plies.GetRecordAndPGNIdx(plyIdx);

            // The canonical predicate, not a hand-rolled Result pattern: the original spelled out
            // Capture and CaptureAndPromotion and thereby MISSED EnPassant — the one capture whose
            // taken pawn is not on the destination square, and whose victim silently never reached
            // the tray. Board records the e.p. victim correctly (it reads the pre-move square), so
            // the tally is the only place that dropped it.
            if (ply.Result.IsCapture() && ply.Captured is not PieceType.None)
            {
                var idx = plyIdx % 2 * PieceTypeStride + (int)ply.Captured;
                capturedPieceCounts[idx]++;
            }
        }
    }

    /// <summary>
    /// Tallies the pieces setup has taken OFF the board, indexed by the piece's OWN side — unlike
    /// <see cref="CountCaptured"/>, which indexes by the side that did the taking (a pile of trophies
    /// belongs to its captor; a pile of removals belongs to the army it was removed from).
    /// <see cref="PileOf"/> and <see cref="PilePieceSide"/> are what reconcile the two conventions,
    /// so the drawing is shared and neither caller has to invert anything.
    ///
    /// <para><b>Derived, not recorded.</b> It is the setup-start board's census minus the live one,
    /// per side and type, floored at zero — so it survives a resize, a flip and a re-place for free,
    /// and it needs no event to be routed through. Comparing COUNTS rather than squares is what stops
    /// a relocation reading as a removal: the same rook on a different square is not missing.</para>
    /// </summary>
    internal void CountRemoved(Span<byte> removedPieceCounts)
    {
        // Two censuses, because subtracting as we went would underflow a byte on any square whose
        // live piece outnumbers the start's before the rest of the board has been counted.
#if DEBUG
        Span<byte> onBoard = new byte[2 * PieceTypeStride];
#else
        Span<byte> onBoard = stackalloc byte[2 * PieceTypeStride];
#endif
        var live = Game.Board;

        foreach (var position in Position.AllPositions())
        {
            Tally(removedPieceCounts, _setupStartBoard[position]);
            Tally(onBoard, live[position]);
        }

        for (var i = 0; i < removedPieceCounts.Length; i++)
        {
            // Floored, not wrapped: setup may legitimately place MORE of a type than the board
            // started with (a second queen), and a byte would otherwise come back as 255.
            removedPieceCounts[i] = (byte)Math.Max(0, removedPieceCounts[i] - onBoard[i]);
        }

        static void Tally(Span<byte> counts, Piece piece)
        {
            if (piece.PieceType is not PieceType.None)
            {
                counts[((int)piece.Side - 1) * PieceTypeStride + (int)piece.PieceType]++;
            }
        }
    }

    /// <summary>
    /// The bin's glyph plate: one captured cell, centred in <paramref name="area"/>. The plate is the
    /// AFFORDANCE only — <see cref="IsOverBin"/> takes the whole area, because a piece you want gone is
    /// aimed at the region it will end up in and a pixel-accurate hit is the last thing a touch host or
    /// a terminal can offer.
    /// </summary>
    private RectInt BinPlate(in RectInt area)
    {
        var size = Math.Min(_capturedCellHeight, (int)Math.Min(area.Width, area.Height));
        var x = area.UpperLeft.X + ((int)area.Width - size) / 2;
        var y = area.UpperLeft.Y + ((int)area.Height - size) / 2;
        return new RectInt((x + size, y + size), (x, y));
    }

    /// <summary>
    /// Draws the bin at the centre of a captured band or gutter. The same red cross the placement
    /// palette puts over a piece it would clear, deliberately: one glyph for one meaning, so the two
    /// ways to remove a piece do not have to be learned separately. It fills its plate and goes fully
    /// opaque only while a piece is in hand — the rest of the time the pile it shares the area with is
    /// the more useful thing to read, and a permanently lit target reads as a button that does nothing.
    /// </summary>
    private void DrawBin<TRenderer, TSurface>(TRenderer renderer, in RectInt area)
        where TRenderer : Renderer<TSurface>
    {
        var plate = BinPlate(area);
        var armed = PickedUp is not null;

        if (BinIsHot)
        {
            // A REGION lights, not the glyph plate — and that is forced, not stylistic: the plate is one
            // captured cell and the ghost hovering it is a full square, so a lit plate would be entirely
            // hidden beneath the very piece it is answering.
            //
            // The region is the captured area MINUS the piles (see BinArea), not the whole of it. The
            // pile is the other thing that area is for, and washing it red made the removed pieces hard
            // to read at exactly the moment they are changing. Note this is deliberately WIDER than what
            // is lit: IsOverBin still accepts anywhere in the captured area, because a generous target
            // is worth more than the tidiness of the two matching — the lit band says "there", not
            // "only there".
            renderer.DrawScrim(area, BinHotFill);
        }
        else if (armed)
        {
            renderer.FillRectangle(plate, _capturedAreaColor);
        }

        renderer.DrawText("✕", _labelFont, _capturedFontSize,
            armed ? RedCrossFill : RedCrossFill.WithAlpha(BinIdleAlpha), plate,
            TextAlign.Center, vertAlignment: TextAlign.Center);
    }

    /// <summary>
    /// Whether releasing right now would bin the piece in hand — the bin's lit state, and deliberately
    /// the SAME predicate the release itself runs, so what lights up is what will happen rather than an
    /// approximation of it. Keyed on the pointer rather than on the ghost's own rect because that is
    /// what <see cref="HandlePointerUp"/> is handed.
    /// </summary>
    private bool BinIsHot => DragPoint is { } point && IsOverBin(point.X, point.Y);

    /// <summary>
    /// Whether a point is over the bin — the WHOLE captured area, not just the glyph plate. False
    /// outside setup mode, where the same region is real capture history and must stay inert.
    /// </summary>
    public bool IsOverBin(int x, int y)
    {
        if (Mode != GameUIMode.Setup)
            return false;

        if (_capturedLayout == CapturedPiecesLayout.Strips)
        {
            var (top, bottom) = CapturedTextRects();
            return top.Contains(x, y) || bottom.Contains(x, y);
        }

        // External: the gutter is the host's rect, learned at render time (see RenderCapturedColumn),
        // so this reads false until the first frame has been drawn — which is also the first moment a
        // pointer could be dragging anything.
        return _capturedColumn.Width > 0 && _capturedColumn.Contains(x, y);
    }

    /// <summary>One pile's seven counts out of the two-side buffer.</summary>
    private static ReadOnlySpan<byte> PileOf(ReadOnlySpan<byte> counts, Side pileEnd) =>
        counts.Slice(((int)pileEnd - 1) * PieceTypeStride, PieceTypeStride);

    /// <summary>
    /// Which colour the pile at a given end of the board is drawn in. A capture pile holds the pieces
    /// its owner TOOK, so it is the opposite colour; a removal pile holds pieces lifted off that end's
    /// own army, so it is that side's own.
    /// </summary>
    private Side PilePieceSide(Side pileEnd) =>
        Mode == GameUIMode.Setup ? pileEnd : pileEnd.ToOpposite();

    /// <summary>
    /// Which side's pile belongs at the display's top and bottom ends. A player's captures stand at
    /// THEIR end of the board (the pieces you took, beside your own back rank), so this tracks
    /// <see cref="FlipBoard"/> just like the board itself does — which is what keeps each player's
    /// trophies physically in front of them under the across-the-table 180° flip.
    /// </summary>
    private (Side Top, Side Bottom) CapturedStripSides() =>
        FlipBoard ? (Side.White, Side.Black) : (Side.Black, Side.White);

    /// <summary>
    /// Draws both captured piles as vertical trays inside <paramref name="rect"/> — a host's side
    /// gutter, for displays that took the piles out of the board area with
    /// <see cref="CapturedPiecesLayout.External"/> (which is what buys their board the ~1.2 squares
    /// the in-board strips would have cost). Each tray hugs the gutter end where its owner's back
    /// rank sits; see <see cref="CapturedStripSides"/>.
    /// </summary>
    public void RenderCapturedColumn<TSurface, TRenderer>(TRenderer renderer, in RectInt rect)
        where TRenderer : Renderer<TSurface>
    {
        // Stashed for the hit test. The gutter belongs to the HOST's frame, not to the board's content
        // box, so this call is the only place GameUI ever learns where it is — and taking the rect it
        // was actually handed is what keeps the bin's drawn target and IsOverBin the same rect by
        // construction rather than by two formulas agreeing.
        _capturedColumn = rect;
        if (rect.Width <= 0 || rect.Height <= 0) return;

#if DEBUG
        Span<byte> pileCounts = new byte[2 * PieceTypeStride];
#else
        Span<byte> pileCounts = stackalloc byte[2 * PieceTypeStride];
#endif
        var setup = Mode == GameUIMode.Setup;
        if (setup)
        {
            CountRemoved(pileCounts);
        }
        else
        {
            CountCaptured(pileCounts);
        }

        // A tray row is [count][piece] — two captured-font cells wide, centred in the gutter, and
        // never wider than the gutter the host handed over.
        var trayWidth = Math.Min((int)rect.Width, 2 * _capturedCellHeight);
        var x = rect.UpperLeft.X + ((int)rect.Width - trayWidth) / 2;
        var (topSide, bottomSide) = CapturedStripSides();

        var topUsed = DrawPileTray<TRenderer, TSurface>(renderer, PileOf(pileCounts, topSide), PilePieceSide(topSide),
            x, trayWidth, rect.UpperLeft.Y, step: 1);
        var bottomUsed = DrawPileTray<TRenderer, TSurface>(renderer, PileOf(pileCounts, bottomSide), PilePieceSide(bottomSide),
            x, trayWidth, (int)rect.LowerRight.Y - _capturedCellHeight, step: -1);

        // The gutter's MIDDLE is free by construction — the two trays hug its ends — which is what
        // makes it the bin's natural home in this shape.
        if (setup) DrawBin<TRenderer, TSurface>(renderer,
            BinArea(rect, topUsed, bottomUsed, horizontal: false));
    }

    /// <summary>
    /// Draws one pile as a vertical tray of [count][piece] rows starting at <paramref name="y0"/> and
    /// growing in <paramref name="step"/> (+1 = downwards from the gutter's top edge, -1 = upwards from
    /// its bottom edge). <paramref name="pile"/> is already that pile's seven counts and
    /// <paramref name="pieceSide"/> the colour to draw them in, so this is the same painter for
    /// captures and for setup removals — see <see cref="PilePieceSide"/>.
    /// </summary>
    private RectInt DrawPileTray<TRenderer, TSurface>(TRenderer renderer, ReadOnlySpan<byte> pile,
        Side pieceSide, int x, int width, int y0, int step)
        where TRenderer : Renderer<TSurface>
    {
        var cell = _capturedCellHeight;
        var half = width / 2;
        var y = y0;
        var rows = 0;

        for (var pieceIdx = 1; pieceIdx < PieceTypeStride; pieceIdx++)
        {
            var count = pile[pieceIdx];
            if (count == 0) continue;
            rows++;

            // Backing fill per row, so the stacked rows read as one tray (the strips fill their band
            // the same way).
            renderer.FillRectangle(new RectInt((x + width, y + cell), (x, y)), _capturedAreaColor);
            renderer.DrawText(Convert.ToString(count), _labelFont, _capturedFontSize, _mainFontColor,
                new RectInt((x + half, y + cell), (x, y)), TextAlign.Center, vertAlignment: TextAlign.Center);
            DrawPiece<TRenderer, TSurface>(renderer, new Piece((PieceType)pieceIdx, pieceSide),
                new RectInt((x + width, y + cell), (x + half, y)), _capturedFontSize);

            y += step * cell;
        }

        if (rows == 0) return default;

        // Growing UPWARDS puts y0 at the LAST row's top, so the extent has to be reconstructed rather
        // than read off y — the two directions do not share an origin.
        var top = step > 0 ? y0 : y0 - (rows - 1) * cell;
        return new RectInt((x + width, top + rows * cell), (x, top));
    }

    /// <summary>
    /// Draws one pile along <paramref name="band"/> — the layout's captured band, which spans exactly
    /// the eight board columns. Clearing the whole band first is what stops a shrinking pile leaving
    /// its own last frame behind. Slice and colour are the caller's, as in <see cref="DrawPileTray"/>.
    /// </summary>
    private RectInt DrawPileBand<TRenderer, TSurface>(TRenderer renderer, ReadOnlySpan<byte> pile, Side pieceSide, in RectInt band)
        where TRenderer : Renderer<TSurface>
    {
        var cellSize = (int)MathF.Round(_capturedFontSize * 1.4f);
        var x = band.UpperLeft.X;
        var y = band.UpperLeft.Y;
        renderer.FillRectangle(band, _capturedAreaColor);

        var pieceX = x;
        for (var pieceIdx = 1; pieceIdx < PieceTypeStride; pieceIdx++)
        {
            var count = pile[pieceIdx];
            if (count > 0)
            {
                var layoutCount = new RectInt((pieceX + cellSize, y + cellSize), (pieceX, y));
                renderer.DrawText(Convert.ToString(count), _labelFont, _capturedFontSize, _mainFontColor, layoutCount, vertAlignment: TextAlign.Center);
                pieceX += count <= 9 ? cellSize : 2 * cellSize;

                var layoutPiece = new RectInt((pieceX + cellSize, y + cellSize), (pieceX, y));
                DrawPiece<TRenderer, TSurface>(renderer, new Piece((PieceType)pieceIdx, pieceSide), layoutPiece, _capturedFontSize);
                pieceX += (int)(1.5 * cellSize);
            }
        }

        return pieceX == x ? default : new RectInt((pieceX, (int)band.LowerRight.Y), (x, band.UpperLeft.Y));
    }

    /// <summary>
    /// The slice of a captured band or gutter the pile is NOT using — where the bin shows itself.
    /// Derived from the rect the painter reports rather than recomputed, so the two can never disagree
    /// about how much room a pile takes; that advance is a fiddly little formula (a count cell that
    /// doubles past nine, then a piece cell, then a gap) and a second copy of it would drift.
    ///
    /// <para><paramref name="used"/> rects are subtracted from the ends: a band's pile grows from its
    /// left edge, a gutter's two piles from its top and bottom. Falls back to the whole
    /// <paramref name="area"/> when the piles have eaten all of it, so a full board's bin still shows
    /// itself somewhere rather than silently collapsing to nothing.</para>
    /// </summary>
    private static RectInt BinArea(in RectInt area, in RectInt usedNear, in RectInt usedFar, bool horizontal)
    {
        if (horizontal)
        {
            var left = usedNear.Width > 0 ? (int)usedNear.LowerRight.X : area.UpperLeft.X;
            return left < (int)area.LowerRight.X
                ? new RectInt(((int)area.LowerRight.X, (int)area.LowerRight.Y), (left, area.UpperLeft.Y))
                : area;
        }

        var top = usedNear.Height > 0 ? (int)usedNear.LowerRight.Y : area.UpperLeft.Y;
        var bottom = usedFar.Height > 0 ? usedFar.UpperLeft.Y : (int)area.LowerRight.Y;
        return bottom > top
            ? new RectInt(((int)area.LowerRight.X, bottom), (area.UpperLeft.X, top))
            : area;
    }

    private void RenderBoard<TRenderer, TSurface>(TRenderer renderer, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
        // Collect squares to draw and pieces to render. Both buffers are sized by the BOARD, not by a
        // legal army: setup mode lets a piece be placed on any empty square, so a standard board (already
        // at 32) plus one placement overflowed a 32-entry piece buffer and took the whole app down.
        Span<(RectInt Rect, RGBAColor32 Color)> squaresToDraw = stackalloc (RectInt, RGBAColor32)[64];
        Span<(Position Position, Piece Piece, RectInt Rect)> piecesToDraw = stackalloc (Position, Piece, RectInt)[64];
        var squareCount = 0;
        var pieceCount = 0;

        var detached = DetachedPiece();

        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var position = Position.FromIndex(fileIdx, rankIdx);
                // Both draw and hit-test go through SquareRect so the flip stays consistent; the
                // (fileIdx+rankIdx) colour parity below is orientation-invariant (180° preserves it).
                var rect = SquareRect(position);

                if (!rect.OverlapsWith(clip))
                {
                    continue;
                }

                var piece = DisplayBoard[position];

                RGBAColor32 squareFill;

                if (Selected == position)
                {
                    squareFill = SelectedSquareFill;
                }
                else if (Mode != GameUIMode.Playback && piece is { PieceType: PieceType.King } && Game is { GameStatus: GameStatus.Check } && piece.Side == Game.CurrentSide)
                {
                    squareFill = CheckSquareFill;
                }
                else if ((fileIdx + rankIdx) % 2 == 0)
                {
                    squareFill = BlackSquareFill;
                }
                else
                {
                    squareFill = WhiteSquareFill;
                }

                squaresToDraw[squareCount++] = (rect, squareFill);

                if (piece.PieceType is not PieceType.None)
                {
                    piecesToDraw[pieceCount++] = (position, piece, rect);
                }
            }
        }

        // Batch draw all squares in a single call
        renderer.FillRectangles(squaresToDraw[..squareCount]);

        // Last-move arrow(s) — drawn before pieces so pieces appear on top.
        // Multiple ExplicitArrows render as a sequence with cycling palette to show move order.
        if (ExplicitArrows.Count > 0)
        {
            for (var i = 0; i < ExplicitArrows.Count; i++)
            {
                var (from, to, isCapture) = ExplicitArrows[i];
                var color = isCapture ? CaptureArrowColor : SequenceArrowColors[i % SequenceArrowColors.Length];
                DrawLastMoveArrow<TRenderer, TSurface>(renderer, from, to, color);
            }
        }
        else if (LastMoveFull is (var arrowFrom, var arrowTo, var arrowIsCapture))
        {
            var arrowColor = arrowIsCapture ? CaptureArrowColor : LastMoveArrowColor;
            DrawLastMoveArrow<TRenderer, TSurface>(renderer, arrowFrom, arrowTo, arrowColor);
        }

        // Draw pieces after squares and arrow (pieces must be on top)
        for (var i = 0; i < pieceCount; i++)
        {
            var (position, piece, rect) = piecesToDraw[i];
            var alpha = detached is { Suppressed: { } suppressed } && suppressed == position
                ? detached.Value.SuppressedAlpha
                : OpaqueAlpha;
            DrawPiece<TRenderer, TSurface>(renderer, piece, rect, _pieceFontSize, alpha);
        }

        // Legal move dots (on top of pieces so dots and capture rings are visible)
        if (Mode == GameUIMode.Playing && Selected is { } selectedPos)
        {
            DrawLegalMoveDots<TRenderer, TSurface>(renderer, selectedPos);
        }

        // Last-move highlight border on the destination square
        if (LastMove is (var lastMoveTo, var lastMoveIsCapture))
        {
            var borderColor = lastMoveIsCapture ? CaptureBorderColor : LastMoveBorderColor;
            DrawLastMoveBorder<TRenderer, TSurface>(renderer, lastMoveTo, borderColor);
        }

        // Selection ring around selected piece
        if (Selected is { } selPos)
        {
            DrawSelectionRing<TRenderer, TSurface>(renderer, selPos);
        }

        // Check ring around king when in check
        if (Mode != GameUIMode.Playback && Game is { GameStatus: GameStatus.Check })
        {
            if (Game.Board.KingPosition(Game.CurrentSide) is { } kingPos)
            {
                DrawCheckRing<TRenderer, TSurface>(renderer, kingPos);
            }
        }

        // The detached piece goes LAST, so it sits over every square, arrow, dot and ring it crosses.
        // There is no z-order conflict with the placement palette, and that is not luck: a piece can
        // never be in hand while the palette is open, because PickedUp is null whenever
        // PendingPlacement is set.
        //
        // Drawn WITHOUT a clip test, unlike everything else here, because it is the one thing this
        // method draws that lives in SURFACE space rather than board space. Culling it against the clip
        // made a piece dragged towards the bin evaporate the moment its square cleared the content box
        // — about halfway across the gutter, since the hosts pass the board's own rect as the clip, so
        // the vanishing point was a rendering artifact that looked exactly like a deliberate boundary.
        // Costs nothing to keep: HandlePointerMove reports a ghost that has left the box as a FULL
        // repaint, so the ghost is always inside the damage actually being painted.
        if (detached is { } inFlight)
        {
            DrawPiece<TRenderer, TSurface>(renderer, inFlight.Piece, inFlight.Rect, _pieceFontSize, inFlight.Alpha);
        }
    }

    /// <summary>
    /// The one piece drawn detached from any square this frame: what to draw, where, and which square
    /// must be toned down behind it — stated as VALUES rather than read from
    /// <see cref="DragPoint"/> at the draw call.
    ///
    /// <para>That indirection is the whole difference between a move animation reusing this and
    /// reimplementing it, because a slide is the same rendering problem with different inputs: the
    /// piece that moved, drawn at <c>lerp(from, to, t)</c>, with a square suppressed behind it. Note
    /// the INVERSION, which is what would bite an implementation assuming the two were identical —
    /// during a drag the model still has the piece on its ORIGIN, so the origin is what is toned down;
    /// during an animation the ply is already committed, so the DESTINATION is what must be hidden
    /// until the slide lands. Hence a suppressed square and its alpha rather than a hard-coded
    /// "dim the origin".</para>
    /// </summary>
    private readonly record struct DetachedPieceDraw(
        Piece Piece, RectInt Rect, byte Alpha, Position? Suppressed, byte SuppressedAlpha);

    private DetachedPieceDraw? DetachedPiece() =>
        PickedUp is { } inHand
        && GhostRect is { } rect
        && DisplayBoard[inHand] is { PieceType: not PieceType.None } piece
            ? new DetachedPieceDraw(piece, rect, GhostAlpha, inHand, LiftedOriginAlpha)
            : null;

    private void DrawLastMoveBorder<TRenderer, TSurface>(TRenderer renderer, Position position, RGBAColor32 color)
        where TRenderer : Renderer<TSurface>
    {
        var inset = SquareRect(position).Inflate(-LastMoveBorderWidth);
        renderer.DrawRectangle(inset, color, LastMoveBorderWidth);
    }

    private (float X, float Y) SquareCenter(Position position)
    {
        var rect = SquareRect(position);
        return (
            (rect.UpperLeft.X + rect.LowerRight.X) / 2f,
            (rect.UpperLeft.Y + rect.LowerRight.Y) / 2f
        );
    }

    private void DrawLegalMoveDots<TRenderer, TSurface>(TRenderer renderer, Position selected)
        where TRenderer : Renderer<TSurface>
    {
        var dotRadius = Math.Max(1, (int)(_squareSize * LegalDotRadiusFraction));
        var strokeWidth = Math.Max(2f, _squareSize * 0.05f);

        foreach (var action in DisplayBoard.ValidMoves(Game.Plies, selected, Game.CurrentSide))
        {
            var (cx, cy) = SquareCenter(action.To);
            var isOccupied = DisplayBoard[action.To].PieceType is not PieceType.None;

            if (isOccupied)
            {
                // Outline ring around capturable piece
                var inset = (int)(_squareSize * 0.1f);
                var ringRect = SquareRect(action.To).Inflate(-inset);
                renderer.DrawEllipse(ringRect, LegalCaptureRingColor, strokeWidth);
            }
            else
            {
                // Filled dot at center of empty target square
                var dotRect = new RectInt(
                    new PointInt((int)(cx + dotRadius), (int)(cy + dotRadius)),
                    new PointInt((int)(cx - dotRadius), (int)(cy - dotRadius)));
                renderer.FillEllipse(dotRect, LegalMoveDotColor);
            }
        }
    }

    private void DrawLastMoveArrow<TRenderer, TSurface>(TRenderer renderer, Position from, Position to, RGBAColor32 color)
        where TRenderer : Renderer<TSurface>
    {
        var (x0, y0) = SquareCenter(from);
        var (x1, y1) = SquareCenter(to);
        var thickness = Math.Max(2, (int)(_squareSize * 0.07f));

        // The knight is the one piece whose move is not a straight line, so a straight arrow draws a
        // path it cannot take — and it crosses squares it never visits, which is exactly what makes a
        // diagram misleading rather than merely stylised. It moves two along a rank or file and then
        // one across, so that is what gets drawn.
        //
        // Detected from the GEOMETRY, not the piece: a (1, 2) delta is a knight's and nothing else's
        // — a king reaches one square, and everything else travels a rank, a file or a diagonal — and
        // neither LastMoveFull nor ExplicitArrows carries a piece type to ask. Which also means the
        // chess-mcp puzzle diagrams, which come through ExplicitArrows, get it for free.
        var files = Math.Abs((int)to.File - (int)from.File);
        var ranks = Math.Abs((int)to.Rank - (int)from.Rank);
        if ((files, ranks) is (1, 2) or (2, 1))
        {
            DrawKnightElbow<TRenderer, TSurface>(renderer, x0, y0, x1, y1, color, thickness);
            return;
        }

        renderer.DrawLine(x0, y0, x1, y1, color, thickness);
    }

    /// <summary>
    /// The knight's L: the LONG leg first — two squares along a rank or file — then the short
    /// perpendicular one.
    ///
    /// <para>The corner is filled as its own rectangle instead of letting the two legs overlap there.
    /// Every arrow colour in this file is translucent (<c>0xCC</c>), so an overlap blends twice and
    /// leaves a visibly darker blob at precisely the point a reader looks to see which way the piece
    /// turned. Each leg therefore stops half a thickness short of the corner and the square between
    /// them is drawn once.</para>
    ///
    /// <para>Long-versus-short is decided in SCREEN space, which is what keeps it right under
    /// <see cref="FlipBoard"/>: a 180° rotation negates both deltas and preserves their magnitudes.</para>
    /// </summary>
    private static void DrawKnightElbow<TRenderer, TSurface>(TRenderer renderer,
        float x0, float y0, float x1, float y1, RGBAColor32 color, int thickness)
        where TRenderer : Renderer<TSurface>
    {
        var half = thickness / 2f;
        var (cornerX, cornerY) = MathF.Abs(x1 - x0) > MathF.Abs(y1 - y0) ? (x1, y0) : (x0, y1);

        // One component of each direction is zero — both legs are axis-aligned — so this shortens each
        // one along its own axis and leaves the other end untouched.
        var (in1X, in1Y) = (MathF.Sign(cornerX - x0) * half, MathF.Sign(cornerY - y0) * half);
        var (out2X, out2Y) = (MathF.Sign(x1 - cornerX) * half, MathF.Sign(y1 - cornerY) * half);

        renderer.DrawLine(x0, y0, cornerX - in1X, cornerY - in1Y, color, thickness);
        renderer.DrawLine(cornerX + out2X, cornerY + out2Y, x1, y1, color, thickness);
        renderer.FillRectangle(
            new RectInt(
                ((int)MathF.Round(cornerX + half), (int)MathF.Round(cornerY + half)),
                ((int)MathF.Round(cornerX - half), (int)MathF.Round(cornerY - half))),
            color);
    }

    private void DrawCheckRing<TRenderer, TSurface>(TRenderer renderer, Position kingPosition)
        where TRenderer : Renderer<TSurface>
    {
        var inset = (int)(_squareSize * 0.08f);
        var ringRect = SquareRect(kingPosition).Inflate(-inset);
        renderer.DrawEllipse(ringRect, CheckRingColor, OverlayStrokeWidth);
    }

    private void DrawSelectionRing<TRenderer, TSurface>(TRenderer renderer, Position selected)
        where TRenderer : Renderer<TSurface>
    {
        var inset = (int)(_squareSize * 0.08f);
        var ringRect = SquareRect(selected).Inflate(-inset);
        renderer.DrawEllipse(ringRect, SelectionRingColor, OverlayStrokeWidth);
    }

    /// <summary>
    /// Draws a piece into an arbitrary rect. <paramref name="alpha"/> is a mask premultiplied into
    /// both glyph colours; <see cref="OpaqueAlpha"/> is exactly a no-op, so the translucent and the
    /// normal path are one call rather than two.
    /// </summary>
    private void DrawPiece<TRenderer, TSurface>(TRenderer renderer, Piece piece, RectInt rect, float fontSize,
        byte alpha = OpaqueAlpha)
        where TRenderer : Renderer<TSurface>
    {
        var whiteText = char.ToString(piece.PieceType.ToUnicode(Side.White));
        var blackText = char.ToString(piece.PieceType.ToUnicode(Side.Black));

        var fill = (piece.Side is Side.White ? FontColorWhite : FontColorBlack).WithAlpha(alpha);
        var outline = (piece.Side is Side.White ? FontColorBlack : FontColorGrey).WithAlpha(alpha);

        renderer.DrawText(blackText, _pieceFont, fontSize, fill, rect, vertAlignment: TextAlign.Center);
        renderer.DrawText(whiteText, _pieceFont, fontSize, outline, rect, vertAlignment: TextAlign.Center);
    }

    /// <summary>
    /// The square under a surface point, or null when the point is not on one. Resolved off the same
    /// arranged tree <see cref="SquareRect"/> draws from, so a pixel belongs to the square you can see
    /// under it by construction rather than by two formulas agreeing.
    ///
    /// <para>The margins are NOT the board. The arithmetic this replaced divided by the square size and
    /// C# truncates toward zero, so a point up to one square left of (or above) the board came back as
    /// column 0 / row 0: clicking the rank labels down the left edge silently selected the a-file.</para>
    /// </summary>
    public Position? FindSelected(int x, int y)
    {
        if (_layout.HitTest(x, y) is { Kind: BoardSlotKind.Square } square)
        {
            var (file, rank) = LogicalCell(square.Index % BoardLayout.Files, square.Index / BoardLayout.Files);
            return Position.FromIndex((byte)file, (byte)rank);
        }

        return default;
    }

    public PieceType FindPromotionType(int x, int y)
    {
        var box = PromotePieceTypeSelectionBox(Game.CurrentSide);
        if (box.Contains(x, y))
        {
            var transX = x - box.UpperLeft.X;
            return (PieceType)(transX / _squareSize + (int)PieceType.Knight);
        }

        return PieceType.None;
    }

    public RectInt SquareRect(Position position)
    {
        var (col, rowFromTop) = DisplayCell((int)position.File, (int)position.Rank);
        return _layout.Square(col, rowFromTop);
    }

    /// <summary>Maps a logical (file, rank) to its on-screen cell — column from the left, row from the
    /// top — applying the 180° flip when <see cref="FlipBoard"/> is set. The single mapping both draw
    /// and hit-test go through, so they can never disagree about orientation.</summary>
    private (int Col, int RowFromTop) DisplayCell(int file, int rank) =>
        FlipBoard ? (7 - file, rank) : (file, 7 - rank);

    /// <summary>Inverse of <see cref="DisplayCell"/>: an on-screen cell back to a logical (file, rank).</summary>
    private (int File, int Rank) LogicalCell(int col, int rowFromTop) =>
        FlipBoard ? (7 - col, rowFromTop) : (col, 7 - rowFromTop);

    public RectInt PromotePieceTypeSelectionBox(Side side)
    {
        // The picker sits on the promoting side's back-rank end; the flip swaps which screen end that is.
        // It is four squares wide from the a-file end of that row, so the row's first square places it.
        var promotesAtDisplayTop = (side is Side.White) ^ FlipBoard;
        var anchor = _layout.Square(0, promotesAtDisplayTop ? 0 : 7);
        var offX = anchor.UpperLeft.X;
        var offY = anchor.UpperLeft.Y;

        if (_alignment is (_, var alignY))
        {
            offY = AlignDown(offY, alignY);
        }

        return new RectInt((offX + _squareSize * 4, offY + _squareSize), (offX, offY));
    }

    public RectInt PieceTypeSelectionBox(Position position)
    {
        // Center the 7-square-wide popup on the selected square's on-screen column (honours the flip),
        // clamped to the board; place it above that square's on-screen row.
        var (col, rowFromTop) = DisplayCell((int)position.File, (int)position.Rank);
        var startFile = Math.Clamp(col - 3, 0, 1); // 7 squares wide, max start index is 1
        var anchor = _layout.Square(startFile, rowFromTop);
        var offX = anchor.UpperLeft.X;

        var squareY = anchor.UpperLeft.Y;
        var offY = squareY - _squareSize;

        // If the popup would go above the board, place it below instead
        if (offY < _layout.Board.UpperLeft.Y)
        {
            offY = squareY + _squareSize;
        }

        if (_alignment is (_, var alignY))
        {
            offY = AlignDown(offY, alignY);
        }

        return new RectInt((offX + _squareSize * 7, offY + _squareSize), (offX, offY));
    }

    /// <summary>
    /// Checks if the given pixel coordinates fall on the toggle-side button in the placement popup.
    /// </summary>
    public bool IsPlacementSideToggle(int x, int y)
    {
        if (PendingPlacement is not { } pos)
            return false;

        var box = PieceTypeSelectionBox(pos);
        // The toggle button is the 7th (last) square in the popup
        var toggleX = box.UpperLeft.X + _squareSize * 6;
        var toggleRect = new RectInt((toggleX + _squareSize, box.LowerRight.Y), (toggleX, box.UpperLeft.Y));
        return toggleRect.Contains(x, y);
    }

    public PieceType FindPlacementPieceType(int x, int y)
    {
        if (PendingPlacement is not { } pos)
            return PieceType.None;

        var box = PieceTypeSelectionBox(pos);
        if (box.Contains(x, y))
        {
            var transX = x - box.UpperLeft.X;
            var idx = transX / _squareSize;
            // First 6 squares are piece types; the 7th is the toggle-side button
            if (idx < 6)
                return (PieceType)(idx + (int)PieceType.Pawn);
        }

        return PieceType.None;
    }

    /// <summary>
    /// Returns the display rects for both captured-piece strips (the top and bottom bands), for
    /// partial-redraw clipping. Empty with <see cref="CapturedPiecesLayout.External"/> — the piles
    /// are then outside the board area entirely and the host repaints them itself.
    /// </summary>
    private (RectInt Top, RectInt Bottom) CapturedTextRects() =>
        _capturedLayout != CapturedPiecesLayout.Strips
            ? (default, default)
            : (_layout.CapturedTray(bottom: false), _layout.CapturedTray(bottom: true));

    /// <summary>
    /// Rounds <paramref name="value"/> down to the nearest multiple of <paramref name="alignment"/>.
    /// </summary>
    private static int AlignDown(int value, uint alignment) =>
        (int)((uint)value / alignment * alignment);

    /// <summary>
    /// Rounds <paramref name="value"/> up to the nearest multiple of <paramref name="alignment"/>.
    /// </summary>
    private static int AlignUp(int value, uint alignment) =>
        (int)(((uint)value + alignment - 1) / alignment * alignment);

    private static uint Lcm(uint a, uint b) => a / Gcd(a, b) * b;

    private static uint Gcd(uint a, uint b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }

    private static RGBAColor32 ComputeCapturedAreaColor(RGBAColor32 background, RGBAColor32 foreground)
    {
        const int shift = 20;

        var bgLuminance = background.Luminance;
        var fgLuminance = foreground.Luminance;

        // Dark background: lighten; bright background: darken
        var delta = bgLuminance < fgLuminance ? shift : -shift;

        return new RGBAColor32(
            (byte)Math.Clamp(background.Red + delta, 0, 255),
            (byte)Math.Clamp(background.Green + delta, 0, 255),
            (byte)Math.Clamp(background.Blue + delta, 0, 255),
            background.Alpha
        );
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPerformAction(int x, int y)
    {
        if (Mode == GameUIMode.Playback)
            return TryHistoryClick(x, y);

        if (IsSetupMode)
        {
            if (PendingPlacement is { } pendingPos)
            {
                if (IsPlacementSideToggle(x, y))
                {
                    return TogglePlacementSide();
                }
                if (FindPlacementPieceType(x, y) is { } pieceType and not PieceType.None)
                {
                    if (Game[pendingPos] is { } existing && existing.PieceType == pieceType && existing.Side == PlacementSide)
                    {
                        return ClearSquare(pendingPos);
                    }
                    else
                    {
                        return TryPlacePiece(pendingPos, pieceType, PlacementSide);
                    }
                }

                // Any OTHER board square dismisses the palette and is re-dispatched as a fresh
                // designation. The scrim spans the whole board, and this branch used to consume
                // every click that missed the seven-square strip — so while the palette was up not
                // one square on the board responded, and Escape was the only way out. Full repaint
                // (no clip rects): the scrim being retired invalidates the entire board, whatever
                // the re-dispatched action alone would have needed.
                if (FindSelected(x, y) is { } redesignated)
                {
                    var (cancelled, _) = CancelPlacement();
                    var (redispatched, _) = TrySetupAction(redesignated);
                    return (cancelled | redispatched, []);
                }
            }
            else if (FindSelected(x, y) is { } selected)
            {
                return TrySetupAction(selected);
            }
            else if (PickedUp is { } inHand && IsOverBin(x, y))
            {
                // Clicked into the bin rather than dragged into it — the same gesture reduced to two
                // taps, for the hosts where a drag is awkward (touch) or barely exists (the terminal).
                return ClearSquare(inHand);
            }

            // Off the board: fall through to the chrome, so a touch-only host's "start the game" chip
            // is reachable while setting up (the desktop presses s). Swallowing the tap here is what
            // made the chip dead on Android.
            return TryHistoryClick(x, y);
        }

        if (PendingPromotion is { } pendingPromotion)
        {
            if (Selected is { } prev && FindPromotionType(x, y) is { } promoteType and not PieceType.None)
            {
                return TryPerformAction(Action.Promote(prev, pendingPromotion, promoteType));
            }
        }
        else if (FindSelected(x, y) is { } selected)
        {
            return TryPerformAction(selected);
        }

        return TryHistoryClick(x, y);
    }

    /// <summary>
    /// Performs a select or move action for the given board position.
    /// If a piece is already selected and <paramref name="position"/> differs, attempts a move;
    /// otherwise selects the square.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPerformAction(Position position)
    {
        if (Mode == GameUIMode.Playback)
            return (UIResponse.None, []);

        // Gated before TrySelect: while waiting for the remote side a click does nothing at
        // all — no selection, no legal-move hints — matching "the board is locked".
        if (MoveLockSide is { } lockSide && Game.CurrentSide != lockSide)
            return (UIResponse.None, []);

        if (Selected is { } prev && prev != position)
        {
            return TryPerformAction(Action.DoMove(prev, position));
        }
        else if (Selected is not { })
        {
            var piece = Game.Board[position];

            if ((piece.PieceType is PieceType.None || piece.Side != Game.CurrentSide)
                && Game.TryFindValidActionToPosition(position) is { } action)
            {
                return TryPerformAction(action);
            }
        }

        return TrySelect(position);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPerformAction(Action action)
    {
        if (Mode == GameUIMode.Playback)
            return (UIResponse.None, []);

        // Also gated here (not just the Position overload): the promotion-picker paths build
        // an Action directly and would otherwise bypass the lock.
        if (MoveLockSide is { } lockSide && Game.CurrentSide != lockSide)
            return (UIResponse.None, []);

        // The full triple, not just LastMove's destination: retiring the arrow has to invalidate
        // where it started too (see the clip rects below).
        var prevArrow = LastMoveFull;

        if (action is { IsMove: true } promotion and not { Promoted: PieceType.None })
        {
            var result = Game.TryMove(promotion);

            if (result.IsPromotion())
            {
                PendingPromotion = default;
                Selected = default;

                return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
            }
            else
            {
                return (UIResponse.None, []);
            }
        }
        else if (action is { IsMove: true })
        {
            var prevStatus = Game.GameStatus;

            // The dots this move erases: collected BEFORE TryMove, because they were painted for the
            // CURRENT position's legal moves and the board is about to change under them. An engine
            // move arrives with nothing selected and painted no dots, so there is nothing to erase.
            var erasedDots = ImmutableArray.CreateBuilder<RectInt>();
            if (Selected is { } dotOrigin)
            {
                AddSelectionRects(erasedDots, dotOrigin);
            }

            var result = Game.TryMove(action);
            if (result.IsMoveOrCapture())
            {
                Selected = default;

                // Terminal states show an overlay across the entire board
                if (Game.GameStatus is GameStatus.Checkmate or GameStatus.Stalemate)
                {
                    return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
                }

                var clipRects = ImmutableArray.CreateBuilder<RectInt>(8);
                clipRects.AddRange(erasedDots);
                clipRects.Add(SquareRect(action.From));
                clipRects.Add(SquareRect(action.To));

                // Last-move arrows run centre-to-centre, so they paint over squares that neither end
                // owns; both the arrow being drawn and the one being retired need their whole span
                // invalidated. Only the retired arrow's *destination* used to be, which left its tail
                // on screen whenever the origin fell outside the repainted region — a partial-render
                // artifact only the console displays could show, since PixelGameDisplay repaints its
                // whole content rect and never consults these. Spans rather than end squares so this
                // also holds for a backend that scissors each rect instead of unioning them.
                clipRects.Add(SquareRect(action.From).Union(SquareRect(action.To)));

                if (prevArrow is (var prevFrom, var prevTo, _))
                {
                    clipRects.Add(SquareRect(prevFrom).Union(SquareRect(prevTo)));
                }

                if (result is ActionResult.Castling)
                {
                    var isKingSide = action.To.File > action.From.File;
                    var homeRank = action.From.Rank;
                    clipRects.Add(SquareRect(new Position(isKingSide ? File.H : File.A, homeRank)));
                    clipRects.Add(SquareRect(new Position(isKingSide ? File.F : File.D, homeRank)));
                }
                else if (result is ActionResult.EnPassant)
                {
                    // The taken pawn is on a different square than action.To
                    clipRects.Add(SquareRect(action.To.AdvanceInPawnDirection(Game.CurrentSide)));
                }

                if (result.IsCapture())
                {
                    var (topCaptured, bottomCaptured) = CapturedTextRects();
                    clipRects.Add(topCaptured);
                    clipRects.Add(bottomCaptured);
                }

                if (Game.GameStatus is GameStatus.Check || prevStatus is GameStatus.Check)
                {
                    if (Game.Board.KingPosition(Game.CurrentSide) is { } kingPos)
                    {
                        clipRects.Add(SquareRect(kingPos));
                    }
                }

                return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, clipRects.DrainToImmutable());
            }
            else if (result is ActionResult.NeedsPromotionType)
            {
                PendingPromotion = action.To;

                return (UIResponse.NeedsRefresh | UIResponse.NeedsPromotionType, []);
            }
        }

        return (UIResponse.None, []);
    }

    /// <summary>
    /// Every rect a selection paints: the ring on the selected square, plus a dot or capture ring on
    /// each legal destination — <see cref="DrawLegalMoveDots{TRenderer, TSurface}"/> keeps both inside
    /// the destination's own square. The dots are deterministic (this is the same ValidMoves call that
    /// paints them), so selecting, deselecting and the erase on a committed move can all repaint
    /// clipped instead of asking for the whole board.
    /// </summary>
    private void AddSelectionRects(ImmutableArray<RectInt>.Builder clipRects, Position selected)
    {
        clipRects.Add(SquareRect(selected));
        foreach (var action in DisplayBoard.ValidMoves(Game.Plies, selected, Game.CurrentSide))
        {
            clipRects.Add(SquareRect(action.To));
        }
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ClearSelection()
    {
        if (Selected is { } prev)
        {
            Selected = default;
            // The game state is unchanged, so the dots being erased are recomputable from prev.
            var clipRects = ImmutableArray.CreateBuilder<RectInt>();
            AddSelectionRects(clipRects, prev);
            return (UIResponse.NeedsRefresh, clipRects.DrainToImmutable());
        }
        return (UIResponse.None, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TrySelect(Position position)
    {
        if (!Game.IsFinished && Game.HasValidMoves(position))
        {
            var clipRects = ImmutableArray.CreateBuilder<RectInt>();
            // A reselection erases one dot set and draws another; both are known squares. Public
            // flows can't reach here with a DIFFERENT selection (a click on another own piece is an
            // attempted move), but TrySelect is public, so the erase is handled rather than assumed.
            if (Selected is { } prev && prev != position)
            {
                AddSelectionRects(clipRects, prev);
            }
            Selected = position;
            AddSelectionRects(clipRects, position);
            return (UIResponse.NeedsRefresh, clipRects.DrainToImmutable());
        }

        return (UIResponse.None, []);
    }

    /// <summary>
    /// Opens the piece-type palette anchored on <paramref name="position"/>. The board is inert
    /// while it is up (it is drawn over a full-board scrim), so this is a modal state about one
    /// square — see <see cref="TrySetupAction"/> for how a square gets here.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) SetupSelect(Position position)
    {
        AdoptPlacementSide(position);
        PendingPlacement = position;
        Selected = position;
        return (UIResponse.NeedsRefresh | UIResponse.NeedsPiecePlacement, []);
    }

    /// <summary>
    /// Adopts the colour of whatever stands on <paramref name="position"/> as the
    /// <see cref="PlacementSide"/>, so designating a black piece switches to Black without a Tab.
    /// The board already knows the answer the toggle was asking for: both palette affordances for
    /// an occupied square — the red cross that clears it, and swapping its type — are keyed on
    /// <see cref="PlacementSide"/> MATCHING the occupant, so touching a black piece while set to
    /// White offered neither, and the palette drew the wrong army over it. An empty square keeps
    /// the current side: there is nothing to adopt, and that is where the toggle still earns its
    /// place.
    /// </summary>
    private void AdoptPlacementSide(Position position)
    {
        if (Game[position] is { PieceType: not PieceType.None } piece)
        {
            PlacementSide = piece.Side;
        }
    }

    /// <summary>
    /// The one setup-mode grammar for "a square was designated", shared by the pointer and the
    /// keyboard so the two can't drift:
    /// <list type="bullet">
    /// <item>nothing in hand, occupied square — pick the piece up</item>
    /// <item>nothing in hand, empty square — open the palette there</item>
    /// <item>piece in hand, the same square again — open the palette there (change type, or clear)</item>
    /// <item>piece in hand, any other square — drop it there</item>
    /// </list>
    /// The dominant setup job is nudging an opening around from the standard board, and that used
    /// to cost four clicks and an inversion per piece (open the palette on the source, click the
    /// piece type it already holds — which CLEARS it — then open the palette on the target and
    /// place it again). Pick-up-and-drop makes it two.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TrySetupAction(Position position)
    {
        if (!IsSetupMode)
            return (UIResponse.None, []);

        if (PickedUp is { } inHand)
        {
            return inHand == position ? SetupSelect(position) : SetupRelocate(inHand, position);
        }

        return Game[position].PieceType is PieceType.None
            ? SetupSelect(position)
            : SetupPickUp(position);
    }

    /// <summary>
    /// Takes the piece on <paramref name="position"/> into hand without opening the palette, so the
    /// board stays live for a drop. Renders as the existing "picked up" tint for free —
    /// <see cref="Selected"/> already fills that square, and the legal-move dots that would
    /// accompany it in a real game are gated on <see cref="GameUIMode.Playing"/>.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) SetupPickUp(Position position)
    {
        AdoptPlacementSide(position);
        Selected = position;
        PendingPlacement = default;
        // IsUpdate, not just NeedsRefresh: the status line names the piece in hand, and console
        // displays only repaint their status bar for IsUpdate/NeedsPiecePlacement.
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    /// <summary>
    /// Moves the piece on <paramref name="from"/> to <paramref name="to"/>, REPLACING whatever
    /// stood there, of either colour. Nothing about legality is consulted — like every other setup
    /// operation this goes straight to <see cref="Chess.Lib.Game.SetPiece"/>/
    /// <see cref="Chess.Lib.Game.ClearPiece"/> and never reaches <c>Board.EvaluateAction</c>, so a
    /// knight can go to h8 and a king can stand next to a king. Replacing is deliberate: setting up
    /// a problem routinely means landing a piece on a square whose occupant should just be gone.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) SetupRelocate(Position from, Position to)
    {
        var piece = Game[from];
        if (piece.PieceType is PieceType.None)
        {
            // Nothing to move (public entry point — the internal paths only pick up occupied
            // squares). Treat it as designating the target afresh rather than silently doing nothing.
            Selected = default;
            return TrySetupAction(to);
        }

        Game.ClearPiece(from);
        Game.SetPiece(to, piece);
        Selected = default;
        PendingPlacement = default;
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPlacePiece(Position position, PieceType pieceType, Side side)
    {
        Game.SetPiece(position, new Piece(pieceType, side));
        PendingPlacement = default;
        Selected = default;
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ClearSquare(Position position)
    {
        Game.ClearPiece(position);
        PendingPlacement = default;
        Selected = default;
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) CancelPlacement()
    {
        PendingPlacement = default;
        Selected = default;
        return (UIResponse.NeedsRefresh, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ToggleKeymap()
    {
        ShowingKeymap = !ShowingKeymap;
        return (UIResponse.NeedsRefresh, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ToggleFlipBoard()
    {
        FlipBoard = !FlipBoard;
        // Whole board re-orients — a full redraw, not a clipped update.
        return (UIResponse.NeedsRefresh, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TogglePlacementSide()
    {
        PlacementSide = PlacementSide.ToOpposite();
        if (PendingPlacement is { })
        {
            return (UIResponse.NeedsRefresh, []);
        }
        return (UIResponse.IsUpdate, []);
    }

    /// <summary>
    /// Navigates backward in move history. Enters playback mode from playing if there are moves to review.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) NavigateBack(int step = 1)
    {
        var plyCount = Game.PlyCount;
        if (plyCount == 0)
            return (UIResponse.None, []);

        Selected = default;
        PendingPromotion = default;

        if (Mode == GameUIMode.Playing)
        {
            Mode = GameUIMode.Playback;
            PlaybackPlyIndex = plyCount - 1 - step;
        }
        else if (PlaybackPlyIndex > -1)
        {
            PlaybackPlyIndex -= step;
        }
        else
        {
            return (UIResponse.None, []);
        }

        // Clamp to valid range (-1 = initial board)
        PlaybackPlyIndex = Math.Max(-1, PlaybackPlyIndex);
        EnsurePlyVisible(PlaybackPlyIndex);

        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    /// <summary>
    /// Navigates forward in move history. Auto-resumes playing when reaching the latest move.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) NavigateForward(int step = 1)
    {
        if (Mode != GameUIMode.Playback)
            return (UIResponse.None, []);

        Selected = default;
        PendingPromotion = default;

        PlaybackPlyIndex += step;

        if (PlaybackPlyIndex >= Game.PlyCount)
        {
            return ExitPlayback();
        }

        EnsurePlyVisible(PlaybackPlyIndex);
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    /// <summary>
    /// Exits playback mode and returns to normal playing.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ExitPlayback()
    {
        Mode = GameUIMode.Playing;
        PlaybackPlyIndex = 0;
        Selected = default;
        PendingPromotion = default;
        HistoryScrollStart = null;
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    /// <summary>
    /// The history panel's scroll window: total move rows, the highest valid first row, and the
    /// effective first visible row (<see cref="HistoryScrollStart"/>, or pinned-to-latest when
    /// null). The one home for the `(plyCount+1)/2` + `?? Max(0, …)` math every history renderer
    /// needs — displays pass their own row capacity (it may differ from
    /// <see cref="HistoryViewportRows"/>, which tracks the ACTIVE display's capacity).
    /// </summary>
    public (int MoveCount, int MaxStart, int StartMove) HistoryWindow(int visibleRows)
    {
        var moveCount = (Game.PlyCount + 1) / 2;
        var maxStart = Math.Max(0, moveCount - visibleRows);
        return (moveCount, maxStart, HistoryScrollStart ?? maxStart);
    }

    /// <summary>
    /// Scrolls the history panel by the given number of move rows (positive = down, negative = up).
    /// Does not change the selected playback ply.
    /// </summary>
    public UIResponse ScrollHistory(int moveDelta)
    {
        var (_, maxStart, current) = HistoryWindow(HistoryViewportRows);
        var newStart = Math.Clamp(current + moveDelta, 0, maxStart);
        HistoryScrollStart = newStart >= maxStart ? null : newStart;
        return UIResponse.IsUpdate;
    }

    /// <summary>
    /// Ensures the move row containing the given ply index is visible in the history panel.
    /// </summary>
    private void EnsurePlyVisible(int plyIndex)
    {
        var moveRow = Math.Max(0, plyIndex) / 2;
        var (_, maxStart, current) = HistoryWindow(HistoryViewportRows);

        if (moveRow < current)
            HistoryScrollStart = moveRow;
        else if (moveRow >= current + HistoryViewportRows)
            HistoryScrollStart = Math.Min(moveRow - HistoryViewportRows + 1, maxStart);
        // else already visible — leave as-is
    }

    /// <summary>
    /// Enters playback mode at the given ply index.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) NavigateToPly(int plyIndex)
    {
        if (plyIndex < 0 || plyIndex >= Game.PlyCount)
            return (UIResponse.None, []);

        Selected = default;
        PendingPromotion = default;
        Mode = GameUIMode.Playback;
        PlaybackPlyIndex = plyIndex;
        EnsurePlyVisible(plyIndex);
        return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
    }

    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryHistoryClick(int x, int y)
    {
        if (ResolveHistoryClick?.Invoke(x, y) is { } plyIndex && plyIndex >= 0)
        {
            // An index at/past the ply count is the "back to latest" affordance — the touch-side
            // ExitPlayback (phones have no Esc; PixelGameDisplay binds it to a header chip).
            if (plyIndex >= Game.PlyCount)
                return Mode == GameUIMode.Playback ? ExitPlayback() : (UIResponse.None, []);
            return NavigateToPly(plyIndex);
        }
        return (UIResponse.None, []);
    }

    // ── Input Handling ───────────────────────────────────────────

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleKeyDown(InputKey key, InputModifier modifiers)
    {
        if (ShowingKeymap)
        {
            if (key is InputKey.F1 or InputKey.Escape)
            {
                PendingFile = null;
                return ToggleKeymap();
            }
            return (UIResponse.None, []);
        }

        if (key is InputKey.F1)
        {
            PendingFile = null;
            return ToggleKeymap();
        }

        if (key is InputKey.F8)
        {
            PendingFile = null;
            return (UIResponse.NeedsRestart, []);
        }

        if (key is InputKey.F9)
        {
            PendingFile = null;
            return (UIResponse.NeedsReset, []);
        }

        var isCtrl = (modifiers & InputModifier.Ctrl) != 0;

        if (isCtrl)
        {
            // Ctrl+F flips the board (bare `f` is the file-f selector). Handled before the mode
            // dispatch below so it works while playing, in playback, and in setup.
            if (key is InputKey.F)     { PendingFile = null; return ToggleFlipBoard(); }
            if (key is InputKey.Left)  { PendingFile = null; return NavigateBack(); }
            if (key is InputKey.Right) { PendingFile = null; return NavigateForward(); }
            if (key is InputKey.Up)    { PendingFile = null; return NavigateBack(2); }
            if (key is InputKey.Down)  { PendingFile = null; return NavigateForward(2); }
        }

        if (key is InputKey.PageUp)
        {
            PendingFile = null;
            return (ScrollHistory(-(HistoryViewportRows - 1)), []);
        }
        if (key is InputKey.PageDown)
        {
            PendingFile = null;
            return (ScrollHistory(HistoryViewportRows - 1), []);
        }

        if (Mode == GameUIMode.Playback)
        {
            return key switch
            {
                InputKey.Escape => ExitPlayback(),
                InputKey.Left => NavigateBack(),
                InputKey.Right => NavigateForward(),
                InputKey.Up => NavigateBack(2),
                InputKey.Down => NavigateForward(2),
                _ => (UIResponse.None, []),
            };
        }

        if (IsSetupMode)
            return HandleSetupKeyInput(key);

        if (key is InputKey.Escape)
        {
            // Progressive escape: cancel an in-progress selection / pending file first; only when
            // there's nothing left to cancel does escape unwind one more level back to the menu.
            // Mirrors the Android back button, which the host maps to Escape (SdlInputMapping).
            var hadInProgress = PendingFile is not null || Selected is not null;
            PendingFile = null;
            var (clearResponse, clearClips) = ClearSelection();
            if (!hadInProgress)
                return (UIResponse.NeedsRestart, []);
            return (clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (FileExtensions.TryParseFromKey(key) is { } file)
        {
            PendingFile = file;
            return (UIResponse.IsUpdate, []);
        }

        if (RankExtensions.TryParseFromKey(key) is { } rank)
        {
            if (PendingFile is { } pendingFile)
            {
                PendingFile = null;
                var (response, clips) = TryPerformAction(new Position(pendingFile, rank));
                return (response | UIResponse.IsUpdate, clips);
            }

            if (Selected is { } selected)
                return TryPerformAction(new Position(selected.File, rank));
        }

        if (PendingPromotion is { } pendingPromotion && Selected is { } prev)
        {
            var promoteType = PieceType.TryParseFromKey(key);

            if (promoteType is { } pt && pt.IsValidPromotion)
            {
                PendingFile = null;
                return TryPerformAction(Action.Promote(prev, pendingPromotion, pt));
            }
        }

        PendingFile = null;
        return (UIResponse.None, []);
    }

    private (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleSetupKeyInput(InputKey key)
    {
        if (key is InputKey.Tab)
        {
            PendingFile = null;
            return TogglePlacementSide();
        }

        if (key is InputKey.S)
        {
            PendingFile = null;
            IsSetupMode = false;
            return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
        }

        if (PendingPlacement is { } pendingPos)
        {
            if (key is InputKey.Escape)
            {
                PendingFile = null;
                return CancelPlacement();
            }

            if (key is InputKey.Delete or InputKey.Backspace)
            {
                PendingFile = null;
                return ClearSquare(pendingPos);
            }

            // Piece letters are parsed HERE and only here, and squares are not parsed here at all —
            // the two alphabets overlap on 'b' (file b vs. bishop). The open palette is exactly what
            // disambiguates them, which is why the keyboard cannot re-anchor to another square while
            // it is up (Escape first), and why a piece in hand takes coordinates rather than letters.
            var pieceType = PieceType.TryParseFromKey(key);
            if (pieceType is not null)
            {
                PendingFile = null;
                return TryPlacePiece(pendingPos, pieceType.Value, PlacementSide);
            }

            return (UIResponse.None, []);
        }

        if (key is InputKey.Escape)
        {
            PendingFile = null;
            var (clearResponse, clearClips) = ClearSelection();
            return (clearResponse | UIResponse.IsUpdate, clearClips);
        }

        if (key is InputKey.Backspace or InputKey.Delete)
        {
            if (Selected is { } selected)
            {
                PendingFile = null;
                return ClearSquare(selected);
            }
        }

        if (FileExtensions.TryParseFromKey(key) is { } file)
        {
            PendingFile = file;
            return (UIResponse.IsUpdate, []);
        }

        if (RankExtensions.TryParseFromKey(key) is { } rank && PendingFile is { } pf)
        {
            PendingFile = null;
            return TrySetupAction(new Position(pf, rank));
        }

        PendingFile = null;
        return (UIResponse.None, []);
    }

    /// <summary>
    /// The canonical one-line status text for the current mode — playback position, setup
    /// placement side, or game status — with the pending-file suffix. All displays derive their
    /// status bars from this (styling/prefixing per surface) instead of re-deriving the facts;
    /// the three hand-written copies this replaced had drifted in wording and hints.
    /// </summary>
    public string StatusLine(bool keyHints = true)
    {
        var fileInfo = PendingFile is { } f ? $" [{f.ToLabel()}]" : "";

        // keyHints: false on touch-only hosts (Chess.Droid) — "[Ctrl+Arrows, Esc exit]" is noise
        // there (and overflows a phone-width status bar); playback exits via the history chip.
        if (Mode == GameUIMode.Playback)
            return $"Playback: ply {PlaybackPlyIndex + 2}/{Game.PlyCount + 1}{(keyHints ? "  [Ctrl+Arrows, Esc exit]" : "")}";

        if (IsSetupMode)
        {
            if (PickedUp is { } inHand)
            {
                var piece = Game[inHand];
                return $"Setup: moving {piece.Side} {piece.PieceType} from {inHand} — pick a square"
                    + $"{(keyHints ? " [Del remove, Esc drop]" : "")}{fileInfo}";
            }

            return $"Setup: placing {PlacementSide} pieces{(keyHints ? " [Tab toggle, s start]" : "")}{fileInfo}";
        }

        return $"{Game.GameStatus.ToMessage(Game.CurrentSide)}{fileInfo}";
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleMouseDown(int x, int y)
    {
        if (ShowingKeymap)
        {
            PendingFile = null;
            return ToggleKeymap();
        }

        var hadPendingFile = PendingFile is not null;
        PendingFile = null;
        _pressedSquare = IsSetupMode ? FindSelected(x, y) : null;
        var (response, clips) = TryPerformAction(x, y);
        if (hadPendingFile) response |= UIResponse.IsUpdate;

        if (PickedUp is { } inHand)
        {
            // Where within the square it was grabbed, so the ghost holds the piece there instead of
            // centring it on the pointer. The drag point itself stays null: a ghost appears only once
            // the pointer MOVES, so a plain click never flashes one, and a stale point from an earlier
            // drag can never resurface under a fresh grab.
            var origin = SquareRect(inHand).UpperLeft;
            GrabOffset = new PointInt(x - origin.X, y - origin.Y);
            _dragPoint = null;
        }

        return (response, clips);
    }

    /// <summary>
    /// Completes a drag: a press that took a piece into hand, released over a DIFFERENT square,
    /// drops it there. Drag is deliberately not a second interaction model — the press already ran
    /// <see cref="TrySetupAction"/> and picked the piece up, so the release only has to dispatch
    /// the square it ended on, and press-and-release-in-place stays exactly the click it always
    /// was. That is what lets the terminal, which has no drag to speak of, lose nothing.
    ///
    /// <para>Three releases deliberately do nothing. One on the square the press started on is the
    /// tail of a click (dispatching again would re-open the palette on it). One where the press
    /// opened the palette instead of picking a piece up — dragging off a modal is a cancel, not a
    /// place — which <see cref="PickedUp"/> already encodes, since it is null whenever a palette is
    /// open. And one off the board entirely, which leaves the piece in hand rather than inventing a
    /// destination.</para>
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandlePointerUp(int x, int y)
    {
        var pressed = _pressedSquare;
        _pressedSquare = null;

        if (pressed is not { } from || PickedUp is not { } inHand || inHand != from)
            return (UIResponse.None, []);

        // Released in the bin: the piece is gone. Tested BEFORE the board precisely because the bin is
        // off it — the off-board no-op below is what a release anywhere else still means, and the bin is
        // now the one place off the board that means something.
        if (IsOverBin(x, y))
            return ClearSquare(inHand);

        if (FindSelected(x, y) is not { } to || to == from)
            return (UIResponse.None, []);

        return TrySetupAction(to);
    }

    /// <summary>
    /// Moves the drag ghost to a new pointer position and reports the damage: the footprint it left
    /// and the one it now occupies. The rects come from here because this is the only code that knows
    /// where the ghost WAS — a repaint needs old union new, and by the time a display renders, the old
    /// position is gone.
    ///
    /// <para>Motion is gated on a piece being in hand before any state is touched. The GPU hosts
    /// deliver pointer motion whether or not a button is down; the terminal is exempt by construction
    /// (<c>\e[?1002h</c> is button-motion tracking, not any-event tracking), but the gate belongs here
    /// where all four hosts get it rather than in one host's input mapping.</para>
    ///
    /// <para>A move that changes nothing returns <see cref="UIResponse.None"/> and no rects, which is
    /// what makes this safe to call from a raw motion stream: on the terminal every frame is a partial
    /// sixel encode, and a pointer that has not left its pixel must not cost one.</para>
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandlePointerMove(int x, int y)
    {
        var oldRect = GhostRect;

        if (PickedUp is not { } inHand)
        {
            if (oldRect is null)
                return (UIResponse.None, []);

            _dragPoint = null;
            return (UIResponse.NeedsRefresh, [oldRect.Value]);
        }

        // The ghost ALWAYS follows the pointer, wherever it goes. Hiding it off the board was made to
        // carry a meaning — "a release here does nothing" — and a piece that blinks out is a bad way to
        // say that: it reads as having dropped the thing you are still holding, and it is silent about
        // the one off-board place that DOES accept a release. The bin says that instead, by lighting up
        // (see BinIsHot), so the feedback is on the target rather than on the piece.
        var point = new PointInt(x, y);

        if (point == _dragPoint)
            return (UIResponse.None, []);

        // Crossing the bin's edge repaints the whole frame: the bin's lit state is a property of the
        // POINTER, not of the ghost, so the rects below do not describe it — and the in-board captured
        // bands are redrawn only when the clip contains their upper-left corner, which a ghost hovering
        // the middle of one does not.
        var wasHot = _dragPoint is { } previousPoint && IsOverBin(previousPoint.X, previousPoint.Y);
        var nowHot = IsOverBin(x, y);

        _dragPoint = point;

        if (wasHot != nowHot)
            return (UIResponse.NeedsRefresh, []);
        var newRect = GhostRect;

        var damage = ImmutableArray.CreateBuilder<RectInt>(3);
        if (oldRect is { } previous) damage.Add(previous);
        if (newRect is { } current) damage.Add(current);

        // The origin square is dimmed only while a ghost is showing, so it repaints when that flips —
        // and, load-bearingly, NOT on the motion events in between, which is what keeps a moving ghost
        // at two footprints rather than three.
        if ((oldRect is null) != (newRect is null))
            damage.Add(SquareRect(inHand));

        // A ghost hovering the bin has left the board, and ContentRect is the only space this class can
        // describe damage in. Reporting NO rects is the protocol's "repaint the frame", which is what
        // the GPU hosts do regardless — but the terminal honours clip rects, and a rect over the gutter
        // would have it blit a band of the BOARD to repair a smudge that is not on it.
        foreach (var rect in damage)
        {
            if (!ContentRect.Contains(rect.UpperLeft.X, rect.UpperLeft.Y)
                || !ContentRect.Contains((int)rect.LowerRight.X, (int)rect.LowerRight.Y))
            {
                return (UIResponse.NeedsRefresh, []);
            }
        }

        return (UIResponse.NeedsRefresh, damage.ToImmutable());
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) HandleMouseWheel(int delta)
        => (ScrollHistory(delta > 0 ? -3 : 3), []);
}
