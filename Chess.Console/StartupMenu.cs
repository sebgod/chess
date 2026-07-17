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
internal class StartupMenu(IVirtualTerminal terminal, TimeProvider timeProvider)
    : MenuBase<(GameMode Mode, Side ComputerSide, Side SideToMove)>(terminal, timeProvider)
{
    protected override async Task<(GameMode Mode, Side ComputerSide, Side SideToMove)> ShowAsyncCore(CancellationToken cancellationToken)
    {
        var wizard = new StartupWizard();
        while (!wizard.IsComplete)
        {
            var (title, prompt, items) = wizard.Current;
            var selected = await ShowMenuAsync(title, prompt, items, cancellationToken);
            wizard.Confirm(selected);
        }
        return wizard.Result;
    }
}
