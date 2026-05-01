using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using Chess.Lib;
using Chess.UCI;
using ModelContextProtocol.Server;

using Action = Chess.Lib.Action;

namespace Chess.MCP.Tools;

[McpServerToolType]
public class AnalysisTools
{
    [McpServerTool, Description("Find the best move for a position using the chess engine (negamax with alpha-beta pruning). Returns the best move, evaluation score, and search statistics.")]
    public static string FindBestMove(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Search depth (1-8, default 4). Higher values are stronger but slower.")] int depth = 4,
        [Description("Moves already played in UCI format, comma-separated. Empty string if none.")] string movesPlayed = "")
    {
        depth = Math.Clamp(depth, 1, 8);

        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = MoveTools.ApplyMoves(ref board, movesPlayed);

        var game = new Game(board, moveSide, plies);
        var engine = new AiEngine(moveSide, depth);

        var sb = new StringBuilder();
        var result = engine.Search(game, onDepthComplete: info =>
        {
            sb.AppendLine($"  depth {info.Depth}: score {BoardTools.FormatScore(info.Score)}, nodes {info.Nodes}");
        });

        var output = new StringBuilder();
        output.AppendLine($"Position: {board.ToFEN()}");
        output.AppendLine($"Side to move: {moveSide}");
        output.AppendLine($"Search depth: {depth}");
        output.AppendLine();
        output.AppendLine("Search progress:");
        output.Append(sb);
        output.AppendLine();

        if (result.BestMove is { } bestMove)
        {
            output.AppendLine($"Best move: {UciMove.Format(bestMove)}");
            output.AppendLine($"Score: {BoardTools.FormatScore(result.Score)}");
            output.AppendLine($"Nodes searched: {result.Nodes}");

            var piece = board[bestMove.From];
            var target = board[bestMove.To];
            output.Append($"Move description: {piece.Side} {piece.PieceType} from {bestMove.From} to {bestMove.To}");
            if (target != Piece.None)
                output.Append($" capturing {target.PieceType}");
            if (bestMove.Promoted != PieceType.None)
                output.Append($" promoting to {bestMove.Promoted}");
            output.AppendLine();
        }
        else
        {
            output.AppendLine("No legal moves available.");
            output.AppendLine($"Game status: {game.GameStatus}");
        }

        return output.ToString();
    }

