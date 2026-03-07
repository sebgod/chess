namespace Console.Lib;

/// <summary>
/// Implemented by items in a <see cref="ScrollableList{TItem}"/> to produce a styled VT row.
/// </summary>
public interface IRowFormatter
{
    /// <summary>
    /// Formats this item as a single row of the given <paramref name="width"/>.
    /// The returned string must include VT escape codes and pad to the full width.
    /// </summary>
    string FormatRow(int width, ColorMode colorMode);
}
