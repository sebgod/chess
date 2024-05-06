using ImageMagick;

namespace Chess.Lib.UI;

public class ImageGameUI(Game game, int uiSizeX, int uiSizeY) : GameUIBase<MagickImage>(game, uiSizeX, uiSizeY)
{
    protected override void DrawRectangle(MagickImage surface, DrawableRectangle rect, DrawableStrokeColor strokeColor, DrawableStrokeWidth strokeWidth)
        => surface.Draw(rect, strokeColor, strokeWidth, new DrawableFillOpacity(new Percentage(0)));

    protected override void FillRectangle(MagickImage surface, DrawableRectangle rect, DrawableFillColor fillColor)
        => surface.Draw(rect, fillColor, new DrawableFillOpacity(new Percentage(100)));

    protected override void DrawText(MagickImage surface, string text, DrawableFont font, DrawableFontPointSize pointSize, DrawableFillColor fontColor, DrawableRectangle rect,
        TextAlign lineAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var drawableText = new DrawableText(rect.LowerRightX, rect.LowerRightY, text);
        var gravity = new DrawableGravity(Gravity.Southwest);
        surface.Draw(drawableText, font, pointSize, fontColor, gravity);
    }
}
