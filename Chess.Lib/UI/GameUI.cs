using System.Collections.Immutable;

namespace Chess.Lib.UI;

public readonly record struct RGBAColor32(byte Red, byte Green, byte Blue, byte Alpha);

public class GameUI
{
    private const string FontDejaVuSans = "Fonts/DejaVuSans.ttf";
    private const string FontMerida     = "Fonts/Merida.ttf";

    private static readonly RGBAColor32 FontColorBlack     = new RGBAColor32(0, 0, 0, 0xff);
    private static readonly RGBAColor32 FontColorWhite     = new RGBAColor32(0xfd, 0xfd, 0xfd, 0xff);
    private static readonly RGBAColor32 FontColorGrey      = new RGBAColor32(0x70, 0x70, 0x70, 0xff);
    private static readonly RGBAColor32 BlackSquareFill    = new RGBAColor32(0xD1, 0x8B, 0x47, 0xff);
    private static readonly RGBAColor32 WhiteSquareFill    = new RGBAColor32(0xFF, 0xCE, 0x9E, 0xff);
    private static readonly RGBAColor32 OverlayFill        = new RGBAColor32(0xFF, 0xCE, 0x9E, 0xCC);
    private static readonly RGBAColor32 SelectedSquareFill = new RGBAColor32(0xCD, 0x5C, 0x5C, 0xff);
    private static readonly RGBAColor32 CheckSquareFill    = new RGBAColor32(0xE9, 0xD5, 0x02, 0xff);

    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _topMargin;
    private readonly int _boardEnd;

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
        RGBAColor32? backgroundColor = null)
    {
        Game = game;
        _squareSize = CalculateSquareSize(uiSizeX, uiSizeY);
        _margin = _squareSize / 2;

        _topMargin = (int)(_squareSize * 0.5);
        _boardEnd = _squareSize * 8 + _margin;

        _mainFontColor = mainFontColor ?? FontColorBlack;
        _backgroundColor = backgroundColor ?? FontColorWhite;
        _capturedAreaColor = ComputeCapturedAreaColor(_backgroundColor, _mainFontColor);
        _labelFont = labelFont;
        _labelFontSize = _squareSize * 0.3f;
        _pieceFont = pieceFont;
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

    public int SquareSize => _squareSize;

    /// <summary>
    /// Creates a new <see cref="GameUI"/> with the given dimensions, preserving game state, selection, and style.
    /// </summary>
    public GameUI Resize(uint uiSizeX, uint uiSizeY) => new(
        Game, uiSizeX, uiSizeY,
        selected: Selected,
        pendingPromotion: PendingPromotion,
        labelFont: _labelFont,
        pieceFont: _pieceFont,
        mainFontColor: _mainFontColor,
        backgroundColor: _backgroundColor);

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

            var blackCapturedTextY = _topMargin - _margin;
            if (clip.Contains(_margin, blackCapturedTextY))
            {
                DrawCapturedText(renderer, surface, capturedPieceCounts, Side.Black, _margin, blackCapturedTextY);
            }
        }

        // promote piece type selection box
        if (PendingPromotion is { })
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
                var rect = new RectInt(
                    (x + _margin + _squareSize, lowerY + _squareSize),
                    (x + _margin, lowerY)
                );

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
            var (_, piece, rect) = piecesToDraw[i];
            DrawPiece(renderer, surface, piece, rect, _pieceFontSize);
        }
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
        var offY = side is Side.White ? _margin : _boardEnd + _topMargin - _margin / 2;

        return new RectInt((offX + _squareSize * 4, offY + _squareSize), (offX, offY));
    }

    /// <summary>
    /// Returns the display rects for both captured-piece text areas (white and black).
    /// </summary>
    private (RectInt White, RectInt Black) CapturedTextRects()
    {
        var cellSize = (int)MathF.Round(_capturedFontSize * 1.4f);
        var maxWidth = _boardEnd - _margin;

        var whiteY = _topMargin + _boardEnd + _margin;
        var blackY = _topMargin - _margin;

        return (
            new RectInt((_margin + maxWidth, whiteY + cellSize), (_margin, whiteY)),
            new RectInt((_margin + maxWidth, blackY + cellSize), (_margin, blackY))
        );
    }

    private static RGBAColor32 ComputeCapturedAreaColor(RGBAColor32 background, RGBAColor32 foreground)
    {
        const int shift = 20;

        var bgLuminance = 0.299f * background.Red + 0.587f * background.Green + 0.114f * background.Blue;
        var fgLuminance = 0.299f * foreground.Red + 0.587f * foreground.Green + 0.114f * foreground.Blue;

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
        if (PendingPromotion is { } pendingPromotion)
        {
            if (Selected is { } prev && FindPromotionType(x, y) is { } promoteType and not PieceType.None)
            {
                return TryPerformAction(Action.Promote(prev, pendingPromotion, promoteType));
            }
        }
        else if (FindSelected(x, y) is { } selected)
        {
            if (Selected is { } prev && prev != selected)
            {
                return TryPerformAction(Action.DoMove(prev, selected));
            }
            else
            {
                return TrySelect(selected);
            }
        }

        return (UIResponse.None, []);
    }

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TryPerformAction(Action action)
    {
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

                var clipRects = ImmutableArray.CreateBuilder<RectInt>(6);
                clipRects.Add(SquareRect(action.From));
                clipRects.Add(SquareRect(action.To));

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

    public (UIResponse Response, ImmutableArray<RectInt> ClipRects) TrySelect(Position position)
    {
        if (!Game.IsFinished && Game.HasValidMoves(position))
        {
            Selected = position;

            return (UIResponse.NeedsRefresh, [SquareRect(position)]);
        }

        return (UIResponse.None, []);
    }
}
