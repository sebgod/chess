using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;
using DIR.Lib;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders the board as ASCII text using FEN notation, for terminals without Sixel support.
/// Uses <see cref="MarkdownRenderer"/> for styled keymap help and status output.
/// </summary>
internal sealed class AsciiDisplay(IVirtualTerminal terminal) : IGameDisplay
{
    private readonly IVirtualTerminal _terminal = terminal;
    private string _lastFen = "";
    private GameUI? _gameUI;

    /// <summary>
    /// Markdown-formatted version of <see cref="GameUI.KeymapText"/>, DERIVED from it so the two
    /// can never drift (the previous hand-copied constant had already lost the F8 line).
    /// </summary>
    private static readonly string KeymapMarkdown = BuildKeymapMarkdown(GameUI.KeymapText);

    /// <summary>
    /// Converts the plain-text keymap into Markdown: a line whose key column is separated from
    /// its description by 2+ spaces becomes a "- **key** description" bullet; every other
    /// non-blank line is a "### " section heading.
    /// </summary>
    private static string BuildKeymapMarkdown(string keymapText)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var raw in keymapText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            var split = line.IndexOf("  ", StringComparison.Ordinal);
            if (split > 0)
            {
                sb.Append("- **").Append(line[..split]).Append("** ")
                  .Append(line[split..].TrimStart()).Append('\n');
            }
            else
            {
                sb.Append("### ").Append(line).Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    public void RenderInitial(Game game)
    {
        RenderBoard(game);
        WritePrompt(game);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects)
    {
        if (UI.ShowingKeymap && response.HasFlag(UIResponse.NeedsRefresh))
        {
            _terminal.WriteLine();
            WriteMarkdown(KeymapMarkdown);
            _terminal.WriteLine();
            return;
        }

        if (response.HasFlag(UIResponse.NeedsRefresh) || response.HasFlag(UIResponse.IsUpdate))
        {
            RenderBoard(game);
        }
        if (UI.IsSetupMode && (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement)))
        {
            WriteMarkdown($"**{UI.StatusLine()}**");
        }

        WritePrompt(game);
    }

    public void HandleResize(Game game) { }

    public void ResetGame(Game game)
    {
        _gameUI = new GameUI(game, 800, 800);
        _lastFen = "";
    }

    private void RenderBoard(Game game)
    {
        var fen = UI.DisplayBoard.ToFEN();
        if (fen == _lastFen)
            return;

        _lastFen = fen;

        _terminal.WriteLine();

        var ranks = fen.Split('/');
        for (var i = 0; i < ranks.Length; i++)
        {
            var rankLabel = 8 - i;
            _terminal.Write($" {rankLabel}  ");

            foreach (var c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    for (var j = 0; j < c - '0'; j++)
                        _terminal.Write(" .");
                }
                else
                {
                    _terminal.Write($" {c}");
                }
            }

            _terminal.WriteLine("  ");
        }

        _terminal.WriteLine();
        _terminal.Write("    ");
        for (var f = 0; f < 8; f++)
            _terminal.Write($" {(char)('a' + f)}");
        _terminal.WriteLine("  ");

        _terminal.WriteLine();
        // Canonical mode-aware status from GameUI, bolded for the Markdown terminal renderer.
        WriteMarkdown($"**{UI.StatusLine()}**");

        var plies = game.Plies;
        if (plies.Count > 0)
        {
            _terminal.WriteLine();
            _terminal.Write($" {plies.ToPGN()}  ");
        }

        _terminal.WriteLine();
    }

    private void WritePrompt(Game game)
    {
        if (UI.ShowingKeymap || game.GameStatus is not (GameStatus.Ongoing or GameStatus.Check))
            return;

        var fileChar = UI.PendingFile is { } f ? ((char)('a' + (int)f)).ToString() : "";
        var selected = UI.Selected is { } sel ? $" [{sel}]" : "";
        _terminal.WriteInPlace($"> {fileChar}{selected}");
    }

    private void WriteMarkdown(string markdown)
    {
        var width = _terminal.Size.Width;
        var lines = MarkdownRenderer.RenderLines(markdown, width, _terminal.ColorMode);
        foreach (var line in lines)
            _terminal.WriteLine(line);
    }

    public void Dispose() { }
}
