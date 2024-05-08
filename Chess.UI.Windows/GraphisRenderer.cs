using Chess.Lib;
using Chess.Lib.UI;

namespace Chess.UI.Windows;

public sealed class GraphisRenderer(FontCache fontCache) : Renderer<Graphics>
{
    public override void DrawRectangle(Graphics surface, in RectInt rect, RGBAColor8B strokeColor, int strokeWidth)
    {
        using var pen = new Pen(strokeColor.ToColor(), strokeWidth);
        surface.DrawRectangle(pen, rect.ToRectF());
    }

    public override void FillRectangle(Graphics surface, in RectInt rect, RGBAColor8B fillColor)
    {
        using var brush = new SolidBrush(fillColor.ToColor());
        surface.FillRectangle(brush, rect.ToRectF());
    }

    public override void FillEllipse(Graphics surface, in RectInt rect, RGBAColor8B fillColor)
    {
        using var brush = new SolidBrush(fillColor.ToColor());
        surface.FillEllipse(brush, rect.ToRectF());
    }

    public override void DrawText(Graphics surface, string text, string fontFileOrFamily, float pointSize, RGBAColor8B fontColor, in RectInt rect,
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
