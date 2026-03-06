using Chess.Lib;
using Console.Lib;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders text-based game chrome (status bar and move history) using VT escape sequences.
/// </summary>
internal sealed class ConsoleGameRenderer
{
    private readonly IVirtualTerminal _terminal;
    private readonly int _historyColumnWidth;
    private int _historyStartColumn;
    private int _historyRowCount;
    private int _statusBarRow;
    private int _totalWidth;

    internal ConsoleGameRenderer(IVirtualTerminal terminal, int historyColumnWidth, int consoleWidth, int consoleHeight)
    {
        _terminal = terminal;
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

    private bool TrySetCursorPosition(int left, int top)
    {
        try
        {
            _terminal.SetCursorPosition(left, top);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public bool NeedsResize(int newConsoleWidth, int newConsoleHeight) => _totalWidth != newConsoleWidth || _statusBarRow + 1 != newConsoleHeight;

    /// <summary>
    /// Renders the status bar showing the current game state.
    /// </summary>
    public void RenderStatusBar(Game game, RenderStats? stats = null, File? pendingFile = null, Side? placementSide = null, (int PlyIndex, int PlyCount)? playbackInfo = null)
    {
        if (_statusBarRow < 0 || _totalWidth <= 0)
            return;

        if (!TrySetCursorPosition(0, _statusBarRow))
            return;

        var fileInfo = pendingFile is { } f ? $" [{f.ToLabel()}]" : "";
        var setupInfo = placementSide is { } side ? $" Setup: placing {side} pieces [Tab to toggle; s to start]" : "";
        string status;
        if (playbackInfo is (var plyIdx, var plyCount))
        {
            status = $" Playback: ply {plyIdx + 2}/{plyCount + 1} [Ctrl+Up/Down, Esc exit]";
        }
        else if (placementSide is { })
        {
            status = $" {setupInfo}{fileInfo}";
        }
        else
        {
            status = $" {game.GameStatus.ToMessage(game.CurrentSide)}{fileInfo}";
        }

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
        _terminal.Write($"\e[97;100m{status.PadRight(padWidth)}{debugInfo}\e[0m");
    }

    /// <summary>
    /// Renders the move history panel on the right side of the screen.
    /// </summary>
    public void RenderHistory(Game game, int? highlightPlyIndex = null, int? scrollStart = null)
    {
        if (_historyStartColumn < 0)
            return;

        var plies = game.Plies;
        var moveCount = (plies.Count + 1) / 2;
        var startMove = scrollStart ?? Math.Max(0, moveCount - (_historyRowCount - 1));

        // Render header
        if (!TrySetCursorPosition(_historyStartColumn, 0))
            return;
        _terminal.Write($"\e[97;100m{" Move History".PadRight(_historyColumnWidth)}\e[0m");

        for (var row = 1; row < _historyRowCount; row++)
        {
            if (!TrySetCursorPosition(_historyStartColumn, row))
                return;

            var moveIdx = startMove + row - 1;
            var plyIdx = moveIdx * 2;

            if (plyIdx < plies.Count)
            {
                var (idxStr, whitePly) = plies.GetRecordAndPGNIdx(plyIdx);
                var blackPlyStr = plyIdx + 1 < plies.Count ? plies.GetRecordAndPGNIdx(plyIdx + 1).Ply.ToString() : "";

                var isHighlightedWhite = highlightPlyIndex == plyIdx;
                var isHighlightedBlack = highlightPlyIndex == plyIdx + 1;

                if (isHighlightedWhite || isHighlightedBlack)
                {
                    // Render move number prefix in normal color
                    var prefix = $" {idxStr} ";
                    var whiteText = $"{whitePly,-8}";
                    var blackText = $" {blackPlyStr,-8}";
                    var remaining = _historyColumnWidth - prefix.Length - whiteText.Length - blackText.Length;

                    var whiteColor = isHighlightedWhite ? "\e[97;44m" : "\e[37;40m";
                    var blackColor = isHighlightedBlack ? "\e[97;44m" : "\e[37;40m";

                    _terminal.Write($"\e[37;40m{prefix}{whiteColor}{whiteText}\e[37;40m{blackColor}{blackText}\e[37;40m{new string(' ', Math.Max(0, remaining))}\e[0m");
                }
                else
                {
                    var line = $" {idxStr} {whitePly,-8} {blackPlyStr,-8}";
                    _terminal.Write($"\e[37;40m{line.PadRight(_historyColumnWidth)}\e[0m");
                }
            }
            else
            {
                _terminal.Write($"\e[37;40m{new string(' ', _historyColumnWidth)}\e[0m");
            }
        }
    }

    /// <summary>
    /// Converts pixel coordinates to a ply index in the history panel.
    /// Returns null if the click is outside the history area.
    /// </summary>
    public int? PlyIndexFromPixel(int pixelX, int pixelY, uint cellWidth, uint cellHeight, int plyCount, int? scrollStart = null)
    {
        var cellCol = pixelX / (int)cellWidth;
        var cellRow = pixelY / (int)cellHeight;

        if (cellCol < _historyStartColumn || cellRow < 1 || cellRow >= _historyRowCount)
            return null;

        var moveCount = (plyCount + 1) / 2;
        var startMove = scrollStart ?? Math.Max(0, moveCount - (_historyRowCount - 1));
        var moveIdx = startMove + cellRow - 1;
        var whitePlyIdx = moveIdx * 2;

        if (whitePlyIdx >= plyCount)
            return null;

        // Right half of the row → black ply if it exists
        var midCol = _historyStartColumn + _historyColumnWidth / 2;
        if (cellCol >= midCol && whitePlyIdx + 1 < plyCount)
            return whitePlyIdx + 1;

        return whitePlyIdx;
    }
}
