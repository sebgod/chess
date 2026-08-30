---
name: run-gui
description: Build and launch the Chess GUI application (Vulkan/SDL3). Use when the user asks to run, start, or test the GUI.
---

Build and launch the Chess GUI application.

Run with stderr redirected to a log file (captures font atlas diagnostics
and .NET exceptions without cluttering the terminal):

```
dotnet run --project Chess.GUI -c Release 2>gui-stderr.log
```

Use `run_in_background: true` on the Bash tool so the GUI runs independently.
Do NOT use shell `&` backgrounding - the GUI exits immediately when backgrounded
via `&` (SDL requires the foreground process).

After the GUI closes, check `gui-stderr.log` if there were any issues.
If the process crashes (exit code 127 or 13x), always read the stderr log
for the actual .NET exception before drawing conclusions from the exit code.

## Driving it yourself: the SDL inspector

Do **not** ask the user to click things you can drive. The renderer ships a DEBUG-only
`DebugInspector` — a TCP command server running commands on the render thread — and chess
registers its sidecar as the **`gui-inspector`** MCP (`dnx SdlVulkan.Renderer.Inspector`).
It finds the app by UDP discovery, so there is no port to copy.

**The launch line above is the WRONG build for this.** `-c Release` has no inspector in it at
all: it is `#if DEBUG` *inside the renderer*, so the renderer itself must compile with DEBUG —
meaning a local sibling, not the NuGet package (which is built in Release). Same rule as the
TUI's inspector.

```
dotnet run --project Chess.GUI -c Debug -p:UseLocalSiblings=true 2> gui.log
```

On start it prints `[inspector] 'Chess.GUI' command server on 0.0.0.0:<PORT>` to **stderr**.
Bind defaults to `Any`, so grep the port without pinning to 127.0.0.1
(`command server on [0-9.]+:([0-9]+)`) and then connect to `127.0.0.1:PORT`.

Tools: `list_instances`, `ping`, `describe_ui`, `describe_layout`, `screenshot`, `click`,
`click_label`, `press_key`, `type_text`, `scroll`, `drag`, `press_hold`, `minimize`,
`maximize`, `restore`, `frame_stats`, `validation_report`, `render_liveness`, `batch`,
`list_signals`, `post_signal`.

- **`screenshot` returns an image block**, so a GUI frame lands directly in the conversation —
  this is the one host where you can just LOOK at the result instead of asserting on state.
- **A screenshot taken while the window is MINIMIZED wedges every later screenshot.** `SdlEventLoop`
  excludes minimized windows from `anyNeedsRedraw`, so `OnRender` never runs, the frame-waiting
  capture never completes, and every subsequent call is refused with
  `'screenshot' is already in progress` — including after the window comes back by hand. **`restore`
  is the fix**: it un-minimizes and rendering resumes, which lets the stuck capture finish. `ping` and
  `describe_ui` keep working throughout, so a live app that cannot screenshot is this, not a crash.
- **The MCP wrapper reports failures as a bare `An error occurred invoking 'x'`.** The raw socket
  returns the actual reason (`{"error": "InvalidOperationException: ..."}`), which is how the latch
  above was identified. When a tool fails opaquely, re-send it over the socket before theorising.
- **`move` IS an MCP tool since SdlVulkan.Renderer 7.27** (a raw verb since 7.25). It is the only verb
  that can drive hover — `click`, `drag` and `press_hold` all arrive with a button DOWN. It takes a
  PATH, `{"x1","y1","x2","y2","steps"}`, not a point; a bare `x`/`y` is rejected. Chess pins 7.30, but
  the sidecar resolves its own version through `dnx`, so on an older one it is raw-socket only.
- **A `move` whose path starts on a live control has been seen to fire that control**, once: a piece in
  hand, a path starting over the setup bin, and the piece came back BINNED. The tool documents itself as
  holding no button, so the mechanism is unexplained rather than established — treat this as "start the
  path somewhere inert and re-check the state afterwards", not as a known behaviour. It read as an app
  bug for a while before being pinned on the harness, which is the part worth remembering.
- **There is no raw `press`/`release` pair** — only `pressHold` (`x`, `y`, `seconds`), which presses
  and releases at the SAME point, so it cannot hold a drag while you move. Worse, it occupies the
  per-frame command pump for its whole duration: a `screenshot` sent during a hold does not queue, it
  fails with the usual bare wrapper error, and a long `seconds` blocks you for that long.
  **You rarely need any of this**: setup mode is click-to-pick-up, so a plain `click` leaves the piece
  in hand with no button held, and `move` then drags it. That is the way to capture a mid-drag frame.
- **`instance=0` means "the ONLY instance", not "the first one".** TianWen is built on the same
  renderer and answers the same discovery, so the moment one is running every tool fails with the bare
  `An error occurred invoking 'x'`. `list_instances` first, then pass `instance=<chess pid>`.
- `describe_layout` shows the whole frame — `board`, `captured`, `history` and `status` by their slot
  `fillKey`, from `GameFrameLayout`'s tree. The board's own 8×8 is NOT in there: it is arranged and
  painted by hand, so only its bounding slot appears. `describe_ui` regions cover the history rows
  only, for the same reason — the board registers none, and is hit-tested by `GameUI` geometry.
- `click_label` matches a `ButtonHit` action. The startup menu registers its items as `listitem`
  regions, NOT buttons, so `click_label` FAILS there — click the region's centre from `describe_ui`
  instead. Their labels are positional (`MenuItem[3]`), and the index shifts with whether "Continue
  game" is present, so prefer keyboard (`Up`/`Down`/`Enter`) for anything order-dependent.
- Keyboard is usually easier than pixel math: menus are `Up`/`Down`/`Enter`; a board move is a
  file key then a rank key — e2e4 = `E`, `D2`, `E`, `D4`.

Unlike the terminal, the GUI repaints a whole frame per event (`PixelGameDisplay.RenderMove`
discards clip rects), so there is no coalescing to work around — an atomic `drag` is fine here.
That is the opposite of the TUI, where a drag must be stepped; see the `run-tui` skill.

$ARGUMENTS
