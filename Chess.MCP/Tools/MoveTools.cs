using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using Chess.Lib;
using Chess.UCI;
using ModelContextProtocol.Server;

using Action = Chess.Lib.Action;

namespace Chess.MCP.Tools;

[McpServerToolType]
public class MoveTools
{
    [McpServerTool, Description("Get all legal moves for a specific piece on the board. Returns moves in UCI format.")]
    public static string GetLegalMovesForPiece(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Square of the piece to get moves for (e.g. 'e2')")] string square,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Moves already played in UCI format, comma-separated (e.g. 'e2e4,e7e5'). Empty string if none.")] string movesPlayed = "")
    {
        var board = BoardTools.ParseBoard(fen);
        var position = BoardTools.ParsePosition(square);
        var moveSide = BoardTools.ParseSide(side);
        var plies = ApplyMoves(ref board, movesPlayed);

        var piece = board[position];
        if (piece == Piece.None)
            return $"No piece at {square}.";

        if (piece.Side != moveSide)
            return $"Piece at {square} is {piece.Side} {piece.PieceType}, but it's {moveSide}'s turn.";

        var moves = board.ValidMoves(plies, position, moveSide).ToList();

        if (moves.Count == 0)
            return $"No legal moves for {piece.Side} {piece.PieceType} at {square}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Legal moves for {piece.Side} {piece.PieceType} at {square} ({moves.Count} moves):");
        foreach (var move in moves)
        {
            var target = board[move.To];
            var desc = target != Piece.None ? $" (captures {target.Side} {target.PieceType})" : "";
            if (move.Promoted != PieceType.None)
                desc += $" (promotes to {move.Promoted})";
            sb.AppendLine($"  {UciMove.Format(move)}{desc}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Get all legal moves for a side in the current position. Returns moves in UCI format.")]
    public static string GetAllLegalMoves(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Moves already played in UCI format, comma-separated (e.g. 'e2e4,e7e5'). Empty string if none.")] string movesPlayed = "")
    {
        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = ApplyMoves(ref board, movesPlayed);

        var allMoves = new List<(Position From, Piece Piece, Action Move)>();
        foreach (var (pos, piece) in board.AllPiecesOfSide(moveSide))
        {
            foreach (var move in board.ValidMoves(plies, pos, moveSide))
            {
                allMoves.Add((pos, piece, move));
            }
        }

        if (allMoves.Count == 0)
        {
            var status = board.DetermineGameResult(plies, moveSide);
            return $"No legal moves. Game status: {status}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Legal moves for {moveSide} ({allMoves.Count} total):");

        var grouped = allMoves.GroupBy(m => m.From);
        foreach (var group in grouped)
        {
            var piece = group.First().Piece;
            sb.Append($"  {piece.PieceType}({group.Key}): ");
            sb.AppendLine(string.Join(", ", group.Select(m => UciMove.Format(m.Move))));
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Apply a move to a board position. Returns the resulting board state and move validation result.")]
    public static string MakeMove(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Move in UCI format (e.g. 'e2e4', 'e7e8q' for promotion)")] string move,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Moves already played in UCI format, comma-separated. Empty string if none.")] string movesPlayed = "")
    {
        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = ApplyMoves(ref board, movesPlayed);

        var action = UciMove.Parse(move);
        var (evalResult, newBoard, newPlies) = board.EvaluateAction(plies, action);

        var sb = new StringBuilder();
        sb.AppendLine($"Move: {move}");
        sb.AppendLine($"Result: {evalResult.Result}");
        sb.AppendLine($"Game Status: {evalResult.Status}");
        sb.AppendLine();

        if (evalResult.Result.IsMoveOrCapture())
        {
            sb.AppendLine(newBoard.ToString());
            sb.AppendLine();
            sb.AppendLine($"FEN: {newBoard.ToFEN()}");

            if (newPlies.Count > 0)
            {
                sb.AppendLine($"PGN: {newPlies.ToPGN()}");
            }
        }
        else
        {
            sb.AppendLine($"Move is not legal: {evalResult.Result}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Validate whether a move is legal in the given position.")]
    public static string ValidateMove(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Move in UCI format (e.g. 'e2e4')")] string move,
        [Description("Side to move: 'white' or 'black'")] string side,
        [Description("Moves already played in UCI format, comma-separated. Empty string if none.")] string movesPlayed = "")
    {
        var board = BoardTools.ParseBoard(fen);
        var moveSide = BoardTools.ParseSide(side);
        var plies = ApplyMoves(ref board, movesPlayed);

        var action = UciMove.Parse(move);
        var (evalResult, _, _) = board.EvaluateAction(plies, action);

        return evalResult.Result.IsMoveOrCapture()
            ? $"Move {move} is legal. Result: {evalResult.Result}, Status after: {evalResult.Status}"
            : $"Move {move} is NOT legal. Reason: {evalResult.Result}";
    }

    internal static ImmutableList<RecordedPly> ApplyMoves(ref Board board, string movesPlayed)
    {
        var plies = ImmutableList<RecordedPly>.Empty;

        if (string.IsNullOrWhiteSpace(movesPlayed))
            return plies;

        var moves = movesPlayed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var moveStr in moves)
        {
            var action = UciMove.Parse(moveStr);
            var (evalResult, newBoard, newPlies) = board.EvaluateAction(plies, action);
            if (evalResult.Result.IsMoveOrCapture())
            {
                board = newBoard;
                plies = newPlies;
            }
            else
            {
                throw new InvalidOperationException($"Failed to apply move '{moveStr}': {evalResult.Result}");
            }
        }

        return plies;
    }
}
