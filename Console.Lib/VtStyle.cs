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

/// <summary>
/// A terminal text style represented as a foreground/background color pair.
/// Produces SGR escape codes via <see cref="ToString"/>, usable directly in string interpolation.
/// </summary>
public readonly record struct VtStyle(SgrColor Foreground, SgrColor Background)
{
    public const string Reset = "\e[0m";

    private static int FgCode(SgrColor c) => (int)c < 8 ? 30 + (int)c : 82 + (int)c;
    private static int BgCode(SgrColor c) => (int)c < 8 ? 40 + (int)c : 92 + (int)c;

    public override string ToString() => $"\e[{FgCode(Foreground)};{BgCode(Background)}m";
}
