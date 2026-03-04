using System.Collections.Immutable;
using Chess.Lib;
using Chess.Lib.UI;

using File = Chess.Lib.File;

namespace Chess.Console;

/// <summary>
/// Renders the board as ASCII text using FEN notation, for terminals without Sixel support.
/// </summary>
internal sealed class AsciiDisplay : IGameDisplay
{
    private string _lastFen = "";

    public GameUI UI { get; private set; }

    public AsciiDisplay(Game game)
    {
        UI = new GameUI(game, 800, 800);
    }

    public void RenderInitial(Game game, File? pendingFile) => RenderBoard(game);

    public void RenderMove(Game game, UIResponse response, ImmutableArray<RectInt> clipRects, File? pendingFile)
    {
        if (response.HasFlag(UIResponse.NeedsRefresh) || response.HasFlag(UIResponse.IsUpdate))
        {
            RenderBoard(game);
        }
        if (UI.IsSetupMode && (response.HasFlag(UIResponse.IsUpdate) || response.HasFlag(UIResponse.NeedsPiecePlacement)))
        {
            System.Console.WriteLine($" Setup: placing {UI.PlacementSide} pieces [Tab to toggle; s to start]  ");
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
        var fen = game.Board.ToFEN();
        if (fen == _lastFen)
            return;

        _lastFen = fen;

        System.Console.WriteLine();

        var ranks = fen.Split('/');
        for (var i = 0; i < ranks.Length; i++)
        {
            var rankLabel = 8 - i;
            System.Console.Write($" {rankLabel}  ");

            foreach (var c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    for (var j = 0; j < c - '0'; j++)
                        System.Console.Write(" .");
                }
                else
                {
                    System.Console.Write($" {c}");
                }
            }

            System.Console.WriteLine("  ");
        }

        System.Console.WriteLine();
        System.Console.Write("    ");
        for (var f = 0; f < 8; f++)
            System.Console.Write($" {(char)('a' + f)}");
        System.Console.WriteLine("  ");

        System.Console.WriteLine();
        System.Console.Write($" {game.GameStatus.ToMessage(game.CurrentSide)}  ");

        var plies = game.Plies;
        if (plies.Count > 0)
        {
            System.Console.WriteLine();
            System.Console.Write($" {plies.ToPGN()}  ");
        }

        System.Console.WriteLine();
    }

    public void Dispose() { }
}
