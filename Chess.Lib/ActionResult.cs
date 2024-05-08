using System.Runtime.CompilerServices;

namespace Chess.Lib;

public enum ActionResult : byte
{
    Impossible,
    IllegalDueToInCheck,
    NeedsPromotionType,
    Attack,
    Move,
    EnPassant,
    Promotion,
    Capture,
    CaptureAndPromotion,
    Castling,
    Cover,
    Control
}

public static class ActionResultExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsMoveOrCapture(this ActionResult result) => result.IsMove() || result.IsCapture();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsCapture(this ActionResult result) => result is ActionResult.Capture or ActionResult.EnPassant or ActionResult.CaptureAndPromotion;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsMove(this ActionResult result) => result is ActionResult.Move or ActionResult.Castling or ActionResult.Promotion;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool IsPromotion(this ActionResult result) => result is ActionResult.Promotion or ActionResult.CaptureAndPromotion;
}
