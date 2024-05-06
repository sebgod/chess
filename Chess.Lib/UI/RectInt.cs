namespace Chess.Lib.UI;

public readonly record struct RectInt((int X, int Y) LowerRight, (int X, int Y) UpperLeft);
