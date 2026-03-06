using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders the board as ASCII text using FEN notation, for terminals without Sixel support.
/// </summary>
internal sealed class AsciiDisplay(IVirtualTerminal terminal, Game game) : IGameDisplay
{
    private readonly IVirtualTerminal _terminal = terminal;
    private string _lastFen = "";

    public GameUI UI { get; private set; } = new GameUI(game, 800, 800);

    public void RenderInitial(Game game) => RenderBoard(game);

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (UI.ShowingKeymap && response.HasFlag(UIResponse.NeedsRefresh))
        {
            _terminal.WriteLine();
            _terminal.WriteLine(GameUI.KeymapText);
            _terminal.WriteLine();
            return;
        }

        if (response.HasFlag(UIResponse.NeedsRefresh) || response.HasFlag(UIResponse.IsUpdate))
        {
            RenderBoard(game);
        }
        if (UI.IsSetupMode && (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement)))
        {
            _terminal.WriteLine($" Setup: placing {UI.PlacementSide} pieces [Tab to toggle; s to start]  ");
        }
    }

    public void HandleResize(Game game) { }

    public void ResetGame(Game game)
    {
        UI = new GameUI(game, 800, 800);
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
            _terminal.Write($" Playback: ply {UI.PlaybackPlyIndex + 2}/{game.PlyCount + 1} [Ctrl+Up/Down, Esc exit]  ");
        }
        else
        {
            _terminal.Write($" {game.GameStatus.ToMessage(game.CurrentSide)}  ");
        }

        var plies = game.Plies;
        if (plies.Count > 0)
        {
            _terminal.WriteLine();
            _terminal.Write($" {plies.ToPGN()}  ");
        }

        _terminal.WriteLine();
    }

    public void Dispose() { }
}
