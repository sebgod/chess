using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chess.Lib;

public record struct Board()
{
    // board ranks
    private uint _r1;
    private uint _r2;
    private uint _r3;
    private uint _r4;
    private uint _r5;
    private uint _r6;
    private uint _r7;
    private uint _r8;

    private static readonly ImmutableArray<Side> Sides = [Side.White, Side.Black];
    private static readonly IReadOnlyDictionary<File, PieceType> HomeRankPieceTypes = new SortedDictionary<File, PieceType>() {
        { File.A, PieceType.Rook },
        { File.B, PieceType.Knight },
        { File.C, PieceType.Bishop },
        { File.D, PieceType.Queen },
        { File.E, PieceType.King },
        { File.F, PieceType.Bishop },
        { File.G, PieceType.Knight },
        { File.H, PieceType.Rook }
    };
    private static readonly ImmutableArray<File> CastlingKingSideFiles  = [File.E, File.F, File.G];
    private static readonly ImmutableArray<File> CastlingQueenSideFiles = [File.C, File.D, File.E];

    private static readonly Board _standardGameBoard = BuildStandardGame();
    private static Board BuildStandardGame()
    {
        var board = new Board();
        foreach (var side in Sides)
        {
            var homeRank = side.HomeRank();
            var pawnRank = side.PawnRank();

            foreach (var (file, pieceType) in HomeRankPieceTypes)
            {
                board[new Position(file, homeRank)] = new Piece(pieceType, side);
                board[new Position(file, pawnRank)] = new Piece(PieceType.Pawn, side);
            }
        }

        return board;
    }

    public static Board StandardBoard => _standardGameBoard;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public readonly (EvaluationResult Result, Board Board, ImmutableList<RecordedPly> Plies) EvaluateAction(ImmutableList<RecordedPly> plies, Action action, bool skipGameResultCheck = false)
    {
        if (action.From == action.To)
        {
            return (ActionResult.Impossible, this, plies);
        }

        var pieceFrom    = this[action.From];
        var pieceTo      = this[action.To];
        
        if (pieceFrom == Piece.None)
        {
            return (ActionResult.Impossible, this, plies);
        }
        
        var side = pieceFrom.Side;
        var oppositeSide = side.ToOpposite();

        var delta = action.Delta;
        var isDiagnoal = delta.IsDiagnoal;
        var isStraight = delta.IsStraight;
        var isCastling = isStraight && Math.Abs(delta.File) == 2 && pieceFrom.PieceType is PieceType.King;
        var isKingSideCastling = delta.File > 0;

        // check if nothing is blocking movement
        if (pieceFrom.PieceType is not PieceType.Knight)
        {
            foreach (var position in action.Between())
            {
                if (this[position] != Piece.None)
                {
                    return (ActionResult.Impossible, this, plies);
                }
            }
        }

        ActionResult result;
        if (pieceFrom.PieceType is PieceType.Pawn)
        {
            var isFromPawnRank = action.From.Rank == side.PawnRank();
            var absPawnMove = delta.Rank * side.PawnDirection();
            var isValidPawnForwardMove = isStraight
                && action.IsMove
                && pieceTo == Piece.None
                && (absPawnMove == 1 || (absPawnMove == 2 && isFromPawnRank));
            var isValidPawnCapture = isDiagnoal && absPawnMove == 1;

            if (isValidPawnForwardMove && action.To.Rank == oppositeSide.HomeRank())
            {
                result = action.Promoted.IsValidPromotion() ? ActionResult.Promotion : ActionResult.NeedsPromotionType;
            }
            else if (isValidPawnForwardMove)
            {
                result = ActionResult.Move;
            }
            else if (isValidPawnCapture)
            {
                result = PossibleActionResult(plies, pieceFrom, pieceTo, action);
            }
            else
            {
                result = ActionResult.Impossible;
            }
        }
        else if (isCastling)
        {
            result = ValidateCastling(plies, pieceFrom, isKingSideCastling) ? ActionResult.Castling : ActionResult.Impossible;
        }
        else
        {
            var isPossible = pieceFrom.PieceType switch
            {
                PieceType.King   => delta is { AbsFile: <= 1, AbsRank: <= 1 },
                PieceType.Queen  => isDiagnoal || isStraight,
                PieceType.Bishop => isDiagnoal,
                PieceType.Knight => delta.IsLShape,
                PieceType.Rook   => isStraight,
                _ => false
            };

            result = isPossible ? PossibleActionResult(plies, pieceFrom, pieceTo, action) : ActionResult.Impossible;
        }

        if (action.IsMove && result.IsMoveOrCapture())
        {
            var board = this;
            board[action.From] = Piece.None;
            board[action.To] = result.IsPromotion() ? new Piece(action.Promoted, side) : pieceFrom;

            (Board Board, RecordedPly Ply) afterMove;
            if (result is ActionResult.EnPassant)
            {
                var takenPawnPosition = action.To.AdvanceInPawnDirection(oppositeSide);
                board[takenPawnPosition] = Piece.None;
                afterMove = (board, new RecordedPly(action, result, pieceFrom, this[takenPawnPosition]));
            }
            else if (result is ActionResult.Castling)
            {
                // castling includes build-in Check constraint
                var homeRank = side.HomeRank();
                board[(isKingSideCastling ? File.H : File.A, homeRank)] = Piece.None;
                board[(isKingSideCastling ? File.F : File.D, homeRank)] = (side, PieceType.Rook);
                afterMove = (board, new RecordedPly(action, ActionResult.Castling, pieceFrom, Piece.None));
            }
            else
            {
                var captured = result.IsCapture() ? this[action.To] : Piece.None;
                var promoted = result.IsPromotion() ? action.Promoted : PieceType.None;
                afterMove = (board, new RecordedPly(action, result, pieceFrom, captured, promoted));
            }

            var afterMovePlies = plies.Add(afterMove.Ply);
            var gameStatus = skipGameResultCheck ? GameStatus.Ongoing : board.DetermineGameResult(afterMovePlies, oppositeSide);

            if (board.IsCheck(side))
            {
                return (ActionResult.IllegalDueToInCheck, this, plies);
            }
            else if (gameStatus != afterMove.Ply.Status)
            {
                return ((afterMove.Ply.Result, gameStatus), board, plies.Add(afterMove.Ply with { Status = gameStatus}));
            }
            else
            {
                return ((afterMove.Ply.Result, gameStatus), board, afterMovePlies);
            }
        }
        else
        {
            return (result, this, plies);
        }
    }

