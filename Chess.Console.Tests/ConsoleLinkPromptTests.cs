using System.Text;
using Chess.Lib;
using Chess.UCI;
using Console.Lib;
using Shouldly;
using Xunit;

using Action = Chess.Lib.Action;

namespace Chess.Console.Tests;

/// <summary>
/// The console's link prompt, driven the way a terminal drives it: a paste arrives as a burst of
/// ordinary character events, not as a clipboard read. That is the whole reason this prompt exists
/// (see <see cref="ConsoleLinkPrompt"/>), so it is the thing the tests reproduce.
/// </summary>
public class ConsoleLinkPromptTests
{
    private static IEnumerable<ConsoleInputEvent> Paste(string text) =>
        text.EnumerateRunes().Select(r => new ConsoleInputEvent(null, ConsoleKey.None, 0, r));

    private static ConsoleInputEvent Key(ConsoleKey key) => new(null, key, 0);

    private static async Task<Game?> RunAsync(params IEnumerable<ConsoleInputEvent>[] batches)
    {
        var inputs = new Queue<ConsoleInputEvent>();
        foreach (var batch in batches)
        {
            foreach (var e in batch) inputs.Enqueue(e);
        }

        var prompt = new ConsoleLinkPrompt(new TestableTerminal(inputs), TimeProvider.System);
        return await prompt.ShowAsync(TestContext.Current.CancellationToken);
    }

    private static Game GameOf(params Action[] moves)
    {
        var game = new Game();
        foreach (var move in moves) game.TryMove(move).IsMoveOrCapture().ShouldBeTrue();
        return game;
    }

    [Fact]
    public async Task APastedLinkBecomesTheGameItEncodes()
    {
        var game = GameOf(Action.DoMove(Position.E2, Position.E4), Action.DoMove(Position.E7, Position.E5));
        var url = LinkPlay.ShareBaseUrl + GameLinkCodec.EncodeFragment(game);

        var received = await RunAsync(Paste(url), [Key(ConsoleKey.Enter)]);

        received.ShouldNotBeNull();
        GameLinkCodec.EncodeFragment(received).ShouldBe(GameLinkCodec.EncodeFragment(game));
    }

    [Fact]
    public async Task ALongGameSurvivesTheBurst()
    {
        // The question a terminal raises that a window does not: a link for a real game is a few
        // hundred characters arriving at once. Nothing may be dropped, reordered, or truncated —
        // one lost character is a link that decodes to a different game or to none.
        var game = new Game();
        foreach (var (from, to) in new[]
        {
            (Position.G1, Position.F3), (Position.G8, Position.F6),
            (Position.F3, Position.G1), (Position.F6, Position.G8),
            (Position.B1, Position.C3), (Position.B8, Position.C6),
            (Position.C3, Position.B1), (Position.C6, Position.B8),
            (Position.G1, Position.F3), (Position.G8, Position.F6),
            (Position.F3, Position.G1), (Position.F6, Position.G8),
            (Position.B1, Position.C3), (Position.B8, Position.C6),
            (Position.C3, Position.B1), (Position.C6, Position.B8),
        })
        {
            game.TryMove(Action.DoMove(from, to)).IsMoveOrCapture().ShouldBeTrue();
        }

        var url = LinkPlay.ShareBaseUrl + GameLinkCodec.EncodeFragment(game);
        url.Length.ShouldBeGreaterThan(100);

        var received = await RunAsync(Paste(url), [Key(ConsoleKey.Enter)]);

        received.ShouldNotBeNull();
        received.PlyCount.ShouldBe(game.PlyCount);
        GameLinkCodec.EncodeFragment(received).ShouldBe(GameLinkCodec.EncodeFragment(game));
    }

    [Fact]
    public async Task ABareFragmentIsAcceptedToo()
    {
        // Somebody will paste the part after the '#', because that is the part that looks like the
        // game. GameLinkCodec.ExtractBody already reduces every shape to one body; this checks the
        // prompt hands it the text rather than pre-judging it.
        var game = GameOf(Action.DoMove(Position.D2, Position.D4));

        var received = await RunAsync(Paste(GameLinkCodec.EncodeFragment(game)), [Key(ConsoleKey.Enter)]);

        received.ShouldNotBeNull();
        GameLinkCodec.EncodeFragment(received).ShouldBe(GameLinkCodec.EncodeFragment(game));
    }

    [Fact]
    public async Task EscapeBacksOut()
    {
        (await RunAsync(Paste("#g=e2e4"), [Key(ConsoleKey.Escape)])).ShouldBeNull();
    }

    [Fact]
    public async Task EnterOnAnEmptyPromptIsNeverMind()
    {
        (await RunAsync([Key(ConsoleKey.Enter)])).ShouldBeNull();
    }

    [Fact]
    public async Task ABadLinkIsRefusedAndTheUserCanTryAgain()
    {
        // The first attempt is nonsense, the second is a real game — proving the prompt stays open
        // rather than returning null on the first mistake, which would drop the user back to a menu
        // holding a link they now have to find again.
        var game = GameOf(Action.DoMove(Position.E2, Position.E4));

        var received = await RunAsync(
            Paste("just some text"), [Key(ConsoleKey.Enter)],
            Paste(GameLinkCodec.EncodeFragment(game)), [Key(ConsoleKey.Enter)]);

        received.ShouldNotBeNull();
        GameLinkCodec.EncodeFragment(received).ShouldBe(GameLinkCodec.EncodeFragment(game));
    }

    [Fact]
    public async Task BackspaceEditsWhatWasTyped()
    {
        var game = GameOf(Action.DoMove(Position.E2, Position.E4));
        var fragment = GameLinkCodec.EncodeFragment(game);

        var received = await RunAsync(
            Paste(fragment + "x"), [Key(ConsoleKey.Backspace), Key(ConsoleKey.Enter)]);

        received.ShouldNotBeNull();
        GameLinkCodec.EncodeFragment(received).ShouldBe(fragment);
    }
}
