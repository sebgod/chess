using System.ComponentModel;
using System.Text;
using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using DIR.Lib;
using ModelContextProtocol.Server;
using StbImageWriteSharp;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.MCP.Tools;

[McpServerToolType]
public class BoardTools
{
    [McpServerTool, Description("Display the current state of a chess board from a FEN placement string. Returns a visual Unicode representation and piece list.")]
    public static string DisplayBoard(
        [Description("FEN placement string (e.g. 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR' for starting position). Use 'startpos' for the standard starting position.")] string fen)
    {
        var board = ParseBoard(fen);
        var sb = new StringBuilder();

        sb.AppendLine(board.ToString());
        sb.AppendLine();
        sb.AppendLine($"FEN: {board.ToFEN()}");
        sb.AppendLine();

        AppendPieceList(sb, board, Side.White);
        AppendPieceList(sb, board, Side.Black);

        return sb.ToString();
    }

    [McpServerTool, Description("Render a board as PNG and return a base64 data URL. Uses DIR RGBA rendering for board drawing.")]
    public static string RenderBoardPng(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Image size in pixels (default: 480)")] uint size = 480,
        [Description("Optional UCI move arrow overlay (e.g. 'e2e4')")] string? move = null)
    {
        var board = ParseBoard(fen);
        var game = new Game(board, Side.White, []);

        using var renderer = new RgbaImageRenderer(size, size);
        var ui = new GameUI(game, size, size,
            mainFontColor: new RGBAColor32(0xff, 0xff, 0xff, 0xff),
            backgroundColor: new RGBAColor32(0x00, 0x00, 0x00, 0xff));

        if (!string.IsNullOrWhiteSpace(move))
        {
            var action = UciMove.Parse(move);
            var targetPiece = board[action.To];
            ui.ExplicitArrow = (action.From, action.To, targetPiece != Piece.None);
        }

        var clip = new RectInt(((int)size, (int)size), PointInt.Origin);
        ui.Render<RgbaImage, RgbaImageRenderer>(renderer, clip);

        using var output = new MemoryStream();
        var writer = new ImageWriter();
        writer.WritePng(renderer.Surface.Pixels, renderer.Surface.Width, renderer.Surface.Height, ColorComponents.RedGreenBlueAlpha, output);

        if (output.TryGetBuffer(out var buffer) && buffer.Array is not null)
        {
            return $"data:image/png;base64,{Convert.ToBase64String(buffer.Array, buffer.Offset, buffer.Count)}";
        }

        return $"data:image/png;base64,{Convert.ToBase64String(output.ToArray())}";
    }

    [McpServerTool, Description("Get the piece at a specific square on the board.")]
    public static string GetPieceAt(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Square in algebraic notation (e.g. 'e4', 'a1')")] string square)
    {
        var board = ParseBoard(fen);
        var position = ParsePosition(square);
        var piece = board[position];

        if (piece == Piece.None)
            return $"Square {square} is empty.";

        return $"Square {square}: {piece.Side} {piece.PieceType} ({piece.ToFEN()})";
    }

    [McpServerTool, Description("Evaluate a board position statically. Returns the material and positional score in centipawns from the perspective of the specified side.")]
    public static string EvaluatePosition(
        [Description("FEN placement string or 'startpos'")] string fen,
        [Description("Side to evaluate for: 'white' or 'black' (default: 'white')")] string side = "white")
    {
        var board = ParseBoard(fen);
        var evalSide = ParseSide(side);
        var score = AiEngine.Evaluate(board, evalSide);

        var sb = new StringBuilder();
        sb.AppendLine($"Evaluation for {evalSide}: {FormatScore(score)}");
        sb.AppendLine();
        sb.AppendLine("Piece values: Pawn=100, Knight=320, Bishop=330, Rook=500, Queen=900");
        sb.AppendLine("Score includes piece-square table positional bonuses.");

        return sb.ToString();
    }

    [McpServerTool, Description("Convert a board position to FEN placement notation.")]
    public static string ToFen(
        [Description("FEN placement string or 'startpos'")] string fen)
    {
        var board = ParseBoard(fen);
        return board.ToFEN();
    }

    internal static Board ParseBoard(string fen)
    {
        if (string.Equals(fen, "startpos", StringComparison.OrdinalIgnoreCase))
            return Board.StandardBoard;

        return Board.FromFenPlacement(fen);
    }

    internal static Position ParsePosition(string square)
    {
        if (square.Length != 2)
            throw new ArgumentException($"Invalid square notation: '{square}'. Expected format like 'e4'.");

        var file = square[0] switch
        {
            >= 'a' and <= 'h' => (File)(square[0] - 'a'),
            _ => throw new ArgumentException($"Invalid file: '{square[0]}'. Must be a-h.")
        };

        var rank = square[1] switch
        {
            >= '1' and <= '8' => (Rank)(square[1] - '1'),
            _ => throw new ArgumentException($"Invalid rank: '{square[1]}'. Must be 1-8.")
        };

        return new Position(file, rank);
    }

    internal static Side ParseSide(string side) => side.ToLowerInvariant() switch
    {
        "white" or "w" => Side.White,
        "black" or "b" => Side.Black,
        _ => throw new ArgumentException($"Invalid side: '{side}'. Use 'white' or 'black'.")
    };

    internal static string FormatScore(int centipawns)
    {
        if (Math.Abs(centipawns) >= AiEngine.MateScore - 100)
        {
            var mateIn = (AiEngine.MateScore - Math.Abs(centipawns) + 1) / 2;
            return centipawns > 0 ? $"Mate in {mateIn}" : $"Mated in {mateIn}";
        }

        var sign = centipawns >= 0 ? "+" : "";
        return $"{sign}{centipawns / 100.0:F2} pawns ({centipawns} centipawns)";
    }

    private static void AppendPieceList(StringBuilder sb, Board board, Side side)
    {
        sb.Append($"{side}: ");
        var pieces = board.AllPiecesOfSide(side)
            .OrderByDescending(p => p.Item2.PieceType)
            .Select(p => $"{p.Item2.PieceType}({p.Item1})")
            .ToList();
        sb.AppendLine(string.Join(", ", pieces));
    }
}
