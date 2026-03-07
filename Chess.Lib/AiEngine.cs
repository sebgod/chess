using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Chess.Lib;

/// <summary>
/// Chess engine using negamax search with alpha-beta pruning and quiescence search.
/// Evaluation uses centipawn piece values and piece-square tables.
/// </summary>
public sealed class AiEngine(Side side, int maxDepth = AiEngine.DefaultDepth)
{
    public const int DefaultDepth = 4;
    public const int MateScore = 100_000;
    private const int InfiniteScore = MateScore + 1;

    private static readonly int[] PieceValues =
    [
        0,    // None
        100,  // Pawn
        320,  // Knight
        330,  // Bishop
        500,  // Rook
        900,  // Queen
        20000 // King
    ];

    // Piece-square tables (from White's perspective, index = rank * 8 + file, A1 = 0)
    // Flipped for Black by mirroring rank: index = (7 - rank) * 8 + file
    private static readonly int[] PawnPST =
    [
         0,  0,  0,  0,  0,  0,  0,  0,
         5, 10, 10,-20,-20, 10, 10,  5,
         5, -5,-10,  0,  0,-10, -5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5,  5, 10, 25, 25, 10,  5,  5,
        10, 10, 20, 30, 30, 20, 10, 10,
        50, 50, 50, 50, 50, 50, 50, 50,
         0,  0,  0,  0,  0,  0,  0,  0,
    ];

    private static readonly int[] KnightPST =
    [
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
    ];

    private static readonly int[] BishopPST =
    [
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    ];

    private static readonly int[] RookPST =
    [
         0,  0,  0,  5,  5,  0,  0,  0,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
         5, 10, 10, 10, 10, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0,
    ];

    private static readonly int[] QueenPST =
    [
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -10,  5,  5,  5,  5,  5,  0,-10,
          0,  0,  5,  5,  5,  5,  0, -5,
         -5,  0,  5,  5,  5,  5,  0, -5,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
    ];

    private static readonly int[] KingPST =
    [
         20, 30, 10,  0,  0, 10, 30, 20,
         20, 20,  0,  0,  0,  0, 20, 20,
        -10,-20,-20,-20,-20,-20,-20,-10,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
    ];

    private static readonly int[][] PieceSquareTables =
    [
        [], // None
        PawnPST,
        KnightPST,
        BishopPST,
        RookPST,
        QueenPST,
        KingPST,
    ];

    public Side Side { get; } = side;
    public int MaxDepth { get; } = maxDepth;
    public long NodesSearched { get; private set; }

    /// <summary>
    /// Search result containing the best move and its evaluation score in centipawns.
    /// </summary>
    public readonly record struct SearchResult(Action? BestMove, int Score, int Depth, long Nodes);

    /// <summary>
    /// Picks the best move using iterative deepening negamax with alpha-beta pruning.
    /// </summary>
    public SearchResult Search(Game game, Action<SearchResult>? onDepthComplete = null)
    {
        if (game.CurrentSide != Side || game.IsFinished)
            return new SearchResult(null, 0, 0, 0);

        NodesSearched = 0;
        SearchResult best = default;

        // Iterative deepening
        for (var depth = 1; depth <= MaxDepth; depth++)
        {
            var result = SearchRoot(game.Board, game.Plies, Side, depth);
            best = result;
            onDepthComplete?.Invoke(result);

            // Stop if we found a forced mate
            if (Math.Abs(result.Score) >= MateScore - 100)
                break;
        }

        return best;
    }

    /// <summary>
    /// Picks a legal move for the current board state, or returns <c>null</c> if no move is available.
    /// </summary>
    public Action? PickMove(Game game) => Search(game).BestMove;

    private SearchResult SearchRoot(Board board, ImmutableList<RecordedPly> plies, Side side, int depth)
    {
        var moves = GenerateMoves(board, plies, side);
        OrderMoves(moves, board);

        if (moves.Count == 0)
            return new SearchResult(null, board.IsCheck(side) ? -MateScore : 0, depth, NodesSearched);

        var bestScore = -InfiniteScore;
        Action? bestMove = null;

        foreach (var move in moves)
        {
            var ((result, _), newBoard, newPlies) = board.EvaluateAction(plies, move, skipGameResultCheck: true);
            if (!result.IsMoveOrCapture()) continue;

            NodesSearched++;
            var score = -Negamax(newBoard, newPlies, side.ToOpposite(), depth - 1, -InfiniteScore, -bestScore);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }

        return new SearchResult(bestMove, bestScore, depth, NodesSearched);
    }

