using Chess.Lib;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

internal sealed class VkStartupMenu : IWidget
{
    private enum Phase { GameMode, PlayAs, BoardType, SideToMove, HumanSide }

    private readonly VkMenuWidget _menu;
    private Phase _phase = Phase.GameMode;
    private GameMode _gameMode;
    private Side _computerSide;
    private Side _sideToMove = Side.White;

    public bool IsComplete { get; private set; }
    public (GameMode Mode, Side ComputerSide, Side SideToMove) Result => (_gameMode, _computerSide, _sideToMove);

    public VkStartupMenu()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
        var (title, prompt, items) = MenuContent(Phase.GameMode);
        _menu = new VkMenuWidget(fontPath, title, prompt, items);
    }

    public void Render(VkRenderer renderer) => _menu.Render(renderer);

    public bool HandleInput(InputEvent evt)
    {
        if (IsComplete) return false;

        if (!_menu.HandleInput(evt))
            return false;

        if (_menu.IsConfirmed)
            Confirm();

        return true;
    }

    private void Confirm()
    {
        var selected = _menu.SelectedIndex;

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
        _menu.Reset(title, prompt, items);
    }

    private static (string Title, string Prompt, string[] Items) MenuContent(Phase phase) => phase switch
    {
        Phase.GameMode => ("\u265a Chess \u2654", "Select game mode:", ["Player vs Player", "Player vs Computer", "Custom Game"]),
        Phase.PlayAs => ("\u265a Chess \u2654", "Play as:", ["White", "Black"]),
        Phase.BoardType => ("\u265a Chess \u2654", "Starting board:", ["Empty Board", "Standard Board"]),
        Phase.SideToMove => ("\u265a Chess \u2654", "Side to move first:", ["White", "Black"]),
        Phase.HumanSide => ("\u265a Chess \u2654", "Play as:", ["White", "Black"]),
        _ => ("", "", [])
    };
}
