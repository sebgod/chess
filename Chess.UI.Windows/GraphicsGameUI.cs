using Chess.Lib;
using Chess.Lib.UI;
using System.Runtime.CompilerServices;

namespace Chess.UI.Windows;

public class GraphicsGameUI(FontCache fontCache, Game game, int uiSizeX, int uiSizeY) : GameUIBase<Graphics>(game, uiSizeX, uiSizeY)
{
    protected override void DrawRectangle(Graphics surface, in RectLTRBInt rect, RGBAColor8B strokeColor, int strokeWidth)
    {
        using var pen = new Pen(GetColor(strokeColor), strokeWidth);
        surface.DrawRectangle(pen, GetRectF(rect));
    }

    protected override void FillRectangle(Graphics surface, in RectLTRBInt rect, RGBAColor8B fillColor)
    {
        using var brush = new SolidBrush(GetColor(fillColor));
        surface.FillRectangle(brush, GetRectF(rect));
    }

    protected override void DrawText(Graphics surface, string text, string fontFileOrFamily, float pointSize, RGBAColor8B fontColor, in RectLTRBInt rect,
        TextAlign horizAlignment = TextAlign.Near, TextAlign vertAlignment = TextAlign.Center)
    {
        var fontFamily = fontCache.GetFontFamily(fontFileOrFamily);

        using var gdiFont = new Font(fontFamily, (float)pointSize, GraphicsUnit.Point);
        using var brush = new SolidBrush(GetColor(fontColor));
        using var format = new StringFormat
        {
            Alignment = ToStringAlignment(horizAlignment),
            LineAlignment = ToStringAlignment(vertAlignment),
            FormatFlags = StringFormatFlags.NoFontFallback | StringFormatFlags.NoClip
        };
        
        surface.DrawString(text, gdiFont, brush, GetRectF(rect), format);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StringAlignment ToStringAlignment(TextAlign lineAlignment) => lineAlignment switch
    {
        TextAlign.Center => StringAlignment.Center,
        TextAlign.Near => StringAlignment.Near,
        TextAlign.Far => StringAlignment.Far,
        _ => throw new ArgumentException($"Unknown line alignment {lineAlignment}", nameof(lineAlignment))
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color GetColor(RGBAColor8B color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RectangleF GetRectF(in RectLTRBInt rect) => RectangleF.FromLTRB(rect.UpperLeft.X, rect.UpperLeft.Y, rect.LowerRight.X, rect.LowerRight.Y);
}
