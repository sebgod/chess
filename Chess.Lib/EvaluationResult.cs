namespace Chess.Lib;

public readonly record struct EvaluationResult(ActionResult Result, GameStatus Status)
{
    public static implicit operator EvaluationResult(ActionResult result) => new EvaluationResult(result, default);

    public static implicit operator EvaluationResult((ActionResult Result, GameStatus Status) pair) => new EvaluationResult(pair.Result, pair.Status);
}
