using Chess.Lib.UI;

namespace Chess.UI.Windows;

public sealed class GraphicsRenderer(FontCache fontCache, Graphics surface) : Renderer<Graphics>(surface)
{
    public override int Width => (int)Surface.VisibleClipBounds.Width;
    public override int Height => (int)Surface.VisibleClipBounds.Height;

    public override void DrawRectangle(in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
    {
        using var pen = new Pen(strokeColor.ToColor(), strokeWidth);
        Surface.DrawRectangle(pen, rect.ToRectF());
    }

    public override void FillRectangle(in RectInt rect, RGBAColor32 fillColor)
    {
        using var brush = new SolidBrush(fillColor.ToColor());
        Surface.FillRectangle(brush, rect.ToRectF());
    }

    public override void FillEllipse(in RectInt rect, RGBAColor32 fillColor)
    {
        using var brush = new SolidBrush(fillColor.ToColor());
        Surface.FillEllipse(brush, rect.ToRectF());
    }

    public override void DrawText(ReadOnlySpan<char> text, string fontFileOrFamily, float fontSize, RGBAColor32 fontColor, in RectInt rect,
        TextAlign horizAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var fontFamily = fontCache.GetFontFamily(fontFileOrFamily);
        var font = fontCache.GetFont(fontFamily, fontSize, GraphicsUnit.Pixel);

        var textFormatFlags = horizAlignment.ToHorizontalFlag() | vertAlignment.ToVerticalFlag();

        TextRenderer.DrawText(Surface, text, font, rect.ToRectInt(), fontColor.ToColor(), textFormatFlags);
    }
}
