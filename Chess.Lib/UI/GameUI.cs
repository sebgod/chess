namespace Chess.Lib.UI;

public readonly record struct RGBAColor8B(byte Red, byte Green, byte Blue, byte Alpha);

public abstract class GameUIBase<TSurface>
{
    private static readonly string FontFamily = "DejaVuSans.ttf";
    private static readonly RGBAColor8B FontColorBlack  = new RGBAColor8B(0, 0, 0, 0xff);
    private static readonly RGBAColor8B FontColorWhite  = new RGBAColor8B(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor8B FontColorGrey   = new RGBAColor8B(0x70, 0x70, 0x70, 0xff);
    private static readonly RGBAColor8B BlackSquareFill = new RGBAColor8B(0xD1, 0x8B, 0x47, 0xff);
    private static readonly RGBAColor8B WhiteSquareFill = new RGBAColor8B(0xFF, 0xCE, 0x9E, 0xff);
    private static readonly RGBAColor8B SelectedColor   = new RGBAColor8B(0xCD, 0x5C, 0x5C, 0xff);

    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _topMargin;

    private readonly float _labelFontSize;
    private readonly float _pieceFontSize;
    private readonly float _capturedFontSize;

    private readonly RGBAColor8B _mainFontColor;

    public GameUIBase(Game game, int uiSizeX, int uiSizeY)
    {
        const int SquaresNeeded = 12;

        Game = game;
        _squareSize = Math.Min(uiSizeX, uiSizeY) / SquaresNeeded;
        _margin = _squareSize / 2;

        _topMargin = (int)(_squareSize * 0.8);

        _mainFontColor = FontColorBlack;
        _labelFontSize = _squareSize * 0.25f;
        _pieceFontSize = _squareSize * 0.8f;
        _capturedFontSize = _squareSize * 0.3f;
    }

    public Game Game { get; }

    public Position? Selected { get; set; }

    protected abstract void DrawRectangle(TSurface surface, in RectInt rect, RGBAColor8B strokeColor, int strokeWidth);
    protected abstract void FillRectangle(TSurface surface, in RectInt rect, RGBAColor8B fillColor);

    protected abstract void DrawText(TSurface surface, string text, string fontFamily, float pointSize, RGBAColor8B fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    public int SquareSize => _squareSize;

    public void RenderUI(TSurface surface, in RectInt clip)
    {
        const int borderWidth = 2;
        var boardWidth = _squareSize * 8 + _margin;
        var borderStart = _margin - borderWidth / 2;
        var borderEnd = boardWidth + borderWidth / 2;

        // board border
        var borderRect = new RectInt((borderEnd, borderEnd + _topMargin), (borderStart, borderStart  + _topMargin));

        if (clip.IsContainedWithin(borderRect))
        {
            return;
        }

        DrawRectangle(surface, borderRect, _mainFontColor, borderWidth);

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + _margin;

            var pos = Position.FromIndex(idx, (byte)(7 - idx));

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top = new RectInt((x_y + _squareSize, _topMargin + _margin), (x_y, _topMargin));
            var bottom = new RectInt((top.LowerRight.X, top.LowerRight.Y + boardWidth), (top.UpperLeft.X, top.UpperLeft.Y + boardWidth));

            var left = new RectInt((_margin, x_y + _topMargin + _squareSize), (0, x_y + _topMargin));
            var right = new RectInt((left.LowerRight.X + boardWidth, left.LowerRight.Y), (left.UpperLeft.X + boardWidth, left.UpperLeft.Y));

            DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, top, vertAlignment: TextAlign.Center);
            DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, bottom, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, right, TextAlign.Center, vertAlignment: TextAlign.Center);
        }

        // captured
        var plies = Game.Plies;
        var plyCount = plies.Count;

        const int pieceTypeStride = 7;
        var capturedPieceCounts = new byte[2 * pieceTypeStride];

        for (var plyIdx = 0; plyIdx < plyCount; plyIdx++)
        {
            var ply = plies[plyIdx];
            if (ply is not { Result: ActionResult.Capture } and { CapturedOrPromoted: PieceType.None })
            {
                continue;
            }
            var idx = plyIdx % 2 * pieceTypeStride + (int)ply.CapturedOrPromoted;
            capturedPieceCounts[idx]++;
        }

        DrawCapturedText(Side.White, _margin, _topMargin + boardWidth + _margin);
        DrawCapturedText(Side.Black, _margin, _topMargin - _margin);

        void DrawCapturedText(Side side, int x, int y)
        {
            var pieceX = x;
            var capturedSide = side.ToOpposite();
            for (var pieceIdx = 1; pieceIdx < pieceTypeStride; pieceIdx++)
            {
                var count = capturedPieceCounts[((int)side - 1) * pieceTypeStride + pieceIdx];
                if (count > 0)
                {
                    var w = (int)Math.Round(_capturedFontSize * 1.4);
                    var h = w;
                    var layoutCount = new RectInt((pieceX + w, y + h), (pieceX, y));
                    DrawText(surface, Convert.ToString(count), FontFamily, _capturedFontSize, _mainFontColor, layoutCount);
                    pieceX += count <= 9 ? w : 2 * w;

                    var layoutPiece = new RectInt((pieceX + w, y + h), (pieceX, y));
                    DrawPiece(surface, new Piece((PieceType)pieceIdx, capturedSide), layoutPiece, _capturedFontSize);
                    pieceX += (int)(1.5 * w);
                }
            }
        }
    }

    public void RenderBoard(TSurface surface, in RectInt clip)
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

                FillRectangle(surface, rect, (fileIdx + rankIdx) % 2 == 0 ? BlackSquareFill : WhiteSquareFill);

                if (Selected == position)
                {
                    DrawRectangle(surface, rect, SelectedColor, 4);
                }

                var piece = Game[position];

                if (piece.PieceType is not PieceType.None)
                {
                    DrawPiece(surface, piece, rect, _pieceFontSize);
                }
            }
        }
    }

    private void DrawPiece(TSurface surface, Piece piece, RectInt rect, float fontSize)
    {
        var whiteText = char.ToString(piece.PieceType.ToUnicode(Side.White));
        var blackText = char.ToString(piece.PieceType.ToUnicode(Side.Black));

        DrawText(surface, blackText, FontFamily, fontSize, piece.Side is Side.White ? FontColorWhite : FontColorBlack, rect);
        DrawText(surface, whiteText, FontFamily, fontSize, piece.Side is Side.White ? FontColorBlack : FontColorGrey,  rect);
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

    public RectInt SquareRect(Position position)
    {
        var x = (int)position.File * _squareSize + _margin;
        var y = (7 - (int)position.Rank) * _squareSize + _margin + _topMargin;
        return new RectInt((x + _squareSize, y + _squareSize), (x, y));
    }

    public (bool NeedsRefresh, bool IsUpdate, RectInt? ClipRect) TryPerformAction(int x, int y)
    {
        if (FindSelected(x, y) is { } selected)
        {
            if (Selected is { } prev && prev != selected)
            {
                if (Game.TryMove(prev, selected) is { } result && result.IsMoveOrCapture())
                {
                    Selected = default;

                    if (result.IsCapture())
                    {
                        return (true, true, null);
                    }
                    else
                    {
                        return (true, true, SquareRect(prev).Union(SquareRect(selected)));
                    }
                }
            }
            else if (Game.HasValidMoves(selected))
            {
                Selected = selected;

                return (true, false, SquareRect(selected));
            }
        }

        return (false, false, null);
    }
}
