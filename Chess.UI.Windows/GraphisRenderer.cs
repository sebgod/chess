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

    public override void DrawText(Graphics surface, string text, string fontFileOrFamily, float fontSize, RGBAColor8B fontColor, in RectInt rect,
        TextAlign horizAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var fontFamily = fontCache.GetFontFamily(fontFileOrFamily);
        var font = fontCache.GetFont(fontFamily, fontSize, GraphicsUnit.Pixel);

        var textFormatFlags = horizAlignment.ToHorizontalFlag() | vertAlignment.ToVerticalFlag();

        TextRenderer.DrawText(surface, text, font, rect.ToRectInt(), fontColor.ToColor(), textFormatFlags);
    }
}
