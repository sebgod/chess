using System.Runtime.CompilerServices;

namespace Chess.Lib;

public enum ActionResult : byte
{
    Impossible,
    IllegalDueToInCheck,
    Attack,
    Move,
    EnPassant,
    Promotion,
    Capture,
    Castling,
    Cover,
    Control
}

public static class ActionResultExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsMoveOrCapture(this ActionResult result) => result.IsMove() || result.IsCapture();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsCapture(this ActionResult result) => result is ActionResult.Capture or ActionResult.EnPassant;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsMove(this ActionResult result) => result is ActionResult.Move or ActionResult.Castling or ActionResult.Promotion;
}
