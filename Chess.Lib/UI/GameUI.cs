using System.Collections.Immutable;
using System.Drawing;

namespace Chess.Lib.UI;

public readonly record struct RGBAColor8B(byte Red, byte Green, byte Blue, byte Alpha);

public class GameUI
{
    private static readonly string FontFamily = "DejaVuSans.ttf";
    private static readonly RGBAColor8B FontColorBlack  = new RGBAColor8B(0, 0, 0, 0xff);
    private static readonly RGBAColor8B FontColorWhite  = new RGBAColor8B(0xfd, 0xfd, 0xfd, 0xff);
    private static readonly RGBAColor8B FontColorGrey   = new RGBAColor8B(0x70, 0x70, 0x70, 0xff);
    private static readonly RGBAColor8B BlackSquareFill = new RGBAColor8B(0xD1, 0x8B, 0x47, 0xff);
    private static readonly RGBAColor8B WhiteSquareFill = new RGBAColor8B(0xFF, 0xCE, 0x9E, 0xff);
    private static readonly RGBAColor8B OverlayFill = new RGBAColor8B(0xFF, 0xCE, 0x9E, 0xCC);
    private static readonly RGBAColor8B SelectedSquareFill = new RGBAColor8B(0xCD, 0x5C, 0x5C, 0xff);

    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _topMargin;
    private readonly int _boardEnd;

    private readonly float _labelFontSize;
    private readonly float _pieceFontSize;
    private readonly float _capturedFontSize;

    private readonly RGBAColor8B _mainFontColor;

    private const int SquaresNeeded = 12;
    private const int PieceTypeStride = 7;
    private const int BorderWidth = 2;

    public GameUI(Game game, int uiSizeX, int uiSizeY, Position? selected = null, Position? pendingPromotion = null)
    {
        Game = game;
        _squareSize = CalculateSquareSize(uiSizeX, uiSizeY);
        _margin = _squareSize / 2;

        _topMargin = (int)(_squareSize * 0.6);
        _boardEnd = _squareSize * 8 + _margin;

        _mainFontColor = FontColorBlack;
        _labelFontSize = _squareSize * 0.3f;
        _pieceFontSize = _squareSize * 0.8f;
        _capturedFontSize = _squareSize * 0.4f;

        Selected = selected;
        PendingPromotion = pendingPromotion;
    }

    public static int CalculateSquareSize(int uiSizeX, int uiSizeY) => Math.Min(uiSizeX, uiSizeY) / SquaresNeeded;

    public Game Game { get; }

    public Position? Selected { get; private set; }

    public Position? PendingPromotion { get; private set; }

    public int SquareSize => _squareSize;

