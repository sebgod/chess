namespace Chess.GUI;

/// <summary>Request to restart the game (back to menu). Triggered by F8.</summary>
public readonly record struct RequestRestartSignal;

/// <summary>Request to reset the current game (new game, same settings). Triggered by F9.</summary>
public readonly record struct RequestResetSignal;
