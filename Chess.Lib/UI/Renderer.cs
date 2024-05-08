namespace Chess.Lib.UI;

public abstract class Renderer<TSurface>
{
    public abstract void DrawRectangle(TSurface surface, in RectInt rect, RGBAColor8B strokeColor, int strokeWidth);
    public abstract void FillRectangle(TSurface surface, in RectInt rect, RGBAColor8B fillColor);
    public abstract void FillEllipse(TSurface surface, in RectInt rect, RGBAColor8B fillColor);
    public abstract void DrawText(TSurface surface, string text, string fontFamily, float fontSize, RGBAColor8B fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near);
}
