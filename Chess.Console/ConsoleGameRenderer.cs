using Chess.Lib;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders text-based game chrome (status bar and move history) using VT escape sequences.
/// </summary>
internal sealed class ConsoleGameRenderer
{
    private readonly int _historyColumnWidth;
    private int _historyStartColumn;
    private int _historyRowCount;
    private int _statusBarRow;
    private int _totalWidth;

    internal ConsoleGameRenderer(int historyColumnWidth, int consoleWidth, int consoleHeight)
    {
        _historyColumnWidth = historyColumnWidth;
        Resize(consoleWidth, consoleHeight);
    }

    /// <summary>
    /// Recalculates layout positions after a console resize.
    /// </summary>
    public void Resize(int consoleWidth, int consoleHeight)
    {
        _historyStartColumn = consoleWidth - _historyColumnWidth;
        _historyRowCount = consoleHeight - 1;
        _statusBarRow = consoleHeight - 1;
        _totalWidth = consoleWidth;
    }

    public bool NeedsResize(int newConsoleWidth, int newConsoleHeight) => _totalWidth != newConsoleWidth || _statusBarRow + 1 != newConsoleHeight;

    /// <summary>
    /// Renders the status bar showing the current game state.
    /// </summary>
    public void RenderStatusBar(Game game, RenderStats? stats = null, File? pendingFile = null)
    {
        System.Console.SetCursorPosition(0, _statusBarRow);

        var fileInfo = pendingFile is { } f ? $" [{f.ToLabel()}]" : "";
        var status = $" {game.GameStatus.ToMessage(game.CurrentSide)}{fileInfo}";

        var debugInfo = "";
        if (stats is { } s)
        {
            var total = s.FullRenders + s.PartialRenders;
            if (total > 0)
            {
                debugInfo = $"{s.LastFrameMs,6:F1}ms  F:{s.FullRenders} P:{s.PartialRenders} ({100.0 * s.PartialRenders / total:F0}% partial) ";
            }
        }

        var padWidth = _totalWidth - debugInfo.Length;
        System.Console.Write($"\e[97;100m{status.PadRight(padWidth)}{debugInfo}\e[0m");
    }

    /// <summary>
    /// Renders the move history panel on the right side of the screen.
    /// </summary>
    public void RenderHistory(Game game)
    {
        var plies = game.Plies;
        var moveCount = (plies.Count + 1) / 2;
        var startMove = Math.Max(0, moveCount - _historyRowCount);

        // Render header
        System.Console.SetCursorPosition(_historyStartColumn, 0);
        System.Console.Write($"\e[97;100m{" Move History".PadRight(_historyColumnWidth)}\e[0m");

        for (var row = 1; row < _historyRowCount; row++)
        {
            System.Console.SetCursorPosition(_historyStartColumn, row);

            var moveIdx = startMove + row - 1;
            var plyIdx = moveIdx * 2;

            if (plyIdx < plies.Count)
            {
                var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(plyIdx);
                var blackPly = plyIdx + 1 < plies.Count ? plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

                var line = $" {idxStr} {whitePly,-8} {blackPly,-8}";
                System.Console.Write($"\e[37;40m{line.PadRight(_historyColumnWidth)}\e[0m");
            }
            else
            {
                System.Console.Write($"\e[37;40m{new string(' ', _historyColumnWidth)}\e[0m");
            }
        }
    }
}
