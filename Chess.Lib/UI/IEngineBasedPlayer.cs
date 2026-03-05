namespace Chess.Lib.UI;

public interface IEngineBasedPlayer : IGamePlayer, IAsyncDisposable
{
    Task InitAsync(string? initialFen, CancellationToken ct = default);
    Task NewGameAsync(string? initialFen, CancellationToken ct = default);
}
