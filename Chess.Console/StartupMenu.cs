using Chess.Lib;
using Chess.Lib.UI;
using Console.Lib;

namespace Chess.Console;

/// <summary>
/// Terminal adapter for the shared <see cref="StartupWizard"/> (Chess.Lib.UI): the wizard owns
/// the phases/content/flow, this class only presents each step through Console.Lib's text menu.
/// The desktop (Chess.GUI) and web (Chess.Web) drive the same wizard through DIR.Lib's
/// PixelMenuWidget.
/// </summary>
internal class StartupMenu(IVirtualTerminal terminal, TimeProvider timeProvider, System.Action? drawBackdrop = null)
    : MenuBase<(GameMode Mode, Side ComputerSide, Side SideToMove, Difficulty Difficulty)>(terminal, timeProvider)
{
    // On the alternate screen the menu is a card over the board the host drew first (#84), as on the other hosts.
    protected override bool UsePanel => true;

#if CONSOLE_INSPECTOR
    // Lets the inspector's queued commands run while this menu waits for input (Console.Lib 5.31).
    protected override void OnIdle() => InspectorHooks.Pump();
#endif

    protected override async Task<(GameMode Mode, Side ComputerSide, Side SideToMove, Difficulty Difficulty)> ShowAsyncCore(CancellationToken cancellationToken)
    {
        // The console can open sockets, so it offers LAN "Network game" (routes into ConsoleLanLobby),
        // and it can carry a link, so it offers "Play by Link" too. The link courier needs nothing
        // from the host but a clipboard and somewhere to type -- OSC 52 and a prompt, both of which a
        // terminal has.
        // HeaderBack: a later step's way back is the panel's "< Back" control and Esc (MenuActions.Back), not a row.
        var wizard = new StartupWizard(
            StartupWizardOptions.NetworkPlay | StartupWizardOptions.LinkPlay | StartupWizardOptions.HeaderBack);
        while (!wizard.IsComplete)
        {
            var (title, prompt, items) = wizard.Current;
            // The panel paints its card over what is there and cannot restore the cells beneath, so a smaller card
            // on the next step would leave the last one's edge showing: put the board back first.
            drawBackdrop?.Invoke();
            var choice = await ShowMenuAsync(title, prompt, items,
                wizard.CanGoBack ? MenuActions.Back : MenuActions.None, cancellationToken);
            if (choice.IsBack)
            {
                wizard.Back();
                continue;
            }
            wizard.Confirm(choice.Index);
        }
        return wizard.Result;
    }
}
