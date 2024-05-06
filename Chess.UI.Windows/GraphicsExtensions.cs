using Chess.Lib.UI;
using System.Runtime.CompilerServices;

namespace Chess.UI.Windows;

public static class GraphicsExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringAlignment ToStringAlignment(this TextAlign lineAlignment) => lineAlignment switch
    {
        TextAlign.Center => StringAlignment.Center,
        TextAlign.Near => StringAlignment.Near,
        TextAlign.Far => StringAlignment.Far,
        _ => throw new ArgumentException($"Unknown line alignment {lineAlignment}", nameof(lineAlignment))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToColor(this RGBAColor8B color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangleF ToRectF(this in RectInt rect) => RectangleF.FromLTRB(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rectangle ToRectInt(this in RectInt rect) => Rectangle.FromLTRB(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);
}
