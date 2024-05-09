using System;
using System.Collections.Immutable;

namespace Chess.Lib;

public class Game
{
    private ImmutableList<RecordedPly> _plies = [];
    private Board _board;
    private Side _currentSide;
    private GameStatus _gameStatus;

    public Game()
    {
        _board = Board.StandardBoard;
        _currentSide = Side.White;
    }

    internal Game(Board board, Side side, ImmutableList<RecordedPly> plies)
    {
        _plies = plies;
        _board = board;
        _currentSide = side;
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

    public override string ToString() => $"{_board.ToFEN()} [{_plies.ToPGN()}] {_gameStatus.ToMessage(_currentSide)}";
}