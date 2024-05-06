using Chess.Lib;
using Chess.Lib.UI;

namespace Chess.UI.Windows;

public class GraphicsGameUI(FontCache fontCache, Game game, int uiSizeX, int uiSizeY) : GameUIBase<Graphics>(game, uiSizeX, uiSizeY)
{
    protected override void DrawRectangle(Graphics surface, in RectInt rect, RGBAColor8B strokeColor, int strokeWidth)
    {
        using var pen = new Pen(strokeColor.ToColor(), strokeWidth);
        surface.DrawRectangle(pen, rect.ToRectF());
    }

    protected override void FillRectangle(Graphics surface, in RectInt rect, RGBAColor8B fillColor)
    {
        using var brush = new SolidBrush(fillColor.ToColor());
        surface.FillRectangle(brush, rect.ToRectF());
    }

    protected override void DrawText(Graphics surface, string text, string fontFileOrFamily, float pointSize, RGBAColor8B fontColor, in RectInt rect,
        TextAlign horizAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var fontFamily = fontCache.GetFontFamily(fontFileOrFamily);

        using var gdiFont = new Font(fontFamily, (float)pointSize, GraphicsUnit.Point);
        using var brush = new SolidBrush(fontColor.ToColor());
        using var format = new StringFormat
        {
            Alignment = horizAlignment.ToStringAlignment(),
            LineAlignment = vertAlignment.ToStringAlignment(),
            FormatFlags = StringFormatFlags.NoFontFallback | StringFormatFlags.NoClip
        };
        
        surface.DrawString(text, gdiFont, brush, rect.ToRectF(), format);
    }
}
