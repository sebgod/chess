namespace Chess.Lib.UI;

public readonly record struct RGBAColor8B(byte Red, byte Green, byte Blue, byte Alpha);

public abstract class GameUIBase<TSurface>
{
    private static readonly string FontFamily = "DejaVuSans.ttf";
    private static readonly RGBAColor8B FontColorBlack = new RGBAColor8B(0, 0, 0, 0xff);
    private static readonly RGBAColor8B FontColorWhite =new RGBAColor8B(0xff, 0xff, 0xff, 0xff);
    private static readonly RGBAColor8B FontColorGrey = new RGBAColor8B(0x70, 0x70, 0x70, 0xff);
    private static readonly RGBAColor8B BlackSquareFill = new RGBAColor8B(0xD1, 0x8B, 0x47, 0xff);
    private static readonly RGBAColor8B WhiteSquareFill = new RGBAColor8B(0xFF, 0xCE, 0x9E, 0xff);

    private readonly Game _game;
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

        _game = game;
        _squareSize = Math.Min(uiSizeX, uiSizeY) / SquaresNeeded;
        _margin = _squareSize / 2;

        _topMargin = (int)(_squareSize * 0.8);

        _mainFontColor = FontColorBlack;
        _labelFontSize = _squareSize * 0.25f;
        _pieceFontSize = _squareSize * 0.8f;
        _capturedFontSize = _squareSize * 0.3f;
    }

    protected abstract void DrawRectangle(TSurface surface, in RectLTRBInt rect, RGBAColor8B strokeColor, int strokeWidth);
    protected abstract void FillRectangle(TSurface surface, in RectLTRBInt rect, RGBAColor8B fillColor);

    protected abstract void DrawText(TSurface surface, string text, string fontFamily, float pointSize, RGBAColor8B fontColor, in RectLTRBInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    public int SquareSize => _squareSize;

    public void RenderUI(TSurface surface, in RectLTRBInt clip)
    {
        const int borderWidth = 2;
        var boardWidth = _squareSize * 8 + _margin;
        var borderStart = _margin - borderWidth / 2;
        var borderEnd = boardWidth + borderWidth / 2;

        // board border
        DrawRectangle(surface, new RectLTRBInt((borderEnd, borderEnd + _topMargin), (borderStart, borderStart  + _topMargin)), _mainFontColor, borderWidth);

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + _margin;

            var pos = Position.FromIndex(idx, (byte)(7 - idx));

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top = new RectLTRBInt((x_y + _squareSize, _topMargin + _margin), (x_y, _topMargin));
            var bottom = new RectLTRBInt((top.LowerRight.X, top.LowerRight.Y + boardWidth), (top.UpperLeft.X, top.UpperLeft.Y + boardWidth));

            var left = new RectLTRBInt((_margin, x_y + _topMargin + _squareSize), (0, x_y + _topMargin));
            var right = new RectLTRBInt((left.LowerRight.X + boardWidth, left.LowerRight.Y), (left.UpperLeft.X + boardWidth, left.UpperLeft.Y));

            DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, top, vertAlignment: TextAlign.Center);
            DrawText(surface, fileText, FontFamily, _labelFontSize, _mainFontColor, bottom, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, FontFamily, _labelFontSize, _mainFontColor, right, TextAlign.Center, vertAlignment: TextAlign.Center);
        }

        // captured
        var plies = _game.Plies;
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
                    var w = (int)Math.Round(_capturedFontSize * 2);
                    var h = w;
                    var layoutCount = new RectLTRBInt((pieceX + w, y + h), (pieceX, y));
                    DrawText(surface, Convert.ToString(count), FontFamily, _capturedFontSize, _mainFontColor, layoutCount);
                    pieceX += w;

                    var layoutPiece = new RectLTRBInt((pieceX + w, y + h), (pieceX, y));
                    DrawPiece(surface, new Piece((PieceType)pieceIdx, capturedSide), layoutPiece, _capturedFontSize);
                    pieceX += 2 * w;
                }
            }
        }
    }

    public void RenderBoard(TSurface surface, in RectLTRBInt clip)
    {
        // tiles
        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            var x = fileIdx * _squareSize;
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var sqY = (7 - rankIdx) * _squareSize;

                var lowerY = sqY + _margin + _topMargin;
                var rect = new RectLTRBInt(
                    (x + _margin + _squareSize, lowerY + _squareSize),
                    (x + _margin, lowerY)
                );

                FillRectangle(surface, rect, (fileIdx + rankIdx) % 2 == 0 ? BlackSquareFill : WhiteSquareFill);

                var y = rankIdx * _squareSize;

                var piece = _game[Position.FromIndex(fileIdx, rankIdx)];

                if (piece.PieceType is not PieceType.None)
                {
                    DrawPiece(surface, piece, rect, _pieceFontSize);
                }
            }
        }
    }

    private void DrawPiece(TSurface surface, Piece piece, RectLTRBInt rect, float fontSize)
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

    public (int X, int Y) SquarePos(Position position)
    {
        var x = (int)position.File * _squareSize + _margin;
        var y = (7 - (int)position.Rank) * _squareSize + _margin + _topMargin;
        return (x, y);
    }
}
