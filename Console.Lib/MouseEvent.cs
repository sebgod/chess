namespace Console.Lib;

/// <summary>
/// Represents a mouse button event with pixel position and press/release state.
/// </summary>
public readonly record struct MouseEvent(int Button, int X, int Y, bool IsRelease);
