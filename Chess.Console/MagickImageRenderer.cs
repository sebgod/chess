using Chess.Lib;
using Chess.Lib.UI;
using ImageMagick;

namespace Chess.Console;

public class MagickImageRenderer() : Renderer<MagickImage>()
{
    public override void FillRectangle(MagickImage surface, in RectInt rect, RGBAColor8B fillColor)
        => surface.Draw(GetDrawableRect(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void FillEllipse(MagickImage surface, in RectInt rect, RGBAColor8B fillColor)
        => surface.Draw(GetDrawableEllipse(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void DrawRectangle(MagickImage surface, in RectInt rect, RGBAColor8B strokeColor, int strokeWidth)
        => surface.Draw(GetDrawableRect(rect), new DrawableStrokeColor(GetColor(strokeColor)), new DrawableStrokeWidth(strokeWidth), new DrawableFillOpacity(new Percentage(0)));

    public static MagickColor GetColor(RGBAColor8B fillColor) => MagickColor.FromRgba(fillColor.Red, fillColor.Green, fillColor.Blue, fillColor.Alpha);

    private static DrawableRectangle GetDrawableRect(in RectInt rect)
        => new DrawableRectangle(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);

    private static DrawableEllipse GetDrawableEllipse(in RectInt rect)
    {
        int x = rect.UpperLeft.X;
        int y = rect.UpperLeft.Y;
        var rX = (rect.LowerRight.X - x) * 0.5;
        var rY = (rect.LowerRight.Y - y) * 0.5;

        return new DrawableEllipse(x + rX, y + rY, rX, rY, 0, 360);
    }

    public override void DrawText(MagickImage surface, string text, string fontFamily, float pointSize, RGBAColor8B fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        int x = layout.UpperLeft.X;
        int y = layout.UpperLeft.Y;
        int w = layout.LowerRight.X - x;
        int h = layout.LowerRight.Y - y;

        var readSettings = new MagickReadSettings
        {
            Font = fontFamily,
            Width = w,
            Height = h,
            TextGravity = GetGravity(horizAlignment, vertAlignment),
            FontPointsize = pointSize,
            BackgroundColor = new MagickColor(0, 0, 0, 0),
            FillColor =  GetColor(fontColor)
        };
        using var overlayImage = new MagickImage("caption:" + text, readSettings);
        surface.Composite(overlayImage, Gravity.Northwest, x, y, CompositeOperator.Atop);
    }

    private static Gravity GetGravity(TextAlign horizAlignment, TextAlign vertAlignment)
    { 
        if (horizAlignment is TextAlign.Center && vertAlignment is TextAlign.Center)
        {
            return Gravity.Center;
        }
        else if (horizAlignment is TextAlign.Center && vertAlignment is TextAlign.Near)
        {
            return Gravity.South;
        }
        else if (horizAlignment is TextAlign.Center && vertAlignment is TextAlign.Far)
        {
            return Gravity.North;
        }
        else if (horizAlignment is TextAlign.Far && vertAlignment is TextAlign.Center)
        {
            return Gravity.East;
        }
        else if (horizAlignment is TextAlign.Far && vertAlignment is TextAlign.Far)
        {
            return Gravity.Northeast;
        }
        else if (horizAlignment is TextAlign.Far && vertAlignment is TextAlign.Near)
        {
            return Gravity.Southeast;
        }
        else if (horizAlignment is TextAlign.Near && vertAlignment is TextAlign.Center)
        {
            return Gravity.West;
        }
        else if (horizAlignment is TextAlign.Near && vertAlignment is TextAlign.Far)
        {
            return Gravity.Northwest;
        }
        else if (horizAlignment is TextAlign.Near && vertAlignment is TextAlign.Near)
        {
            return Gravity.Southwest;
        }

        return Gravity.Undefined;
    }
}