    public void Render<TSurface, TRenderer>(TRenderer renderer, TSurface surface, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
        // board
        RenderBoard(renderer, surface, clip);

        // board border
        var borderStart = _margin - BorderWidth / 2;
        var borderEnd = _boardEnd + BorderWidth / 2;
        var borderRect = new RectInt((borderEnd, borderEnd + _topMargin), (borderStart, borderStart  + _topMargin));

        if (clip.IsContainedWithin(borderRect))
        {
            return;
        }

        renderer.DrawRectangle(surface, borderRect, _mainFontColor, BorderWidth);

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

            renderer.DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, top, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, bottom, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            renderer.DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, right, TextAlign.Center, vertAlignment: TextAlign.Center);
        }

        // active player indicator
        var currentSide = Game.CurrentSide;
        var activePlayerRect = ActivePlayerRect(currentSide);
        renderer.FillEllipse(surface, ActivePlayerRect(currentSide), _mainFontColor);
        renderer.FillEllipse(surface, activePlayerRect.Inflate(-BorderWidth), currentSide is Side.White ? FontColorWhite : FontColorBlack);

        // captured
        var plies = Game.Plies;
        var plyCount = plies.Count;

        Span<byte> capturedPieceCounts = stackalloc byte[2 * PieceTypeStride];

        for (var plyIdx = 0; plyIdx < plyCount; plyIdx++)
        {
            var ply = plies[plyIdx];
            if (ply is not { Result: ActionResult.Capture or ActionResult.CaptureAndPromotion } and { Captured: PieceType.None })
            {
                continue;
            }
            var idx = plyIdx % 2 * PieceTypeStride + (int)ply.Captured;
            capturedPieceCounts[idx]++;
        }

        DrawCapturedText(renderer, surface, capturedPieceCounts, Side.White, _margin, _topMargin + _boardEnd + _margin);
        DrawCapturedText(renderer, surface, capturedPieceCounts, Side.Black, _margin, _topMargin - _margin);

        // promote piece type selection box
        if (PendingPromotion is { })
        {
            var overlay = new RectInt((_boardEnd, _topMargin + _boardEnd), (_margin, _topMargin + _margin));
            renderer.FillRectangle(surface, overlay, OverlayFill);

            var box = PromotePieceTypeSelectionBox(currentSide);
            var offX = box.UpperLeft.X;
            var offY = box.UpperLeft.Y;

            renderer.DrawRectangle(surface, box.Inflate(BorderWidth / 2), _mainFontColor, BorderWidth);

            for (var i = 0; i < 4; i++)
            {
                var squareRect = new RectInt((offX + _squareSize * (i + 1), offY + _squareSize), (offX + _squareSize * i, offY));
                renderer.FillRectangle(surface, squareRect, i % 2 == 0 ? WhiteSquareFill : BlackSquareFill);

                DrawPiece(renderer, surface, new Piece((PieceType)(i + (int)PieceType.Knight), currentSide), squareRect, _pieceFontSize);
            }
        }
    }

    private void DrawCapturedText<TRenderer, TSurface>(TRenderer renderer, TSurface surface, ReadOnlySpan<byte> capturedPieceCounts, Side side, int x, int y)
        where TRenderer : Renderer<TSurface>
    {
        var pieceX = x;
        var capturedSide = side.ToOpposite();
        for (var pieceIdx = 1; pieceIdx < PieceTypeStride; pieceIdx++)
        {
            var count = capturedPieceCounts[((int)side - 1) * PieceTypeStride + pieceIdx];
            if (count > 0)
            {
                var w = (int)Math.Round(_capturedFontSize * 1.4);
                var h = w;
                var layoutCount = new RectInt((pieceX + w, y + h), (pieceX, y));
                renderer.DrawText(surface, Convert.ToString(count), FontFamily, _capturedFontSize, _mainFontColor, layoutCount);
                pieceX += count <= 9 ? w : 2 * w;

                var layoutPiece = new RectInt((pieceX + w, y + h), (pieceX, y));
                DrawPiece(renderer, surface, new Piece((PieceType)pieceIdx, capturedSide), layoutPiece, _capturedFontSize);
                pieceX += (int)(1.5 * w);
            }
        }
    }

    private void RenderBoard<TRenderer, TSurface>(TRenderer renderer, TSurface surface, in RectInt clip)
        where TRenderer : Renderer<TSurface>
    {
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

                var squareFill = Selected == position
                    ? SelectedSquareFill
                    : (fileIdx + rankIdx) % 2 == 0
                        ? BlackSquareFill
                        : WhiteSquareFill;

                renderer.FillRectangle(surface, rect, squareFill);

                var piece = Game[position];

                if (piece.PieceType is not PieceType.None)
                {
                    DrawPiece(renderer, surface, piece, rect, _pieceFontSize);
                }
            }
        }
    }

    private static void DrawPiece<TRenderer, TSurface>(TRenderer renderer, TSurface surface, Piece piece, RectInt rect, float fontSize)
        where TRenderer : Renderer<TSurface>
    {
        var whiteText = char.ToString(piece.PieceType.ToUnicode(Side.White));
        var blackText = char.ToString(piece.PieceType.ToUnicode(Side.Black));

        renderer.DrawText(surface, blackText, FontFamily, fontSize, piece.Side is Side.White ? FontColorWhite : FontColorBlack, rect, vertAlignment: TextAlign.Center);
        renderer.DrawText(surface, whiteText, FontFamily, fontSize, piece.Side is Side.White ? FontColorBlack : FontColorGrey,  rect, vertAlignment: TextAlign.Center);
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

    public RectInt ActivePlayerRect(Side side)
    {
        var off = _margin / 2;
        var x = _boardEnd + off;
        var y = _topMargin + (side is Side.White ? _boardEnd + off : -off);

        return new RectInt((x + _margin, y + _margin), (x, y));
    }

    public RectInt PromotePieceTypeSelectionBox(Side side)
    {
        var offX = _margin;
        var offY = side is Side.White ? _margin : _boardEnd + _topMargin - _margin / 2;

        return new RectInt((offX + _squareSize * 4, offY + _squareSize), (offX, offY));
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
        else if (action is { IsMove: true})
        {
            var result = Game.TryMove(action);
            if (result.IsMoveOrCapture())
            {
                Selected = default;

                if (result.IsCapture() || result is ActionResult.Castling)
                {
                    return (UIResponse.NeedsRefresh | UIResponse.IsUpdate, []);
                }
                else
                {
                    return (UIResponse.NeedsRefresh | UIResponse.IsUpdate,
                        [SquareRect(action.From), SquareRect(action.To), ActivePlayerRect(Side.White), ActivePlayerRect(Side.Black)]
                    );
                }
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
        if (Game.HasValidMoves(position))
        {
            Selected = position;

            return (UIResponse.NeedsRefresh, [SquareRect(position)]);
        }

        return (UIResponse.None, []);
    }
}
