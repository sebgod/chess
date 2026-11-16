using Chess.Lib;
using Chess.Lib.UI;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

/// <summary>
/// Desktop adapter for the shared <see cref="StartupWizard"/> (Chess.Lib.UI): the wizard owns the
/// phases/content/flow, this class only feeds it through DIR.Lib's PixelMenuWidget for rendering
/// and input. The web (Chess.Web) and console (Chess.Console) drive the same wizard through
/// their own widgets.
/// </summary>
internal sealed class VkStartupMenu(BackgroundTaskTracker tasks, System.Action requestRedraw, bool includeContinue = false, bool online = false) : IWidget
{
    // includeContinue prepends a "Continue game" entry (resumes the persisted in-progress game);
    // the host passes true only when a resumable save exists, exactly as the Android head does.
    // NetworkPlay is always on for the desktop GUI (it can open sockets), appending a
    // "Network game" entry that routes into the LAN lobby. LinkPlay is always on too: a game link
    // needs a clipboard, not a network, and SDL gives the desktop one — so correspondence play works
    // on a machine with no connectivity at all, which is the point of encoding the game in the URL.
    // online adds "Online game", only when there is a backend config (CloudConfigFile): every fork and
    // PR build has none, and the menu then looks exactly as it did.
    private readonly StartupWizard _wizard = new(
        (includeContinue ? StartupWizardOptions.Continue : StartupWizardOptions.None)
        | StartupWizardOptions.LinkPlay
        | StartupWizardOptions.NetworkPlay
        | (online ? StartupWizardOptions.OnlinePlay : StartupWizardOptions.None));

    // Lazy: PixelMenuWidget<VulkanContext> is constructed on first Render call so we have a
    // VkRenderer instance to pass to the PixelWidgetBase ctor.
    private PixelMenuWidget<VulkanContext>? _menu;
    // Built with the menu, from the app's task tracker and the loop's redraw. The menu declares its own keys, so the
    // router lists it and needs no fallback.
    private InputRouter? _router;

    public bool IsComplete => _wizard.IsComplete;
    public (GameMode Mode, Side ComputerSide, Side SideToMove, Difficulty Difficulty) Result => _wizard.Result;

#if DEBUG
    /// <summary>The underlying pixel widget, so the DEBUG inspector can read its clickable regions +
    /// captured layout (null until the first Render builds it).</summary>
    public PixelWidgetBase<VulkanContext>? InspectorWidget => _menu;
#endif

    public void Render(VkRenderer renderer, float dpiScale)
    {
        if (_menu is null)
        {
            var menu = new PixelMenuWidget<VulkanContext>(renderer, FontPaths.DejaVuSans);
            _menu = menu;
            _router = new InputRouter(menu.Ui, tasks, requestRedraw) { Widgets = () => [menu] };
            var (title, prompt, items) = _wizard.Current;
            _menu.Reset(title, prompt, [..items]);
        }
        // The panel is sized in design units, so it needs the window's display scale to keep its size on a
        // high-DPI screen.
        _menu.DpiScale = dpiScale;
        _menu.RenderPanel();
    }

    public bool HandleInput(InputEvent evt)
    {
        if (IsComplete || _menu is null || _router is null) return false;

        // Esc or Backspace on a later wizard step goes back one step, as the step's "Back" row does. On the first
        // step neither is consumed nor reaches the router (whose modal panel would swallow it), so the host can close
        // the menu onto the game behind it on Esc.
        if (evt is InputEvent.KeyDown { Key: InputKey.Escape or InputKey.Backspace })
        {
            if (!_wizard.Back()) return false;
            var (backTitle, backPrompt, backItems) = _wizard.Current;
            _menu.Reset(backTitle, backPrompt, [.. backItems]);
            return true;
        }

        if (!_router.Handle(evt))
            return false;

        if (_menu.IsConfirmed)
        {
            _wizard.Confirm(_menu.SelectedIndex);
            if (!_wizard.IsComplete)
            {
                var (title, prompt, items) = _wizard.Current;
                _menu.Reset(title, prompt, [..items]);
            }
        }

        return true;
    }
}
