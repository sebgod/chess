namespace Chess.Lib;

using ImageMagick;

public class GameUI : IDisposable
{
    const double fontXOffsetFactor = 0.15;
    const int borderWidth = 2;

    private static readonly DrawableFont Font = new DrawableFont("DejaVuSans.ttf");
    private static readonly MagickColor FontColorBlack = MagickColor.FromRgb(0, 0, 0);
    private static readonly MagickColor FontColorWhite = MagickColor.FromRgb(0xff, 0xff, 0xff);
    private static readonly MagickColor FontColorGrey = MagickColor.FromRgb(0x70, 0x70, 0x70);

    private static readonly DrawableFillColor BlackFontFill = new DrawableFillColor(FontColorBlack);
    private static readonly DrawableFillColor WhiteFontFill = new DrawableFillColor(FontColorWhite);
    private static readonly DrawableFillColor GreyFontFill = new DrawableFillColor(FontColorGrey);

    private static readonly DrawableFillColor BlackSquareFill = new DrawableFillColor(MagickColor.FromRgb(0xD1, 0x8B, 0x47));
    private static readonly DrawableFillColor WhiteSquareFill = new DrawableFillColor(MagickColor.FromRgb(0xFF, 0xCE, 0x9E));

    private static readonly DrawableGravity Origin = new DrawableGravity(Gravity.Southwest);

    private readonly Game _game;
    private readonly MagickImage _boardImage;
    private readonly int _margin;
    private readonly int _squareSize;
    private readonly int _totalSizeX;
    private readonly int _totalSizeY;
    private readonly int _extraSizeY;
    private readonly int _topMargin;

    private readonly DrawableFontPointSize _labelFontSize;
    private readonly DrawableFontPointSize _pieceFontSize;
    private readonly DrawableFontPointSize _capturedFontSize;

    private readonly MagickColor _backgroundColor;
    private readonly MagickColor _mainFontColor;
    private readonly DrawableFillColor _mainFontFill;

    public GameUI(Game game, int uiSizeX, int uiSizeY)
    {
        _game = game;
        _squareSize = (int)(Math.Min(uiSizeX, uiSizeY) / 10);
        _margin = _squareSize / 2;
        var dim = _squareSize * 8 + _margin * 2;

        _totalSizeX = uiSizeX;
        _totalSizeY = uiSizeY;
        _extraSizeY = _totalSizeY - dim;
        _topMargin = (int)(_extraSizeY * 0.35);

        _backgroundColor = MagickColor.FromRgb(0xBC, 0xBC, 0xBC);
        _boardImage = new MagickImage(_backgroundColor, _totalSizeX, _totalSizeY);

        _mainFontColor = FontColorBlack;
        _mainFontFill = BlackFontFill;

        _labelFontSize = new DrawableFontPointSize(_squareSize * 0.25d);
        _pieceFontSize = new DrawableFontPointSize(_squareSize * 0.8d);
        _capturedFontSize = new DrawableFontPointSize(_squareSize * 0.3d);
    }

    public int SquareSize => _squareSize;

    public MagickColor BackgroundColor => _backgroundColor;

    public void RenderUI()
    {
        var borderStart = _margin - borderWidth;
        var borderEnd = _squareSize * 8 + _margin + borderWidth;
        var rankMargin = _margin * 0.25;
        var fileMargin = _margin * 0.15;
        var labelOffset = _margin + _squareSize * 0.35;

        // board border
        _boardImage.Draw(new DrawableRectangle(borderStart, borderStart + _topMargin, borderEnd, borderEnd + _topMargin),
            new DrawableStrokeColor(_mainFontColor), new DrawableStrokeWidth(borderWidth), new DrawableFillOpacity(new Percentage(0)));

        // labels
        var yOff = _extraSizeY - _topMargin;
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + labelOffset;

            var pos = Position.FromIndex(idx, idx);

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top    = new DrawableText(x_y, fileMargin + borderEnd + yOff, fileText);
            var bottom = new DrawableText(x_y, fileMargin + yOff,             fileText);

            var left   = new DrawableText(rankMargin,             x_y + yOff, rankText);
            var right  = new DrawableText(borderEnd + rankMargin, x_y + yOff, rankText);

            _boardImage.Draw(top, bottom, left, right, Font, _labelFontSize, _mainFontFill, Origin);
        }
    }

    public void RenderBoard()
    {
        // tiles
        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            var x = fileIdx * _squareSize;
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var sqY = (7 - rankIdx) * _squareSize;
                _boardImage.Draw(
                    new DrawableRectangle(
                        x + _margin, sqY + _margin + _topMargin,
                        x + _margin + _squareSize, sqY + _margin + _squareSize + _topMargin
                    ),
                    (fileIdx + rankIdx) % 2 == 0 ? BlackSquareFill : WhiteSquareFill
                );

                var y = rankIdx * _squareSize;

                var piece = _game[Position.FromIndex(fileIdx, rankIdx)];

                if (piece.PieceType is not PieceType.None)
                {
                    var pX = x + _margin + _squareSize * fontXOffsetFactor;
                    var pY = y + _margin + _extraSizeY - _topMargin;

                    DrawPiece(piece, pX, pY, _pieceFontSize);
                }
            }
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
            var idx = (plyIdx % 2) * pieceTypeStride + (int)ply.CapturedOrPromoted;
            capturedPieceCounts[idx]++;
        }

        DrawCapturedText(Side.White, _margin, _extraSizeY - _topMargin - _margin * 0.7);
        DrawCapturedText(Side.Black, _margin, _extraSizeY - _topMargin + _squareSize * 8 + _margin * 2);

        void DrawCapturedText(Side side, double x, double y)
        {
            var pieceX = x;
            var capturedSide = side.ToOpposite();
            for (var pieceIdx = 1; pieceIdx < pieceTypeStride; pieceIdx++)
            {
                var count = capturedPieceCounts[((int)side - 1) * pieceTypeStride + pieceIdx];
                if (count > 0)
                {
                    _boardImage.Draw(new DrawableText(pieceX, y, Convert.ToString(count)), _capturedFontSize, Origin);
                    pieceX += _capturedFontSize.PointSize * 0.6;

                    DrawPiece(new Piece((PieceType)pieceIdx, capturedSide), pieceX, y, _capturedFontSize);
                    pieceX += _capturedFontSize.PointSize * 1.2;
                }
            }
        }
    }

    private void DrawPiece(Piece piece, double x, double y, DrawableFontPointSize fontSize)
    {
        var drawableWhite = new DrawableText(x, y, char.ToString(piece.PieceType.ToUnicode(Side.White)));
        var drawableBlack = new DrawableText(x, y, char.ToString(piece.PieceType.ToUnicode(Side.Black)));

        _boardImage.Draw(drawableBlack, Font, fontSize, piece.Side is Side.White ? WhiteFontFill : BlackFontFill, Origin);
        _boardImage.Draw(drawableWhite, Font, fontSize, piece.Side is Side.White ? BlackFontFill : GreyFontFill, Origin);
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

    public MagickImage Image => _boardImage;

    public void Dispose()
    {
        _boardImage.Dispose();
        GC.SuppressFinalize(this);
    }
}
