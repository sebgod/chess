using Chess.Lib.UI;
using ImageMagick;
using ImageMagick.Drawing;

namespace Chess.Console;

public class MagickImageRenderer() : Renderer<MagickImage>()
{
    public override void FillRectangle(MagickImage surface, in RectInt rect, RGBAColor32 fillColor)
        => surface.Draw(GetDrawableRect(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void FillEllipse(MagickImage surface, in RectInt rect, RGBAColor32 fillColor)
        => surface.Draw(GetDrawableEllipse(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void DrawRectangle(MagickImage surface, in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
        => surface.Draw(GetDrawableRect(rect), new DrawableStrokeColor(GetColor(strokeColor)), new DrawableStrokeWidth(strokeWidth), new DrawableFillOpacity(new Percentage(0)));

    public static MagickColor GetColor(RGBAColor32 fillColor) => MagickColor.FromRgba(fillColor.Red, fillColor.Green, fillColor.Blue, fillColor.Alpha);

    private static DrawableRectangle GetDrawableRect(in RectInt rect)
        => new DrawableRectangle(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);

    private static DrawableEllipse GetDrawableEllipse(in RectInt rect)
    {
        var x = rect.UpperLeft.X;
        var y = rect.UpperLeft.Y;
        var rX = (rect.LowerRight.X - x) * 0.5;
        var rY = (rect.LowerRight.Y - y) * 0.5;

        return new DrawableEllipse(x + rX, y + rY, rX, rY, 0, 360);
    }

    public override void DrawText(MagickImage surface, ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        var x = layout.UpperLeft.X;
        var y = layout.UpperLeft.Y;
        var w = layout.LowerRight.X - x;
        var h = layout.LowerRight.Y - y;

        var origDensity = surface.Density.ChangeUnits(DensityUnit.PixelsPerInch);
        var density = origDensity.X == 0 || origDensity.Y == 0 ? new Density(72, DensityUnit.PixelsPerInch) : origDensity;

        var factor = density.Y / 72f;

        var readSettings = new MagickReadSettings
        {
            Font = fontFamily,
            Width = (uint)w,
            Height = (uint)h,
            TextGravity = GetGravity(horizAlignment, vertAlignment),
            FontPointsize = fontSize / factor, 
            BackgroundColor = new MagickColor(0, 0, 0, 0),
            FillColor =  GetColor(fontColor),
            Density = density,
        };
        using var overlayImage = new MagickImage(string.Concat("caption:", text), readSettings);
        surface.Composite(overlayImage, Gravity.Northwest, (int)x, (int)y, CompositeOperator.Atop);
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
