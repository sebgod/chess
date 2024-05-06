using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

namespace Chess.UI.Windows;

public class GraphicsGameUI(FontCache fontCache, Game game, int uiSizeX, int uiSizeY) : GameUIBase<Graphics>(game, uiSizeX, uiSizeY)
{
    protected override void DrawRectangle(Graphics surface, DrawableRectangle rect, DrawableStrokeColor strokeColor, DrawableStrokeWidth strokeWidth)
    {
        using var pen = new Pen(strokeColor.Color.ToColor(), (float)strokeWidth.Width);
        GetRectF(rect, out var rectF);
        surface.DrawRectangle(pen, rectF);
    }

    protected override void FillRectangle(Graphics surface, DrawableRectangle rect, DrawableFillColor fillColor)
    {
        using var brush = new SolidBrush(fillColor.Color.ToColor());
        GetRectF(rect, out var rectF);
        surface.FillRectangle(brush, rectF);
    }

    protected override void DrawText(Graphics surface, string text, DrawableFont font, DrawableFontPointSize pointSize, DrawableFillColor fontColor, DrawableRectangle rect,
        TextAlign horizAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var fontFamily = fontCache.GetFontFamily(font.Family);

        using var gdiFont = new Font(fontFamily, (float)pointSize.PointSize, GraphicsUnit.Point);
        using var brush = new SolidBrush(fontColor.Color.ToColor());
        using var format = new StringFormat
        {
            Alignment = ToStringAlignment(horizAlignment),
            LineAlignment = ToStringAlignment(vertAlignment),
            FormatFlags = StringFormatFlags.NoFontFallback | StringFormatFlags.NoClip
        };
        GetRectF(rect, out var rectF);

        surface.DrawString(text, gdiFont, brush, rectF, format);
    }

    private static StringAlignment ToStringAlignment(TextAlign lineAlignment) => lineAlignment switch
    {
        TextAlign.Center => StringAlignment.Center,
        TextAlign.Near => StringAlignment.Near,
        TextAlign.Far => StringAlignment.Far,
        _ => throw new ArgumentException($"Unknown line alignment {lineAlignment}", nameof(lineAlignment))
    };

    private static void GetRectF(DrawableRectangle rect, out RectangleF rectF)
    {
        var x = (float)Math.Min(rect.UpperLeftX, rect.LowerRightX);
        var y = (float)Math.Min(rect.UpperLeftY, rect.LowerRightY);
        var w = (float)Math.Max(rect.UpperLeftX, rect.LowerRightX) - x;
        var h = (float)Math.Max(rect.UpperLeftY, rect.LowerRightY) - y;
        rectF = new RectangleF(x, y, w, h);
    }
}
