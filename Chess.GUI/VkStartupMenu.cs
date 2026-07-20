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
internal sealed class VkStartupMenu(bool includeContinue = false) : IWidget
{
    // includeContinue prepends a "Continue game" entry (resumes the persisted in-progress game);
    // the host passes true only when a resumable save exists, exactly as the Android head does.
    private readonly StartupWizard _wizard = new(includeContinue: includeContinue);

    // Lazy: PixelMenuWidget<VulkanContext> is constructed on first Render call so we have a
    // VkRenderer instance to pass to the PixelWidgetBase ctor.
    private PixelMenuWidget<VulkanContext>? _menu;

    public bool IsComplete => _wizard.IsComplete;
    public (GameMode Mode, Side ComputerSide, Side SideToMove) Result => _wizard.Result;

    public void Render(VkRenderer renderer)
    {
        if (_menu is null)
        {
            _menu = new PixelMenuWidget<VulkanContext>(renderer, FontPaths.DejaVuSans);
            var (title, prompt, items) = _wizard.Current;
            _menu.Reset(title, prompt, [..items]);
        }
        _menu.Render();
    }

    public bool HandleInput(InputEvent evt)
    {
        if (IsComplete || _menu is null) return false;

        if (!_menu.HandleInput(evt))
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
