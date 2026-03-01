namespace Chess.Lib.UI;

public abstract class Renderer<TSurface>
{
    public abstract void DrawRectangle(TSurface surface, in RectInt rect, RGBAColor32 strokeColor, int strokeWidth);
    public abstract void FillRectangle(TSurface surface, in RectInt rect, RGBAColor32 fillColor);
    public abstract void FillEllipse(TSurface surface, in RectInt rect, RGBAColor32 fillColor);
    public abstract void DrawText(TSurface surface, ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);

    /// <summary>
    /// Fills multiple rectangles in a single batched draw call.
    /// Default implementation falls back to individual FillRectangle calls.
    /// </summary>
    public virtual void FillRectangles(TSurface surface, ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        foreach (var (rect, color) in rectangles)
        {
            FillRectangle(surface, rect, color);
        }
    }
}