    [McpServerTool, Description("Analyze a position to determine game status and find checks, threats, and tactical features.")]
    public static string AnalyzePosition(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Moves already played in UCI format, comma-separated. Empty string if none.")] string movesPlayed = "")
    {
        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = MoveTools.ApplyMoves(ref board, movesPlayed);

        var sb = new StringBuilder();
        sb.AppendLine($"Position: {board.ToFEN()}");
        sb.AppendLine($"Side to move: {moveSide}");
        sb.AppendLine();

        var status = board.DetermineGameResult(plies, moveSide);
        sb.AppendLine($"Game Status: {status}");

        if (board.IsCheck(moveSide))
            sb.AppendLine($"*** {moveSide} King is IN CHECK ***");

        sb.AppendLine();

        // Count material
        sb.AppendLine("Material:");
        AppendMaterial(sb, board, Side.White);
        AppendMaterial(sb, board, Side.Black);
        sb.AppendLine();

        // Evaluation
        var whiteScore = AiEngine.Evaluate(board, Side.White);
        var blackScore = AiEngine.Evaluate(board, Side.Black);
        sb.AppendLine($"Evaluation (White): {BoardTools.FormatScore(whiteScore)}");
        sb.AppendLine($"Evaluation (Black): {BoardTools.FormatScore(blackScore)}");
        sb.AppendLine();

        // Move counts
        var whiteMoves = CountMoves(board, plies, Side.White);
        var blackMoves = CountMoves(board, plies, Side.Black);
        sb.AppendLine($"Legal moves available - White: {whiteMoves}, Black: {blackMoves}");

        // Captures available for side to move
        var captures = FindCaptures(board, plies, moveSide);
        if (captures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Available captures for {moveSide}:");
            foreach (var (from, piece, target, move) in captures)
            {
                sb.AppendLine($"  {UciMove.Format(move)}: {piece.PieceType}({from}) x {target.PieceType}({move.To})");
            }
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Solve a chess problem by searching for a forced mate. Returns the mating sequence if found.")]
    public static string SolveMateIn(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Maximum number of moves to search for mate (1-5). Mate in 1 searches depth 2, mate in 2 searches depth 4, etc.")] int mateIn = 2,
        [Description("Moves already played in UCI format, comma-separated. Empty string if none.")] string movesPlayed = "")
    {
        mateIn = Math.Clamp(mateIn, 1, 5);
        var searchDepth = mateIn * 2;

        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = MoveTools.ApplyMoves(ref board, movesPlayed);

        var game = new Game(board, moveSide, plies);
        var engine = new AiEngine(moveSide, searchDepth);

        var result = engine.Search(game);

        var sb = new StringBuilder();
        sb.AppendLine($"Searching for mate in {mateIn} for {moveSide}...");
        sb.AppendLine($"Position: {board.ToFEN()}");
        sb.AppendLine($"Search depth: {searchDepth} ply");
        sb.AppendLine($"Nodes searched: {result.Nodes}");
        sb.AppendLine();

        if (result.BestMove is null)
        {
            sb.AppendLine("No moves available - game is already over.");
            return sb.ToString();
        }

        if (result.Score >= AiEngine.MateScore - searchDepth)
        {
            var actualMateIn = (AiEngine.MateScore - result.Score + 1) / 2;
            sb.AppendLine($"*** MATE FOUND in {actualMateIn} move(s)! ***");
            sb.AppendLine();

            // Play out the mating sequence
            sb.AppendLine("Mating sequence:");
            var currentGame = new Game(board, moveSide, plies);
            var currentSide = moveSide;
            var moveNum = 1;

            for (var i = 0; i < searchDepth && !currentGame.IsFinished; i++)
            {
                var moveEngine = new AiEngine(currentSide, searchDepth - i);
                var moveResult = moveEngine.Search(currentGame);

                if (moveResult.BestMove is not { } nextMove)
                    break;

                var moveStr = UciMove.Format(nextMove);
                var prefix = currentSide == moveSide
                    ? $"  {moveNum}. {moveStr}"
                    : $"  {moveNum}... {moveStr}";

                currentGame.TryMove(nextMove);
                sb.AppendLine($"{prefix} ({currentGame.GameStatus})");

                if (currentSide != moveSide)
                    moveNum++;
                currentSide = currentSide.ToOpposite();
            }

            if (currentGame.GameStatus == GameStatus.Checkmate)
                sb.AppendLine($"\n  Checkmate! {moveSide} wins.");
        }
        else
        {
            sb.AppendLine($"No forced mate in {mateIn} found.");
            sb.AppendLine($"Best move: {UciMove.Format(result.BestMove.Value)}");
            sb.AppendLine($"Score: {BoardTools.FormatScore(result.Score)}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Play a complete game or sequence of moves and return the final position with game state.")]
    public static string PlayMoves(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side that plays the first move: 'white' or 'black'")] string startingSide,
        [Description("Moves to play in UCI format, comma-separated (e.g. 'e2e4,e7e5,g1f3')")] string moves)
    {
        var board = BoardTools.ParseBoard(fen);
        var currentSide = BoardTools.ParseSide(startingSide);
        var plies = ImmutableList<RecordedPly>.Empty;

        var moveList = moves.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();

        sb.AppendLine($"Starting position: {board.ToFEN()}");
        sb.AppendLine($"Playing {moveList.Length} move(s):");
        sb.AppendLine();

        foreach (var moveStr in moveList)
        {
            var action = UciMove.Parse(moveStr);
            var (evalResult, newBoard, newPlies) = board.EvaluateAction(plies, action);

            if (!evalResult.Result.IsMoveOrCapture())
            {
                sb.AppendLine($"  ILLEGAL: {moveStr} - {evalResult.Result}");
                sb.AppendLine($"\nStopped at illegal move. Current FEN: {board.ToFEN()}");
                return sb.ToString();
            }

            var piece = board[action.From];
            sb.AppendLine($"  {moveStr}: {piece.PieceType} {evalResult.Result}{(evalResult.Status != GameStatus.Ongoing ? $" ({evalResult.Status})" : "")}");

            board = newBoard;
            plies = newPlies;
            currentSide = currentSide.ToOpposite();
        }

        sb.AppendLine();
        sb.AppendLine("Final position:");
        sb.AppendLine(board.ToString());
        sb.AppendLine();
        sb.AppendLine($"FEN: {board.ToFEN()}");
        sb.AppendLine($"Side to move: {currentSide}");

        var finalStatus = board.DetermineGameResult(plies, currentSide);
        sb.AppendLine($"Status: {finalStatus}");

        if (plies.Count > 0)
            sb.AppendLine($"PGN: {plies.ToPGN()}");

        return sb.ToString();
    }

    private static void AppendMaterial(StringBuilder sb, Board board, Side side)
    {
        var pieces = board.AllPiecesOfSide(side).Select(p => p.Item2.PieceType).ToList();
        var counts = pieces.GroupBy(p => p).OrderByDescending(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        sb.AppendLine($"  {side}: {string.Join(", ", counts)}");
    }

    private static int CountMoves(Board board, ImmutableList<RecordedPly> plies, Side side)
    {
        var count = 0;
        foreach (var (pos, _) in board.AllPiecesOfSide(side))
        {
            count += board.ValidMoves(plies, pos, side).Count();
        }
        return count;
    }

    private static List<(Position From, Piece Piece, Piece Target, Action Move)> FindCaptures(
        Board board, ImmutableList<RecordedPly> plies, Side side)
    {
        var captures = new List<(Position, Piece, Piece, Action)>();
        foreach (var (pos, piece) in board.AllPiecesOfSide(side))
        {
            foreach (var move in board.ValidMoves(plies, pos, side))
            {
                var target = board[move.To];
                if (target != Piece.None && target.Side != side)
                {
                    captures.Add((pos, piece, target, move));
                }
            }
        }
        return captures.OrderByDescending(c => c.Item3.PieceType).ToList();
    }
}
