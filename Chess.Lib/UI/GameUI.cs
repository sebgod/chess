namespace Chess.Lib.UI;

using ImageMagick;

public abstract class GameUIBase<TSurface>
{
    private static readonly DrawableFont Font = new DrawableFont("DejaVuSans.ttf");
    private static readonly MagickColor FontColorBlack = MagickColor.FromRgb(0, 0, 0);
    private static readonly MagickColor FontColorWhite = MagickColor.FromRgb(0xff, 0xff, 0xff);
    private static readonly MagickColor FontColorGrey = MagickColor.FromRgb(0x70, 0x70, 0x70);

    private static readonly DrawableFillColor BlackFontFill = new DrawableFillColor(FontColorBlack);
    private static readonly DrawableFillColor WhiteFontFill = new DrawableFillColor(FontColorWhite);
    private static readonly DrawableFillColor GreyFontFill = new DrawableFillColor(FontColorGrey);

    private static readonly DrawableFillColor BlackSquareFill = new DrawableFillColor(MagickColor.FromRgb(0xD1, 0x8B, 0x47));
    private static readonly DrawableFillColor WhiteSquareFill = new DrawableFillColor(MagickColor.FromRgb(0xFF, 0xCE, 0x9E));

    private readonly Game _game;
    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _topMargin;

    private readonly DrawableFontPointSize _labelFontSize;
    private readonly DrawableFontPointSize _pieceFontSize;
    private readonly DrawableFontPointSize _capturedFontSize;

    private readonly MagickColor _backgroundColor;
    private readonly MagickColor _mainFontColor;
    private readonly DrawableFillColor _mainFontFill;

    public GameUIBase(Game game, int uiSizeX, int uiSizeY)
    {
        const int SquaresNeeded = 12;

        _game = game;
        _squareSize = Math.Min(uiSizeX, uiSizeY) / SquaresNeeded;
        _margin = _squareSize / 2;

        _topMargin = (int)(_squareSize * 0.8);

        _backgroundColor = MagickColor.FromRgb(0xBC, 0xBC, 0xBC);

        _mainFontColor = FontColorBlack;
        _mainFontFill = BlackFontFill;

        _labelFontSize = new DrawableFontPointSize(_squareSize * 0.25d);
        _pieceFontSize = new DrawableFontPointSize(_squareSize * 0.8d);
        _capturedFontSize = new DrawableFontPointSize(_squareSize * 0.3d);
    }

    protected abstract void DrawRectangle(TSurface surface, DrawableRectangle rect, DrawableStrokeColor strokeColor, DrawableStrokeWidth strokeWidth);
    protected abstract void FillRectangle(TSurface surface, DrawableRectangle rect, DrawableFillColor fillColor);

    protected abstract void DrawText(TSurface surface, string text, DrawableFont font, DrawableFontPointSize pointSize, DrawableFillColor fontColor, DrawableRectangle layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    public int SquareSize => _squareSize;

    public MagickColor BackgroundColor => _backgroundColor;

    public void RenderUI(TSurface surface, RectInt clip)
    {
        const int borderWidth = 2;
        var boardWidth = _squareSize * 8 + _margin;
        var borderStart = _margin - borderWidth / 2;
        var borderEnd = boardWidth + borderWidth / 2;

        // board border
        DrawRectangle(surface,
            new DrawableRectangle(borderStart, borderStart + _topMargin, borderEnd, borderEnd + _topMargin),
            new DrawableStrokeColor(_mainFontColor), new DrawableStrokeWidth(borderWidth)
        );

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + _margin;

            var pos = Position.FromIndex(idx, (byte)(7 - idx));

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top = new DrawableRectangle(x_y, _topMargin, lowerRightX: x_y + _squareSize, _topMargin + _margin);
            var bottom = new DrawableRectangle(top.UpperLeftX, top.UpperLeftY + boardWidth, top.LowerRightX, top.LowerRightY + boardWidth);

            var left = new DrawableRectangle(0, x_y + _topMargin, _margin, x_y + _topMargin + _squareSize);
            var right = new DrawableRectangle(left.UpperLeftX + boardWidth, left.UpperLeftY, left.LowerRightX + boardWidth, lowerRightY: left.LowerRightY);

            DrawText(surface, fileText, Font, _labelFontSize, _mainFontFill, top, vertAlignment: TextAlign.Center);
            DrawText(surface, fileText, Font, _labelFontSize, _mainFontFill, bottom, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, Font, _labelFontSize, _mainFontFill, left, TextAlign.Center, vertAlignment: TextAlign.Center);
            DrawText(surface, rankText, Font, _labelFontSize, _mainFontFill, right, TextAlign.Center, vertAlignment: TextAlign.Center);
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

        void DrawCapturedText(Side side, double x, double y)
        {
            var pieceX = x;
            var capturedSide = side.ToOpposite();
            for (var pieceIdx = 1; pieceIdx < pieceTypeStride; pieceIdx++)
            {
                var count = capturedPieceCounts[((int)side - 1) * pieceTypeStride + pieceIdx];
                if (count > 0)
                {
                    var w = _capturedFontSize.PointSize * 2;
                    var h = w;
                    var layoutCount = new DrawableRectangle(pieceX, y, x + w, y + h);
                    DrawText(surface, Convert.ToString(count), Font, _capturedFontSize, _mainFontFill, layoutCount);
                    pieceX += w * 1.5;

                    var layoutPiece = new DrawableRectangle(pieceX, y, x + w, y + h);
                    DrawPiece(surface, new Piece((PieceType)pieceIdx, capturedSide), layoutPiece, _capturedFontSize);
                    pieceX += w;
                }
            }
        }
    }

    public void RenderBoard(TSurface surface, RectInt clip)
    {
        // tiles
        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            var x = fileIdx * _squareSize;
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var sqY = (7 - rankIdx) * _squareSize;

                var rect = new DrawableRectangle(
                    x + _margin, sqY + _margin + _topMargin,
                    x + _margin + _squareSize, sqY + _margin + _squareSize + _topMargin
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

    private void DrawPiece(TSurface surface, Piece piece, DrawableRectangle rect, DrawableFontPointSize fontSize)
    {
        var whiteText = char.ToString(piece.PieceType.ToUnicode(Side.White));
        var blackText = char.ToString(piece.PieceType.ToUnicode(Side.Black));

        DrawText(surface, blackText, Font, fontSize, piece.Side is Side.White ? WhiteFontFill : BlackFontFill, rect);
        DrawText(surface, whiteText, Font, fontSize, piece.Side is Side.White ? BlackFontFill : GreyFontFill, rect);
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
