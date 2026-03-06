namespace Console.Lib;

/// <summary>
/// Pixel dimensions of a single terminal cell.
/// </summary>
public readonly record struct TermCell(byte Width, byte Height);
