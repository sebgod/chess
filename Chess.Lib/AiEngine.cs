using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Chess.Lib;

/// <summary>
/// Chess engine using negamax search with alpha-beta pruning and quiescence search.
/// Evaluation uses centipawn piece values and piece-square tables.
/// </summary>
public sealed class AiEngine(Side side, int maxDepth = AiEngine.DefaultDepth, TimeProvider? timeProvider = null)
{
    public const int DefaultDepth = 4;

    /// <summary>
    /// Depth cap for a search bounded by the clock rather than by depth. Iterative deepening will
    /// never get near this without a transposition table — it exists so that "search until the time
    /// runs out" has a terminating upper bound rather than an infinite loop.
    /// </summary>
    public const int MaxSearchDepth = 64;

    public const int MateScore = 100_000;
    private const int InfiniteScore = MateScore + 1;

    /// <summary>
    /// How often the search consults the clock, in nodes. Must be a power of two — the check is a
    /// mask, not a modulo. Small enough that an abort lands within about a millisecond of the
    /// deadline, large enough to keep the timestamp read off the hot path.
    /// </summary>
    private const long NodesPerTimeCheck = 2048;

    /// <summary>
    /// Fraction of the budget past which a new iteration is not started. Each iteration costs several
    /// times the one before, so beginning one we cannot finish just burns clock we could have kept:
    /// the work is thrown away, since an aborted iteration's result is discarded.
    /// </summary>
    private const double NextIterationBudgetFraction = 0.5;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Per-search abort state, reset at the top of Search and only meaningful while one is in flight.
    private bool _abort;
    private bool _abortEnabled;
    private bool _hasBudget;
    private TimeSpan _budget;
    private long _searchStart;
    private CancellationToken _cancellationToken;

    // Depth of the iteration currently running, so mate scores can be expressed as plies from the
    // root (see Negamax).
    private int _rootDepth;

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
    /// True when the last search gave up early — because its move-time budget ran out or the caller
    /// cancelled it. The abandoned iteration's result is discarded either way, so this never shows up
    /// in <see cref="SearchResult"/>; it is for callers that want to report that the search was cut
    /// short rather than exhausted.
    /// </summary>
    public bool WasAborted => _abort;

    /// <summary>
    /// Picks the best move using iterative deepening negamax with alpha-beta pruning.
    /// </summary>
    /// <param name="game">Position to search from.</param>
    /// <param name="onDepthComplete">Invoked once per <em>completed</em> iteration; an aborted one is
    /// never reported, because its result is discarded.</param>
    /// <param name="moveTime">Wall-clock budget for the whole search. When supplied, the search stops
    /// as soon as it elapses and returns the best move from the deepest iteration that finished.
    /// <see cref="MaxDepth"/> still applies as an upper bound.</param>
    /// <param name="cancellationToken">Cooperative cancellation, checked on the same schedule as the
    /// clock. Cancelling is not an error: the best move found so far is returned.</param>
    public SearchResult Search(
        Game game,
        Action<SearchResult>? onDepthComplete = null,
        TimeSpan? moveTime = null,
        CancellationToken cancellationToken = default)
    {
        if (game.CurrentSide != Side || game.IsFinished)
            return new SearchResult(null, 0, 0, 0);

        NodesSearched = 0;
        _abort = false;
        // Depth 1 is exempt from aborting (enabled once it completes, below). That exemption is what
        // guarantees we return a legal move however tight the budget is, and it is cheap: one ply
        // plus quiescence.
        _abortEnabled = false;
        _hasBudget = moveTime is { } budget && budget > TimeSpan.Zero;
        _budget = _hasBudget ? moveTime!.Value : TimeSpan.Zero;
        _cancellationToken = cancellationToken;
        _searchStart = _timeProvider.GetTimestamp();

        SearchResult best = default;

        // Iterative deepening
        for (var depth = 1; depth <= MaxDepth; depth++)
        {
            var result = SearchRoot(game.Board, game.Plies, Side, depth);

            // An aborted iteration only searched a prefix of the root moves, so its "best" was never
            // compared against the rest of them. Keep the last iteration that finished instead.
            if (_abort)
                break;

            best = result;
            onDepthComplete?.Invoke(result);

            // Stop if we found a forced mate
            if (Math.Abs(result.Score) >= MateScore - 100)
                break;

            _abortEnabled = true;

            if (cancellationToken.IsCancellationRequested)
                break;

            if (_hasBudget && _timeProvider.GetElapsedTime(_searchStart) >= _budget * NextIterationBudgetFraction)
                break;
        }

        return best;
    }

    /// <summary>
    /// Cooperative abort check, called once per node. Reads the clock only every
    /// <see cref="NodesPerTimeCheck"/> nodes; in between it is a single bool test.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldAbort()
    {
        if (_abort)
            return true;

        if (!_abortEnabled || (NodesSearched & (NodesPerTimeCheck - 1)) != 0)
            return false;

        if (_cancellationToken.IsCancellationRequested ||
            (_hasBudget && _timeProvider.GetElapsedTime(_searchStart) >= _budget))
        {
            _abort = true;
        }

        return _abort;
    }

    /// <summary>
    /// Picks a legal move for the current board state, or returns <c>null</c> if no move is available.
    /// </summary>
    public Action? PickMove(Game game) => Search(game).BestMove;

    private SearchResult SearchRoot(Board board, ImmutableList<RecordedPly> plies, Side side, int depth)
    {
        _rootDepth = depth;

        var moves = GenerateMoves(board, plies, side);
        OrderMoves(moves, board);

        if (moves.Count == 0)
            return new SearchResult(null, board.IsCheck(side) ? -MateScore : 0, depth, NodesSearched);

        var bestScore = -InfiniteScore;
        Action? bestMove = null;

        foreach (var move in moves)
        {
            if (ShouldAbort())
                break;

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
        // Once aborted every node returns immediately, so the recursion unwinds fast. The value is
        // meaningless — Search discards the whole iteration.
        if (ShouldAbort())
            return alpha;

        if (depth <= 0)
            return Quiescence(board, plies, side, alpha, beta);

        var moves = GenerateMoves(board, plies, side);
        OrderMoves(moves, board);

        // Mate is scored by distance from the root so that a shorter mate outranks a longer one. The
        // root is _rootDepth plies up, not MaxDepth: under iterative deepening those differ on every
        // iteration but the last.
        if (moves.Count == 0)
            return board.IsCheck(side) ? -(MateScore - (_rootDepth - depth)) : 0;

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
        if (ShouldAbort())
            return alpha;

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
