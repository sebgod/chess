using System.Collections.Immutable;
using Chess.Lib;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

internal sealed class VkStartupMenu : IWidget
{
    private enum Phase { GameMode, PlayAs, BoardType, SideToMove, HumanSide }

    // Lazy: PixelMenuWidget<VulkanContext> is constructed on first Render call so we have a
    // VkRenderer instance to pass to the PixelWidgetBase ctor. Until then, pending content is
    // stored as plain fields and flushed in the first Render invocation.
    private PixelMenuWidget<VulkanContext>? _menu;
    private readonly string _fontPath;
    private string _pendingTitle;
    private string _pendingPrompt;
    private ImmutableArray<string> _pendingItems;

    private Phase _phase = Phase.GameMode;
    private GameMode _gameMode;
    private Side _computerSide;
    private Side _sideToMove = Side.White;

    public bool IsComplete { get; private set; }
    public (GameMode Mode, Side ComputerSide, Side SideToMove) Result => (_gameMode, _computerSide, _sideToMove);

    public VkStartupMenu()
    {
        _fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        var (title, prompt, items) = MenuContent(Phase.GameMode);
        _pendingTitle = title;
        _pendingPrompt = prompt;
        _pendingItems = [..items];
    }

    public void Render(VkRenderer renderer)
    {
        if (_menu is null)
        {
            _menu = new PixelMenuWidget<VulkanContext>(renderer, _fontPath);
            _menu.Reset(_pendingTitle, _pendingPrompt, _pendingItems);
        }
        _menu.Render();
    }

    public bool HandleInput(InputEvent evt)
    {
        if (IsComplete || _menu is null) return false;

        if (!_menu.HandleInput(evt))
            return false;

        if (_menu.IsConfirmed)
            Confirm();

        return true;
    }

    private void Confirm()
    {
        var selected = _menu!.SelectedIndex;

        switch (_phase)
        {
            case Phase.GameMode:
                if (selected == 0)
                {
                    _gameMode = GameMode.PlayerVsPlayer;
                    _computerSide = Side.None;
                    IsComplete = true;
                }
                else if (selected == 1)
                {
                    _gameMode = GameMode.PlayerVsComputer;
                    AdvanceTo(Phase.PlayAs);
                }
                else
                {
                    AdvanceTo(Phase.BoardType);
                }
                break;

            case Phase.PlayAs when _gameMode is GameMode.PlayerVsComputer:
                _computerSide = selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;

            case Phase.BoardType:
                _gameMode = selected == 1 ? GameMode.CustomGameStandardBoard : GameMode.CustomGameEmpty;
                AdvanceTo(Phase.SideToMove);
                break;

            case Phase.SideToMove:
                _sideToMove = selected == 0 ? Side.White : Side.Black;
                AdvanceTo(Phase.HumanSide);
                break;

            case Phase.HumanSide:
                _computerSide = selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;
        }
    }

    private void AdvanceTo(Phase phase)
    {
        _phase = phase;
        var (title, prompt, items) = MenuContent(phase);
        _pendingTitle = title;
        _pendingPrompt = prompt;
        _pendingItems = [..items];
        if (_menu is not null)
            _menu.Reset(_pendingTitle, _pendingPrompt, _pendingItems);
    }

    private static (string Title, string Prompt, string[] Items) MenuContent(Phase phase) => phase switch
    {
        Phase.GameMode => ("♚ Chess ♔", "Select game mode:", ["Player vs Player", "Player vs Computer", "Custom Game"]),
        Phase.PlayAs => ("♚ Chess ♔", "Play as:", ["White", "Black"]),
        Phase.BoardType => ("♚ Chess ♔", "Starting board:", ["Empty Board", "Standard Board"]),
        Phase.SideToMove => ("♚ Chess ♔", "Side to move first:", ["White", "Black"]),
        Phase.HumanSide => ("♚ Chess ♔", "Play as:", ["White", "Black"]),
        _ => ("", "", [])
    };
}
