using Chess.Lib;
using DIR.Lib;
using static SDL3.SDL;

namespace Chess.OpenGL;

internal sealed class VkStartupMenu
{
    private enum Phase { GameMode, PlayAs, BoardType }

    private static readonly RGBAColor32 BackgroundColor = new(0x1a, 0x1a, 0x2e, 0xff);
    private static readonly RGBAColor32 TitleColor = new(0xff, 0xce, 0x9e, 0xff);
    private static readonly RGBAColor32 PromptColor = new(0xdd, 0xdd, 0xdd, 0xff);
    private static readonly RGBAColor32 ItemColor = new(0xcc, 0xcc, 0xcc, 0xff);
    private static readonly RGBAColor32 SelectedBg = new(0x30, 0x50, 0x90, 0xff);
    private static readonly RGBAColor32 SelectedFg = new(0xff, 0xd7, 0x00, 0xff);

    private readonly string _fontPath;
    private Phase _phase = Phase.GameMode;
    private int _selected;
    private GameMode _gameMode;
    private Side _computerSide;

    public bool IsComplete { get; private set; }
    public (GameMode Mode, Side ComputerSide) Result => (_gameMode, _computerSide);

    public VkStartupMenu()
    {
        _fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    }

    public void Render(VkRenderer renderer)
    {
        var w = renderer.Width;
        var h = renderer.Height;
        var fontSize = Math.Max(16f, h / 25f);
        var titleSize = fontSize * 1.6f;
        var lineH = fontSize * 2f;

        var (title, prompt, items) = CurrentMenuContent();

        var totalH = titleSize * 2f + lineH + items.Length * lineH;
        var startY = (h - totalH) / 2f;

        var titleRect = new RectInt(((int)w, (int)(startY + titleSize * 2f)), (0, (int)startY));
        renderer.DrawText(title.AsSpan(), _fontPath, titleSize, TitleColor, titleRect, vertAlignment: TextAlign.Center);

        var promptY = startY + titleSize * 2f + lineH * 0.5f;
        var promptRect = new RectInt(((int)w, (int)(promptY + lineH)), (0, (int)promptY));
        renderer.DrawText(prompt.AsSpan(), _fontPath, fontSize, PromptColor, promptRect, vertAlignment: TextAlign.Center);

        var itemsStartY = promptY + lineH * 1.5f;
        for (var i = 0; i < items.Length; i++)
        {
            var itemY = itemsStartY + i * lineH;
            var itemRect = new RectInt(((int)w, (int)(itemY + lineH)), (0, (int)itemY));

            if (i == _selected)
            {
                var highlightPad = w * 0.2f;
                var bgRect = new RectInt(((int)(w - highlightPad), (int)(itemY + lineH)), ((int)highlightPad, (int)itemY));
                renderer.FillRectangle(bgRect, SelectedBg);

                var label = $"\u25B6  {items[i]}";
                renderer.DrawText(label.AsSpan(), _fontPath, fontSize, SelectedFg, itemRect, vertAlignment: TextAlign.Center);
            }
            else
            {
                var label = $"   {items[i]}";
                renderer.DrawText(label.AsSpan(), _fontPath, fontSize, ItemColor, itemRect, vertAlignment: TextAlign.Center);
            }
        }
    }

    public void HandleClick(int x, int y, uint rendererWidth, uint rendererHeight)
    {
        if (IsComplete) return;

        var (_, _, items) = CurrentMenuContent();
        var h = rendererHeight;
        var fontSize = Math.Max(16f, h / 25f);
        var titleSize = fontSize * 1.6f;
        var lineH = fontSize * 2f;

        var totalH = titleSize * 2f + lineH + items.Length * lineH;
        var startY = (h - totalH) / 2f;
        var promptY = startY + titleSize * 2f + lineH * 0.5f;
        var itemsStartY = promptY + lineH * 1.5f;

        for (var i = 0; i < items.Length; i++)
        {
            var itemY = itemsStartY + i * lineH;
            if (y >= itemY && y < itemY + lineH)
            {
                _selected = i;
                Confirm();
                return;
            }
        }
    }

    public void HandleKey(Scancode key)
    {
        if (IsComplete) return;

        var (_, _, items) = CurrentMenuContent();

        switch (key)
        {
            case Scancode.Up:
                _selected = (_selected - 1 + items.Length) % items.Length;
                break;
            case Scancode.Down:
                _selected = (_selected + 1) % items.Length;
                break;
            case Scancode.Return:
                Confirm();
                break;
            default:
                var digit = key switch
                {
                    Scancode.Alpha1 => 0,
                    Scancode.Alpha2 => 1,
                    Scancode.Alpha3 => 2,
                    _ => -1
                };
                if (digit >= 0 && digit < items.Length)
                {
                    _selected = digit;
                    Confirm();
                }
                break;
        }
    }

    private (string Title, string Prompt, string[] Items) CurrentMenuContent() => _phase switch
    {
        Phase.GameMode => ("\u265a Chess \u2654", "Select game mode:", ["Player vs Player", "Player vs Computer", "Custom Game"]),
        Phase.PlayAs => ("\u265a Chess \u2654", "Play as:", ["White", "Black"]),
        Phase.BoardType => ("\u265a Chess \u2654", "Starting board:", ["Empty Board", "Standard Board"]),
        _ => ("", "", [])
    };

    private void Confirm()
    {
        switch (_phase)
        {
            case Phase.GameMode:
                if (_selected == 0)
                {
                    _gameMode = GameMode.PlayerVsPlayer;
                    _computerSide = Side.None;
                    IsComplete = true;
                }
                else if (_selected == 1)
                {
                    _gameMode = GameMode.PlayerVsComputer;
                    _phase = Phase.PlayAs;
                    _selected = 0;
                }
                else
                {
                    _phase = Phase.BoardType;
                    _selected = 0;
                }
                break;

            case Phase.PlayAs when _gameMode is GameMode.PlayerVsComputer:
                _computerSide = _selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;

            case Phase.BoardType:
                _gameMode = _selected == 1 ? GameMode.CustomGameStandardBoard : GameMode.CustomGameEmpty;
                _phase = Phase.PlayAs;
                _selected = 0;
                break;

            case Phase.PlayAs:
                _computerSide = _selected == 0 ? Side.Black : Side.White;
                IsComplete = true;
                break;
        }
    }
}
