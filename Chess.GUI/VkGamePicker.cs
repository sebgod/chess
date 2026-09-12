using System;
using System.Collections.Generic;
using System.Linq;
using Chess.Lib.UI;
using Chess.Net;
using Chess.UCI;
using DIR.Lib;
using SdlVulkan.Renderer;

namespace Chess.GUI;

/// <summary>
/// "Continue game" once there can be more than one: a list of saved games to pick from, rendered
/// through the same <see cref="PixelMenuWidget{TSurface}"/> as the startup menu and the LAN lobby.
///
/// <para>A screen the wizard routes <em>into</em>, rather than a wizard phase, following
/// <see cref="VkLanLobby"/>. <see cref="StartupWizard"/> is a state machine over a FIXED set of
/// items — its <c>Confirm(int)</c> switches on the index — and a list whose length is however many
/// games you happen to have is data, not a phase. Bending the wizard around it would put the one
/// thing every front-end shares at the mercy of one host's disk.</para>
///
/// <para>The inbox is read once here and on an explicit toggle, not per frame: only this process
/// writes it, so re-reading the disk every frame would buy nothing and cost a file enumeration at
/// 60Hz.</para>
/// </summary>
internal sealed class VkGamePicker : IWidget
{
    private readonly string _dataDir;
    private readonly TimeProvider _time;

    // The name rows call US by. It already exists for LAN play, so a player who set one there is not
    // asked again; an empty one reads as "You", which is true and needs no prompt.
    private readonly string _localName;

    private PixelMenuWidget<VulkanContext>? _menu;
    private List<InboxEntry> _rows = [];
    private int _hiddenCount;
    private bool _showOlder;

    // What the widget currently shows, so Reset() (which snaps the selection back to 0) only runs
    // when the content actually changed — the same guard VkLanLobby uses.
    private string _shownPrompt = "";
    private string[] _shownItems = [];

    /// <summary>The game the user chose, once they have. Null until then.</summary>
    public InboxEntry? Picked { get; private set; }

    /// <summary>True when the user backed out; the host returns to the menu.</summary>
    public bool IsAborted { get; private set; }

#if DEBUG
    /// <summary>The underlying pixel widget, so the DEBUG inspector can read its clickable regions
    /// + captured layout (null until the first Render builds it).</summary>
    public PixelWidgetBase<VulkanContext>? InspectorWidget => _menu;

    /// <summary>Row count exposed for the DEBUG inspector's appState snapshot.</summary>
    public int RowCount => _rows.Count;
#endif

    public VkGamePicker(string dataDir, TimeProvider time)
    {
        _dataDir = dataDir;
        _time = time;
        _localName = LanProfile.Load(dataDir).Name;
        Reload();
    }

    private void Reload()
    {
        var now = _time.GetUtcNow();
        var all = GameInbox.Load(_dataDir);

        // Games waiting on YOU first, then most recently played. GameInbox.Load already orders by
        // recency and OrderBy is stable, so ordering by the flag alone preserves that within groups.
        _rows = [.. all.Where(e => _showOlder || !e.IsStale(now))
                       .OrderBy(e => e.IsWaitingOnYou ? 0 : 1)];

        _hiddenCount = all.Count - _rows.Count;
    }

    public void Render(VkRenderer renderer)
    {
        _menu ??= new PixelMenuWidget<VulkanContext>(renderer, FontPaths.DejaVuSans);
        RefreshMenuContent();
        _menu.Render();
    }

    private void RefreshMenuContent()
    {
        var now = _time.GetUtcNow();
        var waiting = _rows.Count(e => e.IsWaitingOnYou);

        var prompt = _rows.Count == 0
            ? "No saved games."
            : waiting > 0
                ? $"{waiting} waiting on you:"
                : "Pick a game to continue:";

        string[] items =
        [
            .. _rows.Select(e => e.Summary(now, _localName)),
            .. _hiddenCount > 0 ? new[] { $"Show {_hiddenCount} older game{(_hiddenCount == 1 ? "" : "s")}" } : [],
            "Back",
        ];

        if (prompt == _shownPrompt && items.AsSpan().SequenceEqual(_shownItems)) return;

        _shownPrompt = prompt;
        _shownItems = items;
        _menu!.Reset(StartupWizard.Title, prompt, [.. items]);
    }

    public bool HandleInput(InputEvent evt)
    {
        if (_menu is null || !_menu.HandleInput(evt)) return false;
        if (!_menu.IsConfirmed) return true;

        var selected = _menu.SelectedIndex;

        if (selected >= 0 && selected < _rows.Count)
        {
            Picked = _rows[selected];
        }
        else if (_hiddenCount > 0 && selected == _rows.Count)
        {
            // "Show older": reveal the games past the staleness threshold. They were hidden, never
            // deleted, so this is the whole of getting one back.
            _showOlder = true;
            Reload();
        }
        else
        {
            IsAborted = true;
        }

        // Force a content rebuild next Render (also clears the widget's confirmed flag via Reset).
        _shownItems = [];
        return true;
    }
}
