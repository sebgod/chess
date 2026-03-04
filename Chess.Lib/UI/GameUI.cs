using System.Collections.Immutable;

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

    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _topMargin;
    private readonly int _boardEnd;
    private readonly (uint X, uint Y)? _alignment;

    private readonly string _labelFont;
    private readonly string _pieceFont;
    private readonly float _labelFontSize;
    private readonly float _pieceFontSize;
    private readonly float _capturedFontSize;

    private readonly RGBAColor32 _mainFontColor;
    private readonly RGBAColor32 _backgroundColor;
    private readonly RGBAColor32 _capturedAreaColor;

    private const int PieceTypeStride = 7;
    private const int PortraitFlipFactor = 3;
    private const int LastMoveBorderWidth = 3;
    private const float SquaresNeededNormal = 10.5f;
    private const float SquaresNeededTight  = 11.5f;
    
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
        (uint X, uint Y)? alignment = null)
    {
        Game = game;
        _alignment = alignment;
        _squareSize = CalculateSquareSize(uiSizeX, uiSizeY);

        if (alignment is (var alignX, var alignY))
        {
            var unit = Lcm(alignX, alignY);
            _squareSize = AlignDown(_squareSize, unit);
            _margin = AlignDown(_squareSize / 2, unit);
            var capturedHeight = (int)MathF.Round(_squareSize * 0.4f * 1.4f);
            _topMargin = AlignUp(Math.Max(_squareSize / 2, capturedHeight), unit);
        }
        else
        {
            _margin = _squareSize / 2;
            var capturedHeight = (int)MathF.Round(_squareSize * 0.4f * 1.4f);
            _topMargin = Math.Max((int)(_squareSize * 0.5), capturedHeight);
        }

        _boardEnd = _squareSize * 8 + _margin;

        _mainFontColor = mainFontColor ?? FontColorBlack;
        _backgroundColor = backgroundColor ?? FontColorWhite;
        _capturedAreaColor = ComputeCapturedAreaColor(_backgroundColor, _mainFontColor);
        _labelFont = Path.Combine(AppContext.BaseDirectory, labelFont);
        _labelFontSize = _squareSize * 0.3f;
        _pieceFont = Path.Combine(AppContext.BaseDirectory, pieceFont);
        _pieceFontSize = _squareSize * 0.8f;
        _capturedFontSize = _squareSize * 0.4f;

        Selected = selected;
        PendingPromotion = pendingPromotion;
    }

    public static int CalculateSquareSize(uint uiSizeX, uint uiSizeY)
    {
        var diff = uiSizeX - uiSizeY;
        var minSize = MathF.Min(uiSizeY, uiSizeX);
        var normalSquareSize = minSize / SquaresNeededNormal;
        var tightSquareSize  = (int)(minSize / SquaresNeededTight);
        if (Math.Abs(diff) < normalSquareSize * PortraitFlipFactor)
        {
            return tightSquareSize;
        }
        else
        {
            return (int)normalSquareSize;
        }
    }

    public Game Game { get; }

    public Position? Selected { get; private set; }

    public Position? PendingPromotion { get; private set; }

    public bool IsSetupMode { get; set; }

    public Side PlacementSide { get; set; } = Side.White;

    public Position? PendingPlacement { get; private set; }

    /// <summary>
    /// The destination square of the last completed move, derived from game history.
    /// </summary>
    public (Position To, bool IsCapture)? LastMove =>
        Game.Plies is [.., var last]
            ? (last.To, last.Result.IsCapture())
            : null;

    public int SquareSize => _squareSize;

    /// <summary>
    /// Creates a new <see cref="GameUI"/> with the given dimensions, preserving game state, selection, and style.
    /// </summary>
    public GameUI Resize(uint uiSizeX, uint uiSizeY)
    {
        var resized = new GameUI(
            Game, uiSizeX, uiSizeY,
            selected: Selected,
            pendingPromotion: PendingPromotion,
            labelFont: _labelFont,
            pieceFont: _pieceFont,
            mainFontColor: _mainFontColor,
            backgroundColor: _backgroundColor,
            alignment: _alignment);
        resized.IsSetupMode = IsSetupMode;
        resized.PlacementSide = PlacementSide;
        resized.PendingPlacement = PendingPlacement;
        return resized;
    }

    public void Render<TSurface, TRenderer>(TRenderer renderer, TSurface surface, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
        // board
        RenderBoard(renderer, surface, clip);

        var boardRect = new RectInt((_boardEnd, _topMargin + _boardEnd), (_margin, _topMargin + _margin));

        // If the clip is entirely within the board area, skip chrome rendering
        if (clip.IsContainedWithin(boardRect))
        {
            return;
        }

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + _margin;

            var pos = Position.FromIndex(idx, (byte)(7 - idx));

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top = new RectInt((x_y + _squareSize, _topMargin + _margin), (x_y, _topMargin));
            var bottom = new RectInt((top.LowerRight.X, top.LowerRight.Y + _boardEnd), (top.UpperLeft.X, top.UpperLeft.Y + _boardEnd));

            var left = new RectInt((_margin, x_y + _topMargin + _squareSize), (0, x_y + _topMargin));
            var right = new RectInt((left.LowerRight.X + _boardEnd, left.LowerRight.Y), (left.UpperLeft.X + _boardEnd, left.UpperLeft.Y));

            renderer.DrawText(surface, fileText, _labelFont, _labelFontSize, _mainFontColor, top, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, fileText, _labelFont, _labelFontSize, _mainFontColor, bottom, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, rankText, _labelFont, _labelFontSize, _mainFontColor, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, rankText, _labelFont, _labelFontSize, _mainFontColor, right, TextAlign.Center, vertAlignment: TextAlign.Center);
        }

        var currentSide = Game.CurrentSide;

        // captured pieces
        var plies = Game.Plies;
        var plyCount = plies.Count;

        if (plyCount > 0)
        {
#if DEBUG
            Span<byte> capturedPieceCounts = new byte[2 * PieceTypeStride];
#else
            Span<byte> capturedPieceCounts = stackalloc byte[2 * PieceTypeStride];
#endif
            for (var plyIdx = 0; plyIdx < plyCount; plyIdx++)
            {
                var (_, ply) = plies.GetRecordAndPGNIdx(plyIdx);

                if (ply is { Result: ActionResult.Capture or ActionResult.CaptureAndPromotion } and not { Captured: PieceType.None })
                {
                    var idx = plyIdx % 2 * PieceTypeStride + (int)ply.Captured;
                    capturedPieceCounts[idx]++;
                }
            }

            var whiteCapturedTextY = _topMargin + _boardEnd + _margin;
            if (clip.Contains(_margin, whiteCapturedTextY))
            {
                DrawCapturedText(renderer, surface, capturedPieceCounts, Side.White, _margin, whiteCapturedTextY);
            }

            var capturedCellHeight = (int)MathF.Round(_capturedFontSize * 1.4f);
            var blackCapturedTextY = _topMargin - capturedCellHeight;
            if (clip.Contains(_margin, blackCapturedTextY))
            {
                DrawCapturedText(renderer, surface, capturedPieceCounts, Side.Black, _margin, blackCapturedTextY);
            }
        }

        // piece placement selection box (setup mode)
        if (PendingPlacement is { } placementPos)
        {
            renderer.FillRectangle(surface, boardRect, OverlayFill);

            var box = PieceTypeSelectionBox(placementPos);
            var offX = box.UpperLeft.X;
            var offY = box.UpperLeft.Y;

            for (var i = 0; i < 6; i++)
            {
                var squareRect = new RectInt((offX + _squareSize * (i + 1), offY + _squareSize), (offX + _squareSize * i, offY));
                renderer.FillRectangle(surface, squareRect, i % 2 == 0 ? WhiteSquareFill : BlackSquareFill);

                DrawPiece(renderer, surface, new Piece((PieceType)(i + (int)PieceType.Pawn), PlacementSide), squareRect, _pieceFontSize);
            }
        }
        // promote piece type selection box
        else if (PendingPromotion is { })
        {
            renderer.FillRectangle(surface, boardRect, OverlayFill);

            var box = PromotePieceTypeSelectionBox(currentSide);
            var offX = box.UpperLeft.X;
            var offY = box.UpperLeft.Y;

            for (var i = 0; i < 4; i++)
            {
                var squareRect = new RectInt((offX + _squareSize * (i + 1), offY + _squareSize), (offX + _squareSize * i, offY));
                renderer.FillRectangle(surface, squareRect, i % 2 == 0 ? WhiteSquareFill : BlackSquareFill);

                DrawPiece(renderer, surface, new Piece((PieceType)(i + (int)PieceType.Knight), currentSide), squareRect, _pieceFontSize);
            }
        }
        else if (Game is { GameStatus: GameStatus.Checkmate or GameStatus.Checkmate })
        {
            renderer.FillRectangle(surface, boardRect, OverlayFill);

            var message = Game.GameStatus.ToMessage(Game.IsFinished ? Game.Winner : Game.CurrentSide);
            renderer.DrawText(surface, message, _labelFont, _capturedFontSize, _mainFontColor, boardRect, vertAlignment: TextAlign.Center);
        }
    }

    private void DrawCapturedText<TRenderer, TSurface>(TRenderer renderer, TSurface surface, ReadOnlySpan<byte> capturedPieceCounts, Side side, int x, int y)
        where TRenderer : Renderer<TSurface>
    {
        // Calculate size and clear the area first
        var cellSize = (int)MathF.Round(_capturedFontSize * 1.4f);
        var maxWidth = _boardEnd - x;
        var clearRect = new RectInt((x + maxWidth, y + cellSize), (x, y));
        renderer.FillRectangle(surface, clearRect, _capturedAreaColor);

        var pieceX = x;
        var capturedSide = side.ToOpposite();
        for (var pieceIdx = 1; pieceIdx < PieceTypeStride; pieceIdx++)
        {
            var count = capturedPieceCounts[((int)side - 1) * PieceTypeStride + pieceIdx];
            if (count > 0)
            {
                var layoutCount = new RectInt((pieceX + cellSize, y + cellSize), (pieceX, y));
                renderer.DrawText(surface, Convert.ToString(count), _labelFont, _capturedFontSize, _mainFontColor, layoutCount, vertAlignment: TextAlign.Center);
                pieceX += count <= 9 ? cellSize : 2 * cellSize;

                var layoutPiece = new RectInt((pieceX + cellSize, y + cellSize), (pieceX, y));
                DrawPiece(renderer, surface, new Piece((PieceType)pieceIdx, capturedSide), layoutPiece, _capturedFontSize);
                pieceX += (int)(1.5 * cellSize);
            }
        }
    }

    private void RenderBoard<TRenderer, TSurface>(TRenderer renderer, TSurface surface, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
        // Collect squares to draw and pieces to render
        Span<(RectInt Rect, RGBAColor32 Color)> squaresToDraw = stackalloc (RectInt, RGBAColor32)[64];
        Span<(Position Position, Piece Piece, RectInt Rect)> piecesToDraw = stackalloc (Position, Piece, RectInt)[32];
        var squareCount = 0;
        var pieceCount = 0;

        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            var x = fileIdx * _squareSize;
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var sqY = (7 - rankIdx) * _squareSize;

                var lowerY = sqY + _margin + _topMargin;
                var rect = new RectInt((x + _margin + _squareSize, lowerY + _squareSize), (x + _margin, lowerY));

                if (!rect.OverlapsWith(clip))
                {
                    continue;
                }

                var position = Position.FromIndex(fileIdx, rankIdx);
                var piece = Game[position];

                RGBAColor32 squareFill;

                if (Selected == position)
                {
                    squareFill = SelectedSquareFill;
                }
                else if (piece is { PieceType: PieceType.King } && Game is { GameStatus: GameStatus.Check } && piece.Side == Game.CurrentSide)
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
        renderer.FillRectangles(surface, squaresToDraw[..squareCount]);

        // Draw pieces after squares (pieces must be on top)
        for (var i = 0; i < pieceCount; i++)
        {
            var (position, piece, rect) = piecesToDraw[i];
            DrawPiece(renderer, surface, piece, rect, _pieceFontSize);
        }

        // Draw last-move highlight border on the destination square
        if (LastMove is (var lastMoveTo, var lastMoveIsCapture))
        {
            var borderColor = lastMoveIsCapture ? SelectedSquareFill : LastMoveBorderColor;
            DrawLastMoveBorder(renderer, surface, lastMoveTo, borderColor);
        }
    }

    private void DrawLastMoveBorder<TRenderer, TSurface>(TRenderer renderer, TSurface surface, Position position, RGBAColor32 color)
        where TRenderer : Renderer<TSurface>
    {
        var inset = SquareRect(position).Inflate(-LastMoveBorderWidth);
        renderer.DrawRectangle(surface, inset, color, LastMoveBorderWidth);
    }

    private void DrawPiece<TRenderer, TSurface>(TRenderer renderer, TSurface surface, Piece piece, RectInt rect, float fontSize)
        where TRenderer : Renderer<TSurface>
    {
        var whiteText = char.ToString(piece.PieceType.ToUnicode(Side.White));
        var blackText = char.ToString(piece.PieceType.ToUnicode(Side.Black));

        renderer.DrawText(surface, blackText, _pieceFont, fontSize, piece.Side is Side.White ? FontColorWhite : FontColorBlack, rect, vertAlignment: TextAlign.Center);
        renderer.DrawText(surface, whiteText, _pieceFont, fontSize, piece.Side is Side.White ? FontColorBlack : FontColorGrey,  rect, vertAlignment: TextAlign.Center);
    }

    public Position? FindSelected(int x, int y)
    {
        var boardX = x - _margin;
        var boardY = y - _margin - _topMargin;

        var fileIdx = boardX / _squareSize;
        var rankIdx = boardY / _squareSize;

        if (fileIdx is >= 0 and < 8 && rankIdx is >= 0 and < 8)
        {
            return Position.FromIndex((byte)fileIdx, (byte)(7 - rankIdx));
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
        var x = (int)position.File * _squareSize + _margin;
        var y = (7 - (int)position.Rank) * _squareSize + _margin + _topMargin;

        return new RectInt((x + _squareSize, y + _squareSize), (x, y));
    }

    public RectInt PromotePieceTypeSelectionBox(Side side)
    {
        var offX = _margin;
        var boardTop = _topMargin + _margin;
        var offY = side is Side.White ? boardTop : boardTop + 7 * _squareSize;

        if (_alignment is (_, var alignY))
        {
            offY = AlignDown(offY, alignY);
        }

        return new RectInt((offX + _squareSize * 4, offY + _squareSize), (offX, offY));
    }

    public RectInt PieceTypeSelectionBox(Position position)
    {
        // Center the 6-square-wide popup on the selected file, clamped to the board
        var fileIdx = (int)position.File;
        var startFile = Math.Clamp(fileIdx - 2, 0, 2); // 6 squares wide, max start index is 2
        var offX = startFile * _squareSize + _margin;

        // Place above the selected square
        var squareY = (7 - (int)position.Rank) * _squareSize + _margin + _topMargin;
        var offY = squareY - _squareSize;

        // If the popup would go above the board, place it below instead
        if (offY < _topMargin + _margin)
        {
            offY = squareY + _squareSize;
        }

        if (_alignment is (_, var alignY))
        {
            offY = AlignDown(offY, alignY);
        }

        return new RectInt((offX + _squareSize * 6, offY + _squareSize), (offX, offY));
    }

    public PieceType FindPlacementPieceType(int x, int y)
    {
        if (PendingPlacement is not { } pos)
            return PieceType.None;

        var box = PieceTypeSelectionBox(pos);
        if (box.Contains(x, y))
        {
            var transX = x - box.UpperLeft.X;
            return (PieceType)(transX / _squareSize + (int)PieceType.Pawn);
        }

        return PieceType.None;
    }

    /// <summary>
    /// Returns the display rects for both captured-piece text areas (white and black).
    /// </summary>
    private (RectInt White, RectInt Black) CapturedTextRects()
    {
        var cellSize = (int)MathF.Round(_capturedFontSize * 1.4f);
        var maxWidth = _boardEnd - _margin;

        var whiteY = _topMargin + _boardEnd + _margin;
        var blackY = _topMargin - cellSize;

        return (
            new RectInt((_margin + maxWidth, whiteY + cellSize), (_margin, whiteY)),
            new RectInt((_margin + maxWidth, blackY + cellSize), (_margin, blackY))
        );
    }

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
        if (IsSetupMode)
        {
            if (PendingPlacement is { } pendingPos)
            {
                if (FindPlacementPieceType(x, y) is { } pieceType and not PieceType.None)
                {
                    return TryPlacePiece(pendingPos, pieceType, PlacementSide);
                }
            }
            else if (FindSelected(x, y) is { } selected)
            {
                return SetupSelect(selected);
            }

            return (UIResponse.None, []);
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

        return (UIResponse.None, []);
    }

    /// <summary>
    /// Performs a select or move action for the given board position.
    /// If a piece is already selected and <paramref name="position"/> differs, attempts a move;
    /// otherwise selects the square.
    /// </summary>
    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPerformAction(Position position)
    {
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
        var prevLastMove = LastMove;

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
                clipRects.Add(SquareRect(action.From));
                clipRects.Add(SquareRect(action.To));

                if (prevLastMove is (var prevMove, _))
                {
                    clipRects.Add(SquareRect(prevMove));
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
                    var (whiteCaptured, blackCaptured) = CapturedTextRects();
                    clipRects.Add(whiteCaptured);
                    clipRects.Add(blackCaptured);
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

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) ClearSelection()
    {
        if (Selected is { } prev)
        {
            Selected = default;
            return (UIResponse.NeedsRefresh, [SquareRect(prev)]);
        }
        return (UIResponse.None, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TrySelect(Position position)
    {
        if (!Game.IsFinished && Game.HasValidMoves(position))
        {
            Selected = position;

            return (UIResponse.NeedsRefresh, [SquareRect(position)]);
        }

        return (UIResponse.None, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) SetupSelect(Position position)
    {
        PendingPlacement = position;
        Selected = position;
        return (UIResponse.NeedsRefresh | UIResponse.NeedsPiecePlacement, []);
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

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TogglePlacementSide()
    {
        PlacementSide = PlacementSide.ToOpposite();
        if (PendingPlacement is { })
        {
            return (UIResponse.NeedsRefresh, []);
        }
        return (UIResponse.IsUpdate, []);
    }
}
