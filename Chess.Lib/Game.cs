using System.Collections.Immutable;

namespace Chess.Lib;

public class Game
{
    private ImmutableList<RecordedPly> _plies = [];
    private readonly List<Board> _boardHistory = [];
    private Board _board;
    private Side _currentSide;
    private GameStatus _gameStatus;

    public Game()
    {
        _board = Board.StandardBoard;
        _currentSide = Side.White;
        _boardHistory.Add(_board);
    }

    public Game(Board board, Side side, ImmutableList<RecordedPly> plies)
    {
        _plies = plies;
        _board = board;
        _currentSide = side;
        _gameStatus = board.DetermineGameResult(plies, side);
        _boardHistory.Add(_board);
    }

    public static Game FromReplay(ImmutableList<RecordedPly> plies)
    {
        var game = new Game();

        var plyNo = 1;
        foreach (var ply in plies)
        {
            var result = game.TryMove(ply.From, ply.To);
            if (!result.IsMoveOrCapture())
            {
                throw new ArgumentException($"Could not apply ply #{plyNo} {ply} due to result {result}", nameof(plies));
            }

            plyNo++;
        }

        return game;
    }

    public Side CurrentSide => _currentSide;

    public Side Winner => IsFinished ? _currentSide : Side.None;

    public GameStatus GameStatus => _gameStatus;

    public bool IsFinished => _gameStatus is GameStatus.Checkmate or GameStatus.Stalemate;

    public bool HasValidMoves(Position position) => _board.ValidMoves(_plies, position, _currentSide).Any();

    /// <summary>
    /// A copy of the board
    /// </summary>
    public Board Board => _board;

    public ImmutableList<RecordedPly> Plies => _plies;

    public int PlyCount => _plies.Count;

    /// <summary>
    /// Returns the board state after the given ply index (0 = after first ply).
    /// Pass -1 to get the initial board before any moves.
    /// </summary>
    public Board BoardAtPly(int plyIndex) => _boardHistory[plyIndex + 1];

    public Piece this[in Position position] => _board[position];

    public ActionResult TryMove(in Position from, in Position to) => TryMove(new Action(from, to, IsMove: true));

    public ActionResult TryMove(in Action action)
    {
        if (IsFinished)
        {
            return ActionResult.Impossible;
        }

        if (this[action.From].Side != _currentSide || !action.IsMove)
        {
            return ActionResult.Impossible;
        }

        var ((result, status), board, plies) = _board.EvaluateAction(_plies, action);
        
        if (result.IsMoveOrCapture())
        {
            _board = board;
            _plies = plies;
            _boardHistory.Add(board);
            _gameStatus = status;

            if (status is GameStatus.Stalemate)
            {
                _currentSide = Side.None;
            }
            else if (status is not GameStatus.Checkmate)
            {
                _currentSide = _currentSide.ToOpposite();
            }
        }

        return result;
    }

    public Action? TryFindValidActionToPosition(in Position target)
    {
        var validMoves = new List<Action>();

        foreach (var (position, _) in _board.AllPiecesOfSide(_currentSide))
        {
            foreach (var move in _board.ValidMoves(_plies, position, _currentSide))
            {
                if (move.To == target)
                {
                    validMoves.Add(move);
                }
            }
        }

        return validMoves.Count is 1 ? validMoves[0] : null;
    }

    /// <summary>
    /// Places a piece at the given position during setup mode. Only allowed before the game has started.
    /// </summary>
    public void SetPiece(in Position position, Piece piece)
    {
        if (_plies.Count > 0)
            throw new InvalidOperationException("Cannot modify board after game has started.");
        _board[position] = piece;
    }

    /// <summary>
    /// Removes the piece at the given position during setup mode. Only allowed before the game has started.
    /// </summary>
    public void ClearPiece(in Position position)
    {
        if (_plies.Count > 0)
            throw new InvalidOperationException("Cannot modify board after game has started.");
        _board[position] = Piece.None;
    }

    public override string ToString() => $"{_board.ToFEN()} [{_plies.ToPGN()}] {_gameStatus.ToMessage(_currentSide)}";
}