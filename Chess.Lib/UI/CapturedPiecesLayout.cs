namespace Chess.Lib.UI;

/// <summary>
/// Where <see cref="GameUI"/> puts the captured-piece piles — a layout choice, because the piles
/// are the only part of the board content that a host can host somewhere else.
/// </summary>
public enum CapturedPiecesLayout : byte
{
    /// <summary>Horizontal strips hugging the board's top and bottom edges, inside GameUI's own
    /// area. The default, and the only option for hosts that hand GameUI the whole surface
    /// (console/sixel, MCP snapshots) or have no room beside the board (portrait phones).</summary>
    Strips,

    /// <summary>GameUI draws no piles; the host renders them in its own chrome with
    /// <see cref="GameUI.RenderCapturedColumn{TSurface, TRenderer}"/>. The strips cost the board
    /// ~1.2 squares of height, so handing them to a side gutter buys a visibly bigger board on any
    /// surface wide enough to have gutters (tablets, desktop, phone landscape).</summary>
    External,
}