    private int Negamax(Board board, ImmutableList<RecordedPly> plies, Side side, int depth, int alpha, int beta)
    {
        if (depth <= 0)
            return Quiescence(board, plies, side, alpha, beta);

        var moves = GenerateMoves(board, plies, side);
        OrderMoves(moves, board);

        if (moves.Count == 0)
            return board.IsCheck(side) ? -(MateScore - (MaxDepth - depth)) : 0;

        foreach (var move in moves)
        {
            var ((result, _), newBoard, newPlies) = board.EvaluateAction(plies, move, skipGameResultCheck: true);
            if (!result.IsMoveOrCapture()) continue;

            NodesSearched++;
            var score = -Negamax(newBoard, newPlies, side.ToOpposite(), depth - 1, -beta, -alpha);

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    private int Quiescence(Board board, ImmutableList<RecordedPly> plies, Side side, int alpha, int beta)
    {
        var standPat = Evaluate(board, side);

        if (standPat >= beta)
            return beta;

        if (standPat > alpha)
            alpha = standPat;

        // Only search captures
        var captures = GenerateCaptures(board, plies, side);
        OrderMoves(captures, board);

        foreach (var move in captures)
        {
            var ((result, _), newBoard, newPlies) = board.EvaluateAction(plies, move, skipGameResultCheck: true);
            if (!result.IsCapture()) continue;

            NodesSearched++;
            var score = -Quiescence(newBoard, newPlies, side.ToOpposite(), -beta, -alpha);

            if (score >= beta)
                return beta;

            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    /// <summary>
    /// Static evaluation from the perspective of <paramref name="side"/> in centipawns.
    /// Positive = good for <paramref name="side"/>.
    /// </summary>
    public static int Evaluate(Board board, Side side)
    {
        var score = 0;

        foreach (var rank in Position.AllRanks)
        {
            foreach (var file in Position.AllFiles)
            {
                var pos = new Position(file, rank);
                var piece = board[pos];
                if (piece.PieceType is PieceType.None) continue;

                var pieceValue = PieceValues[(int)piece.PieceType];
                var pstIndex = GetPSTIndex(file, rank, piece.Side);
                var pstValue = PieceSquareTables[(int)piece.PieceType][pstIndex];

                if (piece.Side == side)
                    score += pieceValue + pstValue;
                else
                    score -= pieceValue + pstValue;
            }
        }

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPSTIndex(File file, Rank rank, Side side)
    {
        var r = (int)rank;
        var f = (int)file;
        // PST is from White's perspective (rank 0 = rank 1). Flip for Black.
        return side == Side.White ? r * 8 + f : (7 - r) * 8 + f;
    }

    private static List<Action> GenerateMoves(Board board, ImmutableList<RecordedPly> plies, Side side)
    {
        var moves = new List<Action>();
        foreach (var (position, _) in board.AllPiecesOfSide(side))
        {
            foreach (var move in board.ValidMoves(plies, position, side))
            {
                moves.Add(move);
            }
        }
        return moves;
    }

    private static List<Action> GenerateCaptures(Board board, ImmutableList<RecordedPly> plies, Side side)
    {
        var captures = new List<Action>();
        foreach (var (position, _) in board.AllPiecesOfSide(side))
        {
            foreach (var move in board.ValidMoves(plies, position, side))
            {
                if (board[move.To].PieceType is not PieceType.None)
                    captures.Add(move);
            }
        }
        return captures;
    }

    /// <summary>
    /// MVV-LVA (Most Valuable Victim - Least Valuable Attacker) move ordering.
    /// Captures scored highest, then promotions.
    /// </summary>
    private static void OrderMoves(List<Action> moves, Board board)
    {
        moves.Sort((a, b) => ScoreMove(b, board).CompareTo(ScoreMove(a, board)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScoreMove(in Action move, Board board)
    {
        var score = 0;
        var victim = board[move.To];
        if (victim.PieceType is not PieceType.None)
        {
            // MVV-LVA: prioritize capturing high-value pieces with low-value attackers
            score += PieceValues[(int)victim.PieceType] * 10 - PieceValues[(int)board[move.From].PieceType];
        }

        if (move.Promoted is not PieceType.None)
            score += PieceValues[(int)move.Promoted];

        return score;
    }
}
