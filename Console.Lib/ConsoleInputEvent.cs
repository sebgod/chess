namespace Console.Lib;

/// <summary>
/// Represents a console input event: either a mouse event, a key press, or both with modifier state.
/// </summary>
public readonly record struct ConsoleInputEvent(MouseEvent? Mouse, ConsoleKey Key, ConsoleModifiers Modifiers);
