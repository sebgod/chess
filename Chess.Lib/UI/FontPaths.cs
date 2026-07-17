namespace Chess.Lib.UI;

/// <summary>
/// Canonical absolute paths to the fonts every front-end's chrome draws with (GameUI resolves
/// its own board fonts internally via its ctor defaults; this is for everything AROUND the board:
/// menus, history panels, status bars, snapshot annotations). One place instead of each host
/// re-deriving AppContext.BaseDirectory + "Fonts" + name.
/// </summary>
public static class FontPaths
{
    /// <summary>The Fonts directory next to the executable (on WASM: the in-memory FS path the
    /// host stages fetched fonts into).</summary>
    public static string FontsDirectory => Path.Combine(AppContext.BaseDirectory, "Fonts");

    /// <summary>Label/UI font — coordinates, menus, status text, history.</summary>
    public static string DejaVuSans => Path.Combine(FontsDirectory, "DejaVuSans.ttf");

    /// <summary>Chess piece glyph font (U+2654–U+265F outlines).</summary>
    public static string Merida => Path.Combine(FontsDirectory, "Merida.ttf");
}
