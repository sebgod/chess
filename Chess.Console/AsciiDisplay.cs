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
    /// Markdown-formatted version of <see cref="GameUI.KeymapText"/> for styled terminal rendering.
    /// </summary>
    private const string KeymapMarkdown =
        "### Keyboard Controls\n" +
        "\n" +
        "### Gameplay\n" +
        "- **a-h** Select file\n" +
        "- **1-8** Select rank\n" +
        "- **Esc** Cancel selection\n" +
        "\n" +
        "### Playback\n" +
        "- **Ctrl+Arrow** Navigate history\n" +
        "- **Esc** Exit playback\n" +
        "\n" +
        "### Promotion\n" +
        "- **n/b/r/q** Select piece\n" +
        "\n" +
        "### Custom Setup\n" +
        "- **p/n/b/r/q/k** Place piece\n" +
        "- **Tab** Toggle side\n" +
        "- **Del** Clear square\n" +
        "- **s** Start game\n" +
        "\n" +
        "---\n" +
        "- **F1** Toggle this help\n" +
        "- **F9** New game";

    public GameUI UI => _gameUI ?? throw new InvalidOperationException("Call ResetGame before accessing UI.");

    public void RenderInitial(Game game)
    {
        RenderBoard(game);
        WritePrompt(game, pendingFile: null);
    }

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
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
            WriteMarkdown($"**Setup:** placing *{UI.PlacementSide}* pieces — **Tab** toggle | **s** start");
        }

        WritePrompt(game, pendingFile);
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
        if (UI.Mode == GameUIMode.Playback)
        {
            WriteMarkdown($"**Playback:** ply {UI.PlaybackPlyIndex + 2}/{game.PlyCount + 1} — *Ctrl+Up/Down* navigate | *Esc* exit");
        }
        else
        {
            WriteMarkdown($"**{game.GameStatus.ToMessage(game.CurrentSide)}**");
        }

        var plies = game.Plies;
        if (plies.Count > 0)
        {
            _terminal.WriteLine();
            _terminal.Write($" {plies.ToPGN()}  ");
        }

        _terminal.WriteLine();
    }

    private void WritePrompt(Game game, File? pendingFile)
    {
        if (UI.ShowingKeymap || game.GameStatus is not (GameStatus.Ongoing or GameStatus.Check))
            return;

        var fileChar = pendingFile is { } f ? ((char)('a' + (int)f)).ToString() : "";
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
