using Chess.Lib;
using Console.Lib;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders text-based game chrome (status bar and move history) using VT escape sequences.
/// </summary>
internal sealed class ConsoleGameRenderer
{
    private readonly ITerminalViewport _historyViewport;
    private readonly ITerminalViewport _statusBarViewport;

    internal ConsoleGameRenderer(
        ITerminalViewport historyViewport,
        ITerminalViewport statusBarViewport)
    {
        _historyViewport = historyViewport;
        _statusBarViewport = statusBarViewport;
    }

    private static bool TrySetCursorPosition(ITerminalViewport viewport, int left, int top)
    {
        try
        {
            viewport.SetCursorPosition(left, top);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Renders the status bar showing the current game state.
    /// </summary>
    public void RenderStatusBar(Game game, RenderStats? stats = null, File? pendingFile = null, Side? placementSide = null, (int PlyIndex, int PlyCount)? playbackInfo = null)
    {
        var totalWidth = _statusBarViewport.Size.Width;
        if (totalWidth <= 0)
            return;

        if (!TrySetCursorPosition(_statusBarViewport, 0, 0))
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

        var padWidth = totalWidth - debugInfo.Length;
        _statusBarViewport.Write($"\e[97;100m{status.PadRight(padWidth)}{debugInfo}\e[0m");
    }

    /// <summary>
    /// Renders the move history panel on the right side of the screen.
    /// </summary>
    public void RenderHistory(Game game, int? highlightPlyIndex = null, int? scrollStart = null)
    {
        var historyRowCount = _historyViewport.Size.Height;

        // Render header
        if (!TrySetCursorPosition(_historyViewport, 0, 0))
            return;
        _historyViewport.Write($"\e[97;100m{" Move History".PadRight(_historyViewport.Size.Width)}\e[0m");

        var plies = game.Plies;
        var moveCount = (plies.Count + 1) / 2;
        var startMove = scrollStart ?? Math.Max(0, moveCount - (historyRowCount - 1));

        for (var row = 1; row < historyRowCount; row++)
        {
            if (!TrySetCursorPosition(_historyViewport, 0, row))
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
                    var remaining = _historyViewport.Size.Width - prefix.Length - whiteText.Length - blackText.Length;

                    var whiteColor = isHighlightedWhite ? "\e[97;44m" : "\e[37;40m";
                    var blackColor = isHighlightedBlack ? "\e[97;44m" : "\e[37;40m";

                    _historyViewport.Write($"\e[37;40m{prefix}{whiteColor}{whiteText}\e[37;40m{blackColor}{blackText}\e[37;40m{new string(' ', Math.Max(0, remaining))}\e[0m");
                }
                else
                {
                    var line = $" {idxStr} {whitePly,-8} {blackPlyStr,-8}";
                    _historyViewport.Write($"\e[37;40m{line.PadRight(_historyViewport.Size.Width)}\e[0m");
                }
            }
            else
            {
                _historyViewport.Write($"\e[37;40m{new string(' ', _historyViewport.Size.Width)}\e[0m");
            }
        }
    }

    /// <summary>
    /// Converts viewport-local cell coordinates to a ply index in the history panel.
    /// Returns null if the cell is outside the history data area.
    /// </summary>
    public int? PlyIndexFromCell(int cellCol, int cellRow, int plyCount, int? scrollStart = null)
    {
        var historyRowCount = _historyViewport.Size.Height;

        if (cellCol < 0 || cellRow < 1 || cellRow >= historyRowCount)
            return null;

        var moveCount = (plyCount + 1) / 2;
        var startMove = scrollStart ?? Math.Max(0, moveCount - (historyRowCount - 1));
        var moveIdx = startMove + cellRow - 1;
        var whitePlyIdx = moveIdx * 2;

        if (whitePlyIdx >= plyCount)
            return null;

        // Right half of the row -> black ply if it exists
        var midCol = _historyViewport.Size.Width / 2;
        if (cellCol >= midCol && whitePlyIdx + 1 < plyCount)
            return whitePlyIdx + 1;

        return whitePlyIdx;
    }
}
