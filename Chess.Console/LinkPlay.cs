using Chess.Lib;
using Chess.Lib.UI;
using Chess.UCI;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// The console's half of Play by Link: copy the reply link out, and hand a received one back to the
/// host so it can restart the session from it. Shared by the input handler and the run loop, because
/// the two halves happen in different places — a key is pressed inside a tick, and the session it
/// replaces can only be built outside one.
///
/// <para>Nothing here decides the RULES of link play. Which colour is local, whether a move may be
/// made, what a received link means — all of that is <see cref="GameSession"/>'s, exactly as it is
/// for the GUI and the browser. This is a courier and a clipboard.</para>
/// </summary>
internal sealed class LinkPlay(IVirtualTerminal terminal)
{
    /// <summary>
    /// The web app, not a <c>chess://</c> scheme. The person receiving the link may have nothing
    /// installed, and sebgod.github.io/chess plays the same game in any browser — while this app
    /// reads that shape back happily, since <c>GameLinkCodec.ExtractBody</c> reduces all of them to
    /// one body. A link that only works for people who already have the app is not a correspondence
    /// link. (Chess.GUI says the same thing in the same words, for the same reason.)
    /// </summary>
    public const string ShareBaseUrl = "https://sebgod.github.io/chess/";

    /// <summary>Set by the input handler when the player asks to open a link; consumed by the host.</summary>
    public bool PasteRequested { get; set; }

    /// <summary>A decoded link waiting to become the next session.</summary>
    public Game? Pending { get; set; }

    /// <summary>The live display, so a message can reach the status bar. Set as each one is built.</summary>
    public IConsoleStatusDisplay? Display { get; set; }

    /// <summary>
    /// Copies the link for the game as it stands. OSC 52 hands the text to the TERMINAL, which owns
    /// the real clipboard — no platform library, nothing to P/Invoke, and it works over SSH. The
    /// catch is that some terminals require the escape to be enabled, and none of them report back:
    /// the write cannot fail loudly, so the message says what was attempted rather than claiming
    /// success.
    /// </summary>
    public void CopyReplyLink(GameUI ui)
    {
        var url = ShareBaseUrl + GameLinkCodec.EncodeFragment(ui.Game);
        Clipboard.SetText(terminal, url);
        Say("Reply link sent to the clipboard — paste it to your opponent");
    }

    /// <summary>Asks the host to open the link prompt after this session unwinds.</summary>
    public void RequestPaste()
    {
        PasteRequested = true;
        Say("Opening the link prompt…");
    }

    public void Say(string? message)
    {
        if (Display is { } display) display.StatusOverride = message;
    }
}
