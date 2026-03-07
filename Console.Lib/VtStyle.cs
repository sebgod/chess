using DIR.Lib;

namespace Console.Lib;

/// <summary>
/// Standard SGR (Select Graphic Rendition) colors for terminal text.
/// </summary>
public enum SgrColor : byte
{
    Black, Red, Green, Yellow, Blue, Magenta, Cyan, White,
    BrightBlack, BrightRed, BrightGreen, BrightYellow,
    BrightBlue, BrightMagenta, BrightCyan, BrightWhite,
}

public static class SgrColorExtensions
{
    private static readonly RGBAColor32[] SgrToRgba =
    [
        new(0x00, 0x00, 0x00, 0xff), // Black
        new(0xaa, 0x00, 0x00, 0xff), // Red
        new(0x00, 0xaa, 0x00, 0xff), // Green
        new(0xaa, 0x55, 0x00, 0xff), // Yellow (dark)
        new(0x00, 0x00, 0xaa, 0xff), // Blue
        new(0xaa, 0x00, 0xaa, 0xff), // Magenta
        new(0x00, 0xaa, 0xaa, 0xff), // Cyan
        new(0xaa, 0xaa, 0xaa, 0xff), // White
        new(0x55, 0x55, 0x55, 0xff), // BrightBlack
        new(0xff, 0x55, 0x55, 0xff), // BrightRed
        new(0x55, 0xff, 0x55, 0xff), // BrightGreen
        new(0xff, 0xff, 0x55, 0xff), // BrightYellow
        new(0x55, 0x55, 0xff, 0xff), // BrightBlue
        new(0xff, 0x55, 0xff, 0xff), // BrightMagenta
        new(0x55, 0xff, 0xff, 0xff), // BrightCyan
        new(0xff, 0xff, 0xff, 0xff), // BrightWhite
    ];

    public static RGBAColor32 ToRgba(this SgrColor color) => SgrToRgba[(int)color];

    /// <summary>
    /// Finds the nearest SGR color for the given RGBA color using Euclidean distance in RGB space.
    /// </summary>
    public static SgrColor NearestSgrColor(RGBAColor32 color)
    {
        var bestIdx = 0;
        var bestDist = int.MaxValue;
        for (var i = 0; i < SgrToRgba.Length; i++)
        {
            var c = SgrToRgba[i];
            var dr = color.Red - c.Red;
            var dg = color.Green - c.Green;
            var db = color.Blue - c.Blue;
            var dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return (SgrColor)bestIdx;
    }
}

/// <summary>
/// Controls how <see cref="VtStyle"/> emits color escape sequences.
/// </summary>
public enum ColorMode : byte
{
    /// <summary>16-color SGR codes (works everywhere).</summary>
    Sgr16,
    /// <summary>24-bit truecolor via <c>\e[38;2;R;G;Bm</c> / <c>\e[48;2;R;G;Bm</c>.</summary>
    TrueColor,
}

/// <summary>
/// A terminal text style represented as a foreground/background color pair.
/// Use <see cref="Apply"/> to produce the appropriate escape sequence for the terminal's color mode.
/// </summary>
public readonly record struct VtStyle(RGBAColor32 Foreground, RGBAColor32 Background)
{
    public const string Reset = "\e[0m";

    public VtStyle(SgrColor foreground, SgrColor background)
        : this(foreground.ToRgba(), background.ToRgba()) { }

    private static int FgCode(SgrColor c) => (int)c < 8 ? 30 + (int)c : 82 + (int)c;
    private static int BgCode(SgrColor c) => (int)c < 8 ? 40 + (int)c : 92 + (int)c;

    /// <summary>
    /// Returns the VT escape sequence for this style in the given <paramref name="colorMode"/>.
    /// </summary>
    public string Apply(ColorMode colorMode) => colorMode switch
    {
        ColorMode.TrueColor => $"\e[38;2;{Foreground.Red};{Foreground.Green};{Foreground.Blue};48;2;{Background.Red};{Background.Green};{Background.Blue}m",
        _ => $"\e[{FgCode(SgrColorExtensions.NearestSgrColor(Foreground))};{BgCode(SgrColorExtensions.NearestSgrColor(Background))}m",
    };

    /// <summary>
    /// Default <see cref="ToString"/> uses SGR-16 for maximum compatibility.
    /// Prefer <see cref="Apply"/> when the terminal's <see cref="ColorMode"/> is known.
    /// </summary>
    public override string ToString() => Apply(ColorMode.Sgr16);
}