    private readonly ActionResult PossibleActionResult(ImmutableList<RecordedPly> plies, in Piece pieceFrom, in Piece pieceTo, Action action)
    {
        if (pieceTo == Piece.None)
        {
            if (pieceFrom.PieceType is PieceType.Pawn && action.IsMove)
            {
                return ValidateEnPassant(plies, action) ? ActionResult.EnPassant : ActionResult.Impossible;
            }
            else
            {
                return action.IsMove ? ActionResult.Move : ActionResult.Control;
            }
        }
        else if (pieceFrom.Side == pieceTo.Side)
        {
            return ActionResult.Cover;
        }
        else if (action.IsMove && pieceTo.PieceType is PieceType.King)
        {
            return ActionResult.Impossible;
        }
        else if (action.IsMove && action.To.Rank == pieceTo.Side.HomeRank())
        {
            return action.Promoted.IsValidPromotion() ? ActionResult.CaptureAndPromotion : ActionResult.NeedsPromotionType;
        }
        else if (action.IsMove)
        {
            return ActionResult.Capture;
        }
        else
        {
            return ActionResult.Attack;
        }
    }

    private readonly bool ValidateCastling(ImmutableList<RecordedPly> plies, Piece pieceFrom, bool isKingSideCastling)
    {
        if (pieceFrom.PieceType is not PieceType.King)
        {
            return false;
        }

        var side = pieceFrom.Side;
        for (var i = side is Side.White ? 0 : 1; i < plies.Count; i += 2)
        {
            var ply = plies[i];
            if (ply is { Moved: PieceType.King })
            {
                return false;
            }
            else if (isKingSideCastling && ply is { Moved: PieceType.Rook, From.File: File.H })
            {
                return false;
            }
            else if (!isKingSideCastling && ply is { Moved: PieceType.Rook, From.File: File.A })
            {
                return false;
            }
        }

        var filesToCheck = isKingSideCastling ? CastlingKingSideFiles : CastlingQueenSideFiles;
        var homeRank = side.HomeRank();
        var oppositeSide = side.ToOpposite();
        var oppositeSidePieces = AllPiecesOfSide(oppositeSide).ToList();

        foreach (var file in filesToCheck)
        {
            var kingMovePosition = new Position(file, homeRank);
            if (file is not File.E && this[kingMovePosition] != Piece.None)
            {
                return false;
            }

            foreach (var (piecePosition, _) in oppositeSidePieces)
            {
                var ((result, _), _, _) = EvaluateAction(plies, new Action(piecePosition, kingMovePosition, IsMove: false), skipGameResultCheck: true);
                if (result is ActionResult.Control)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public readonly IEnumerable<Action> ValidMoves(ImmutableList<RecordedPly> plies, Position position, Side side)
    {
        var piece = this[position];
        if (piece == Piece.None || piece.Side != side)
        {
            yield break;
        }

        var oppositeHomeRank = side.ToOpposite().HomeRank();
        foreach (var to in Position.AllPossibleActions(position, piece))
        {
            var action = piece is { PieceType: PieceType.Pawn } && to.Rank == oppositeHomeRank
                ? Action.Promote(position, to, PieceType.Queen)
                : Action.DoMove(position, to);
            var ((result, _), _, _) = EvaluateAction(plies, action, skipGameResultCheck: true);
            if (result.IsMoveOrCapture())
            {
                yield return action;
            }
        }
    }

    public readonly bool IsCheck(Side side)
    {
        if (KingPosition(side) is { } kingPosition)
        {
            var oppositeSide = side.ToOpposite();
            foreach (var (position, _) in AllPiecesOfSide(oppositeSide))
            {
                var ((result, _), _, _) = EvaluateAction([], new Action(position, kingPosition, IsMove: false), skipGameResultCheck: true);
                if (result is ActionResult.Attack)
                {
                    return true;
                }
            }

        }

        return false;
    }

    public readonly GameStatus DetermineGameResult(ImmutableList<RecordedPly> plies, Side side)
    {
        var isCheck = IsCheck(side);
        // find a legal move, otherwise checkmate
        foreach (var (from, piece) in AllPiecesOfSide(side))
        {
            foreach (var to in Position.AllPossibleActions(from, piece))
            {
                var ((result, _), _, _) = EvaluateAction(plies, Action.DoMove(from, to), skipGameResultCheck: true);
                if (result is not ActionResult.Impossible and not ActionResult.IllegalDueToInCheck and not ActionResult.Cover and not ActionResult.NeedsPromotionType)
                {
                    return isCheck ? GameStatus.Check : GameStatus.Ongoing;
                }
            }
        }

        return isCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
    }

    /// <summary>
    /// Assumes basic check for proper pawn take movement has been done (diagonal, distance = 1)
    /// </summary>
    /// <param name="plies"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    private readonly bool ValidateEnPassant(ImmutableList<RecordedPly> plies, in Action action)
    {
        var pieceFrom = this[action.From];
        var pieceTo   = this[action.To];

        if (pieceFrom.PieceType != PieceType.Pawn || pieceTo != Piece.None)
        {
            return false;
        }

        var side = pieceFrom.Side;
        var count = plies.Count;

        if (count == 0)
        {
            return false;
        }
        // last pawn move must be from the opposite colour, so all even plies are white and odd are black
        else if ((count % 2) != (side is Side.White ? 0 : 1))
        {
            return false;
        }

        var oppositeSide = side.ToOpposite();
        var capturedPawnPosition = action.To.AdvanceInPawnDirection(oppositeSide);

        var capturedIsFromOppositeSide = this[capturedPawnPosition] is { PieceType: PieceType.Pawn } capturedPawn
            && capturedPawn.Side == oppositeSide;

        var lastMoveWasTwoRanksFromHomeRank =
            plies[count - 1] is { Moved: PieceType.Pawn, Result: ActionResult.Move, Action.Delta: { AbsRank: 2, File: 0 } } lastPly
            && lastPly.To == capturedPawnPosition;

        return capturedIsFromOppositeSide && lastMoveWasTwoRanksFromHomeRank;
    }

    public Piece this[in Position position]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        [DebuggerStepThrough]
        readonly get
        {
            var rank = GetRankValue(position.Rank);
            ExtractPieceAndSide(rank, position.File, out Side side, out PieceType pieceType);

            return new Piece(pieceType, pieceType != 0 ? side : Side.None);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        [DebuggerStepThrough]
        internal set
        {
            var bits = ((uint)value.PieceType & 0b1111u) << 1 | (value.Side == Side.White ? (byte)0b1 : (byte)0b0);
            var shift = ((int)position.File * 4);
            _ = SetRankValue(position.Rank, (GetRankValue(position.Rank) & ~(0b1111u << shift)) | (bits << shift));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [DebuggerStepThrough]
    private static void ExtractPieceAndSide(uint rank, File file, out Side side, out PieceType pieceType)
    {
        var bits = (rank >> ((int)file * 4)) & 0b1111;
        side = (bits & 0b1) == 0b1 ? Side.White : Side.Black;
        pieceType = (PieceType)(bits >> 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [DebuggerStepThrough]
    private readonly uint GetRankValue(Rank rank) => rank switch
    {
        Rank.R1 => _r1,
        Rank.R2 => _r2,
        Rank.R3 => _r3,
        Rank.R4 => _r4,
        Rank.R5 => _r5,
        Rank.R6 => _r6,
        Rank.R7 => _r7,
        Rank.R8 => _r8,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank can only be between 1 and 8 inclusive")
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [DebuggerStepThrough]
    private uint SetRankValue(Rank rank, uint value) => rank switch
    {
        Rank.R1 => _r1 = value,
        Rank.R2 => _r2 = value,
        Rank.R3 => _r3 = value,
        Rank.R4 => _r4 = value,
        Rank.R5 => _r5 = value,
        Rank.R6 => _r6 = value,
        Rank.R7 => _r7 = value,
        Rank.R8 => _r8 = value,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, "Rank can only be between 1 and 8 inclusive")
    };

    public readonly IEnumerable<(Position Position, Piece Piece)> AllPiecesOfSide(Side side)
    {
        foreach (var rank in Position.AllRanks)
        {
            var rankValue = GetRankValue(rank);
            if (rankValue == 0u)
            {
                continue;
            }

            foreach (var file in Position.AllFiles)
            {
                ExtractPieceAndSide(rankValue, file, out Side pieceSide, out var pieceType);
                if (pieceSide == side)
                {
                    yield return (new Position(file, rank), new Piece(pieceType, side));
                }
            }
        }
    }

    public readonly Position? KingPosition(Side side)
    {
        foreach (var (position, piece) in AllPiecesOfSide(side))
        {
            if (piece.PieceType is PieceType.King)
            {
                return position;
            }
        }

        return default;
    }

    public override readonly string ToString()
    {
        var sb = new StringBuilder(200);

        for (byte rankIdx = 0; rankIdx < 8; rankIdx++)
        {
            var rank = (Rank)(7 - rankIdx);
            var shownRank = false;
            for (byte fileIdx = 0; fileIdx < 8; fileIdx++)
            {
                var file = (File)fileIdx;
                var piece = this[(file, rank)];
                if (piece != Piece.None)
                {
                    if (!shownRank)
                    {
                        sb.AppendFormat(" {0}: ", rank.ToLabel());
                        shownRank = true;
                    }
                    sb.Append(file.ToLabel()).Append(piece.PieceType.ToUnicode(piece.Side));
                } 
            }
        }

        return sb.ToString().Trim();
    }

    public static Board operator +(Board board, Action action)
    {
        if (!action.IsMove)
        {
            throw new ArgumentException("Only moves can be added to boards", nameof(action));
        }

        var @new = board;
        var existing = @new[action.From];
        @new[action.From] = Piece.None;
        @new[action.To] = existing;

        return @new;
    }

    public static Board operator -(Board board, Position action)
    {
        var @new = board;
        @new[action] = Piece.None;

        return @new;
    }
}
