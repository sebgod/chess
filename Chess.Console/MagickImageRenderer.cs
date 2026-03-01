using Chess.Lib.UI;
using ImageMagick;
using ImageMagick.Drawing;

namespace Chess.Console;

public class MagickImageRenderer() : Renderer<MagickImage>(), IDisposable
{
    private readonly Dictionary<CaptionCacheKey, MagickImage> _captionCache = [];
    private Density? _cachedDensity;
    private double _cachedFactor;

    public override void FillRectangle(MagickImage surface, in RectInt rect, RGBAColor32 fillColor)
        => surface.Draw(GetDrawableRect(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void FillEllipse(MagickImage surface, in RectInt rect, RGBAColor32 fillColor)
        => surface.Draw(GetDrawableEllipse(rect), new DrawableFillColor(GetColor(fillColor)), new DrawableFillOpacity(new Percentage(100)));

    public override void DrawRectangle(MagickImage surface, in RectInt rect, RGBAColor32 strokeColor, int strokeWidth)
        => surface.Draw(GetDrawableRect(rect), new DrawableFillColor(MagickColors.Transparent), new DrawableStrokeColor(GetColor(strokeColor)), new DrawableStrokeWidth(strokeWidth));

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

    /// <summary>
    /// Fills multiple rectangles in a single batched Draw call, reducing P/Invoke overhead.
    /// Uses Drawables collection for efficient batching.
    /// </summary>
    public override void FillRectangles(MagickImage surface, ReadOnlySpan<(RectInt Rect, RGBAColor32 Color)> rectangles)
    {
        if (rectangles.IsEmpty)
        {
            return;
        }

        var drawables = new Drawables();

        foreach (var (rect, color) in rectangles)
        {
            drawables
                .FillColor(GetColor(color))
                .FillOpacity(new Percentage(100))
                .Rectangle(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);
        }

        surface.Draw(drawables);
    }

    public override void DrawText(MagickImage surface, ReadOnlySpan<char> text, string fontFamily, float fontSize, RGBAColor32 fontColor, in RectInt layout,
        TextAlign horizAlignment = TextAlign.Center, TextAlign vertAlignment = TextAlign.Near)
    {
        var x = layout.UpperLeft.X;
        var y = layout.UpperLeft.Y;
        var w = layout.LowerRight.X - x;
        var h = layout.LowerRight.Y - y;

        if (_cachedDensity is null)
        {
            var origDensity = surface.Density.ChangeUnits(DensityUnit.PixelsPerInch);
            _cachedDensity = origDensity.X == 0 || origDensity.Y == 0 ? new Density(72, DensityUnit.PixelsPerInch) : origDensity;
            _cachedFactor = _cachedDensity.Y / 72.0;
        }

        var density = _cachedDensity;
        var factor = _cachedFactor;
        var gravity = GetGravity(horizAlignment, vertAlignment);
        var textString = text.ToString();

        var cacheKey = new CaptionCacheKey(
            textString,
            fontFamily,
            fontSize / factor,
            fontColor,
            (uint)w,
            (uint)h,
            gravity,
            density.X,
            density.Y
        );

        if (!_captionCache.TryGetValue(cacheKey, out var overlayImage))
        {
            var readSettings = new MagickReadSettings
            {
                Font = fontFamily,
                Width = (uint)w,
                Height = (uint)h,
                TextGravity = gravity,
                FontPointsize = fontSize / factor,
                BackgroundColor = new MagickColor(0, 0, 0, 0),
                FillColor = GetColor(fontColor),
                Density = density,
            };

            overlayImage = new MagickImage(string.Concat("caption:", textString), readSettings);
            _captionCache[cacheKey] = overlayImage;
        }

        surface.Composite(overlayImage, Gravity.Northwest, (int)x, (int)y, CompositeOperator.Over);
    }

    private static Gravity GetGravity(TextAlign horizAlignment, TextAlign vertAlignment)
    {
        return (horizAlignment, vertAlignment) switch
        {
            (TextAlign.Center, TextAlign.Center) => Gravity.Center,
            (TextAlign.Center, TextAlign.Near) => Gravity.South,
            (TextAlign.Center, TextAlign.Far) => Gravity.North,
            (TextAlign.Far, TextAlign.Center) => Gravity.East,
            (TextAlign.Far, TextAlign.Far) => Gravity.Northeast,
            (TextAlign.Far, TextAlign.Near) => Gravity.Southeast,
            (TextAlign.Near, TextAlign.Center) => Gravity.West,
            (TextAlign.Near, TextAlign.Far) => Gravity.Northwest,
            (TextAlign.Near, TextAlign.Near) => Gravity.Southwest,
            _ => Gravity.Undefined
        };
    }

    public void Dispose()
    {
        foreach (var cachedImage in _captionCache.Values)
        {
            cachedImage.Dispose();
        }
        _captionCache.Clear();
        GC.SuppressFinalize(this);
    }

    private readonly record struct CaptionCacheKey(
        string Text,
        string FontFamily,
        double FontPointSize,
        RGBAColor32 FontColor,
        uint Width,
        uint Height,
        Gravity Gravity,
        double DensityX,
        double DensityY
    );
}
