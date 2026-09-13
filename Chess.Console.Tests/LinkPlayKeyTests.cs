using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using Console.Lib;
using Shouldly;
using Xunit;

using Action = Chess.Lib.Action;

namespace Chess.Console.Tests;

/// <summary>
/// The console's Play-by-Link keys. These are HOST-level bindings taken before <see cref="GameUI"/>
/// sees the event, which is the same division the GUI makes — so the thing worth testing is exactly
/// that division: the chord acts, the bare key does not, and neither reaches the board by accident.
/// </summary>
public class LinkPlayKeyTests
{
    private static ConsoleInputEvent Chord(ConsoleKey key) => new(null, key, ConsoleModifiers.Control);
    private static ConsoleInputEvent Bare(ConsoleKey key) => new(null, key, 0);

    private static (GameUI Ui, HumanPlayer Player, LinkPlay Link, TestableTerminal Terminal,
                    Queue<ConsoleInputEvent> Inputs) Setup(Game? game = null)
    {
        var inputs = new Queue<ConsoleInputEvent>();
        var terminal = new TestableTerminal(inputs);
        var link = new LinkPlay(terminal);
        var ui = new GameUI(game ?? new Game(), 800, 800);
        return (ui, new HumanPlayer(terminal, link), link, terminal, inputs);
    }

    [Fact]
    public void CtrlL_PutsTheWebLinkForTheLiveGameOnTheClipboard()
    {
        var game = new Game();
        game.TryMove(Action.DoMove(Position.E2, Position.E4)).IsMoveOrCapture().ShouldBeTrue();

        var (ui, player, _, terminal, inputs) = Setup(game);
        terminal.ClearOutput();
        inputs.Enqueue(Chord(ConsoleKey.L));

        var result = player.TryMakeMove(ui);

        result.ShouldNotBeNull();
        result.Value.Response.HasFlag(UIResponse.NeedsRefresh).ShouldBeTrue();

        // OSC 52 carries the payload base64-encoded, so the assertion decodes rather than hunting for
        // the URL as a substring -- which would pass just as well if the escape were malformed.
        var output = terminal.Output;
        output.ShouldContain("\u001b]52;c;");
        var b64 = output[(output.IndexOf("\u001b]52;c;", StringComparison.Ordinal) + 7)..];
        b64 = b64[..b64.IndexOf('\u0007')];
        var copied = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));

        copied.ShouldBe(LinkPlay.ShareBaseUrl + GameLinkCodec.EncodeFragment(game));
        // The WEB url, not a chess:// one: an opponent who has nothing installed must still be able
        // to play it.
        copied.ShouldStartWith("https://");
    }

    [Fact]
    public void CtrlO_EndsTheSessionSoTheHostCanBuildTheNextOneFromTheLink()
    {
        var (ui, player, link, _, inputs) = Setup();
        inputs.Enqueue(Chord(ConsoleKey.O));

        var result = player.TryMakeMove(ui);

        result.ShouldNotBeNull();
        // A received link REPLACES the game, and a session cannot swap its own game out underneath
        // itself — so the only honest answer to this key is "stop", and the host takes it from there.
        result.Value.Response.HasFlag(UIResponse.NeedsRestart).ShouldBeTrue();
        link.PasteRequested.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ConsoleKey.L)]
    [InlineData(ConsoleKey.O)]
    public void WithoutTheChord_TheKeyIsJustAKey(ConsoleKey key)
    {
        var (ui, player, link, terminal, inputs) = Setup();
        terminal.ClearOutput();
        inputs.Enqueue(Bare(key));

        player.TryMakeMove(ui);

        link.PasteRequested.ShouldBeFalse();
        terminal.Output.ShouldNotContain("\u001b]52;");
    }

    [Fact]
    public void WithNoLinkPlay_TheChordsAreNotSwallowed()
    {
        // Every other mode builds the player without a courier, and those sessions must behave
        // exactly as they did before link play existed.
        var inputs = new Queue<ConsoleInputEvent>();
        var terminal = new TestableTerminal(inputs);
        var player = new HumanPlayer(terminal);
        var ui = new GameUI(new Game(), 800, 800);

        inputs.Enqueue(Chord(ConsoleKey.L));
        var result = player.TryMakeMove(ui);

        result.ShouldNotBeNull();
        result.Value.Response.HasFlag(UIResponse.NeedsRestart).ShouldBeFalse();
        terminal.Output.ShouldNotContain("\u001b]52;");
    }
}
