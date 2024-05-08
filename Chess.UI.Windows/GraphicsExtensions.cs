using Chess.Lib.UI;
using System.Runtime.CompilerServices;

namespace Chess.UI.Windows;

public static class GraphicsExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringAlignment ToStringAlignment(this TextAlign alignment) => alignment switch
    {
        TextAlign.Center => StringAlignment.Center,
        TextAlign.Near => StringAlignment.Near,
        TextAlign.Far => StringAlignment.Far,
        _ => throw new ArgumentException($"Unknown line alignment {alignment}", nameof(alignment))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextFormatFlags ToHorizontalFlag(this TextAlign alignment) => alignment switch
    {
        TextAlign.Center => TextFormatFlags.HorizontalCenter,
        TextAlign.Near => TextFormatFlags.Left,
        TextAlign.Far => TextFormatFlags.Right,
        _ => throw new ArgumentException($"Unknown line alignment {alignment}", nameof(alignment))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextFormatFlags ToVerticalFlag(this TextAlign alignment) => alignment switch
    {
        TextAlign.Center => TextFormatFlags.VerticalCenter,
        TextAlign.Near => TextFormatFlags.Top,
        TextAlign.Far => TextFormatFlags.Bottom,
        _ => throw new ArgumentException($"Unknown line alignment {alignment}", nameof(alignment))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToColor(this RGBAColor8B color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RectangleF ToRectF(this in RectInt rect) => RectangleF.FromLTRB(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rectangle ToRectInt(this in RectInt rect) => Rectangle.FromLTRB(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);
}
