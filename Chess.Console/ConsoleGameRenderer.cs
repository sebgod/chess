using Chess.Lib;

namespace Chess.Console;

/// <summary>
/// Renders text-based game chrome (status bar and move history) using VT escape sequences.
/// </summary>
internal sealed class ConsoleGameRenderer(int historyStartColumn, int historyColumnWidth, int historyRowCount, int statusBarRow, int totalWidth)
{
    /// <summary>
    /// Renders the status bar showing the current game state.
    /// </summary>
    public void RenderStatusBar(Game game)
    {
        System.Console.SetCursorPosition(0, statusBarRow);

        var currentPlayer = game.CurrentSide == Side.White ? "White" : "Black";
        var status = game.GameStatus switch
        {
            GameStatus.Check => $" {currentPlayer} to move (CHECK)",
            GameStatus.Checkmate => $" {(game.CurrentSide == Side.White ? "Black" : "White")} wins by checkmate!",
            GameStatus.Stalemate => " Draw by stalemate",
            _ => $" {currentPlayer} to move"
        };

        // White text on dark gray background
        System.Console.Write($"\e[97;100m{status.PadRight(totalWidth)}\e[0m");
    }

    /// <summary>
    /// Renders the move history panel on the right side of the screen.
    /// </summary>
    public void RenderHistory(Game game)
    {
        var plies = game.Plies;
        var moveCount = (plies.Count + 1) / 2;
        var startMove = Math.Max(0, moveCount - historyRowCount);

        // Render header
        System.Console.SetCursorPosition(historyStartColumn, 0);
        System.Console.Write($"\e[97;100m{" Move History".PadRight(historyColumnWidth)}\e[0m");

        for (var row = 1; row < historyRowCount; row++)
        {
            System.Console.SetCursorPosition(historyStartColumn, row);

            var moveIdx = startMove + row - 1;
            var plyIdx = moveIdx * 2;

            if (plyIdx < plies.Count)
            {
                var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(plyIdx);
                var blackPly = plyIdx + 1 < plies.Count ? plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

                var line = $" {idxStr} {whitePly,-8} {blackPly,-8}";
                System.Console.Write($"\e[37;40m{line.PadRight(historyColumnWidth)}\e[0m");
            }
            else
            {
                System.Console.Write($"\e[37;40m{new string(' ', historyColumnWidth)}\e[0m");
            }
        }
    }
}
