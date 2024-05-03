namespace Chess.Lib;

using ImageMagick;

public class GameUI : IDisposable
{
    const double fontXOffsetFactor = 0.15;
    const int borderWidth = 2;

    // subset with fonttools subset  DejaVuSans.ttf --text="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789♙♘♗♖♕♔♟︎♞♝♜♛♚"
    private static readonly DrawableFont Font = new DrawableFont("DejaVuSans.Chess.ttf");
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

    private readonly DrawableFontPointSize _labelFontSize;
    private readonly DrawableFontPointSize _pieceFontSize;

    private readonly MagickColor _mainFontColor;
    private readonly DrawableFillColor _mainFontFill;

    public GameUI(Game game, int size = 100, bool portrait = false)
    {
        _game = game;
        _squareSize = size;
        _margin = size / 2;
        var dim = size * 8 + _margin * 2;

        _boardImage = new MagickImage(MagickColor.FromRgb(0xff, 0xff, 0xff), dim, dim);

        _mainFontColor = FontColorBlack;
        _mainFontFill = BlackFontFill;

        _labelFontSize = new DrawableFontPointSize(_squareSize * 0.25d);
        _pieceFontSize = new DrawableFontPointSize(_squareSize * 0.8d);
    }

    public void RenderUI()
    {
        var borderStart = _margin - borderWidth;
        var borderEnd = _squareSize * 8 + _margin + borderWidth;
        var rankMargin = _margin * 0.25;
        var fileMargin = _margin * 0.15;
        var labelOffset = _margin + _squareSize * 0.35;

        // board border
        _boardImage.Draw(new DrawableRectangle(borderStart, borderStart, borderEnd, borderEnd),
            new DrawableStrokeColor(_mainFontColor), new DrawableStrokeWidth(borderWidth), new DrawableFillOpacity(new Percentage(0)));

        // labels
        for (byte idx = 0; idx < 8; idx++)
        {
            var x_y = idx * _squareSize + labelOffset;

            var pos = Position.FromIndex(idx, idx);

            var fileText = pos.File.ToLabel();
            var rankText = pos.Rank.ToLabel();

            var top = new DrawableText(x_y, fileMargin + borderEnd, fileText);
            var bottom = new DrawableText(x_y, fileMargin, fileText);

            var left = new DrawableText(rankMargin, x_y, rankText);
            var right = new DrawableText(borderEnd + rankMargin, x_y, rankText);

            _boardImage.Draw(top, bottom, left, right, Font, _labelFontSize, _mainFontFill, Origin);
        }
    }

    public void RenderBoard()
    {
        for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
        {
            var x = fileIdx * _squareSize;
            for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
            {
                var sqY = (7 - rankIdx) * _squareSize;
                _boardImage.Draw(new DrawableRectangle(x + _margin, sqY + _margin, x + _margin + _squareSize, sqY + _margin + _squareSize),
                    (fileIdx + rankIdx) % 2 == 0 ? BlackSquareFill : WhiteSquareFill);

                var y = rankIdx * _squareSize;

                var piece = _game[Position.FromIndex(fileIdx, rankIdx)];

                if (piece.PieceType is not PieceType.None)
                {
                    var pX = x + _margin + _squareSize * fontXOffsetFactor;
                    var pY = y + _margin;
                    var drawableWhite = new DrawableText(pX, pY, char.ToString(piece.PieceType.ToUnicode(Side.White)));
                    var drawableBlack = new DrawableText(pX, pY, char.ToString(piece.PieceType.ToUnicode(Side.Black)));

                    _boardImage.Draw(drawableBlack, Font, _pieceFontSize, piece.Side is Side.White ? WhiteFontFill : BlackFontFill, Origin);
                    _boardImage.Draw(drawableWhite, Font, _pieceFontSize, piece.Side is Side.White ? BlackFontFill : GreyFontFill, Origin);
                }
            }
        }
    }

    public Task SaveImage(string file, MagickFormat format = MagickFormat.Png) => _boardImage.WriteAsync(file, format);

    public void Dispose() => _boardImage.Dispose();
}
