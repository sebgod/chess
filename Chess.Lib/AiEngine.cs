using System.Collections.Immutable;

namespace Chess.Lib;

/// <summary>
/// A simple computer player that selects a random legal move, preferring captures.
/// </summary>
public sealed class AiEngine(Side side)
{
    private static readonly Random Rng = new();

    /// <summary>
    /// The side the computer plays.
    /// </summary>
    public Side Side { get; } = side;

    /// <summary>
    /// Picks a legal move for the current board state, or returns <c>null</c> if no move is available.
    /// Captures are preferred over quiet moves.
    /// </summary>
    public Action? PickMove(Game game)
    {
        if (game.CurrentSide != Side || game.IsFinished)
        {
            return null;
        }

        var board = game.Board;
        var plies = game.Plies;

        var captures = new List<Action>();
        var quietMoves = new List<Action>();

        foreach (var (position, _) in board.AllPiecesOfSide(Side))
        {
            foreach (var move in board.ValidMoves(plies, position, Side))
            {
                var target = board[move.To];
                if (target.PieceType is not PieceType.None)
                {
                    captures.Add(move);
                }
                else
                {
                    quietMoves.Add(move);
                }
            }
        }

        if (captures.Count > 0)
        {
            return captures[Rng.Next(captures.Count)];
        }

        if (quietMoves.Count > 0)
        {
            return quietMoves[Rng.Next(quietMoves.Count)];
        }

        return null;
    }
}
