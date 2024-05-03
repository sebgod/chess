using System.Collections.Immutable;

namespace Chess.Lib;

public class Game
{
    private ImmutableList<RecordedPly> _plies = [];
    private Board _board;
    private Side _currentSide;
    private GameStatus _gameResult;

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
            if (!game.TryMove(ply.From, ply.To))
            {
                throw new ArgumentException($"Could not apply ply #{plyNo} {ply}", nameof(plies));
            }

            plyNo++;
        }

        return game;
    }

    public Side CurrentSide => _currentSide;

    /// <summary>
    /// A copy of the board
    /// </summary>
    public Board Board => _board;

    public ImmutableList<RecordedPly> Plies => _plies;

    public Piece this[in Position position] => _board[position];

    public bool TryMove(in Position from, in Position to) => TryMove(new Action(from, to, IsMove: true));

    public bool TryMove(in Action action)
    {
        if (_gameResult is not GameStatus.Ongoing)
        {
            return false;
        }

        if (this[action.From].Side != _currentSide || !action.IsMove)
        {
            return false;
        }

        var ((result, status), board, plies) = _board.EvaluateAction(_plies, action);
        
        if (result.IsMoveOrCapture())
        {
            _board = board;
            _plies = plies;

            if (status is not GameStatus.Ongoing)
            {
                _gameResult = status;
            }
            else
            {
                _currentSide = _currentSide.ToOpposite();
            }

            return true;
        }

        return false;
    }

    public override string ToString() => $"{_board} [{RecordedPly.ToPGN(_plies)}] {_gameResult.ToMessage(_currentSide)}";
}