namespace Console.Lib;

/// <summary>
/// Single-line widget with left-aligned text and optional right-aligned text.
/// </summary>
public class TextBar : Widget
{
    private string _text = "";
    private string _rightText = "";
    private string _style = "\e[97;100m";

    public TextBar Text(string text) { _text = text; return this; }
    public TextBar RightText(string text) { _rightText = text; return this; }
    public TextBar Style(string vtStyle) { _style = vtStyle; return this; }

    public override void Render()
    {
        var width = Viewport.Size.Width;
        if (width <= 0) return;

        if (!TrySetCursorPosition(Viewport, 0, 0)) return;

        var padWidth = Math.Max(0, width - _rightText.Length);
        Viewport.Write($"{_style}{_text.PadRight(padWidth)}{_rightText}\e[0m");
    }
}
