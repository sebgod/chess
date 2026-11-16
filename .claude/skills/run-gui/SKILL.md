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

Do **not** ask the user to click things you can drive. The renderer ships `DebugInspector`, a TCP
command server running commands on the render thread, and chess registers its sidecar as the
**`gui-inspector`** MCP (`dnx SdlVulkan.Renderer.Inspector`).

**Since SdlVulkan.Renderer 7.96 the inspector is in every build, off unless opted in at run time:**
`DIR_INSPECTOR=1` plus a `DIR_INSPECTOR_TOKEN` (every request must carry the token). A Debug build
with `DIR_INSPECTOR` unset is still always on, which is the easy way to drive it by hand:

```
dotnet run --project Chess.GUI -c Debug 2> gui.log
```

`CHESS_DATA_DIR` points the GUI at another data folder, so a run never touches the user's saved games.

On start it prints `[inspector] 'Chess.GUI' command server on <ADDRESS>:<PORT>` to **stderr**.
The server is loopback-only; grep the port without pinning the address
(`command server on [0-9.]+:([0-9]+)`) and connect to `127.0.0.1:PORT`.

## The smoke test, in pilot's GUI lane

`Chess.Tests/GuiSmokeTests.cs` starts the built Chess.GUI and drives it with
`SdlVulkan.Renderer.Inspector.Client` (connect by pid and token). It skips unless `DIR_INSPECTOR=1` and a
token are set, which `pilot run --gui` provides, with `PILOT_JOB_ARTIFACTS` for its screenshots. Build
first against packages, then run it from the chess folder (pilot starts a job in the session's folder, and
outside chess there is no `global.json`, so `dotnet test` falls back to VSTest and rejects `--filter-class`):

```
dotnet build -c Release -p:UseLocalSiblings=false
pilot run --gui -- pwsh -NoProfile -Command "Set-Location chess; dotnet test Chess.Tests -c Release --no-build -p:UseLocalSiblings=false --filter-class Chess.Tests.GuiSmokeTests"
```

(Pilot reads MTP's `--filter-*` options since pilot#1091; a node older than that refuses the run as a whole
suite, and `--whole-suite "<why>"` gets past it. A bare short name matches nothing in xUnit v3: use the fully
qualified class, or a leading `*`.)

Tools: `list_instances`, `ping`, `describe_ui`, `describe_layout`, `screenshot`, `click`,
`click_label`, `press_key`, `type_text`, `scroll`, `drag`, `press_hold`, `minimize`,
`maximize`, `restore`, `frame_stats`, `validation_report`, `render_liveness`, `batch`,
`list_signals`, `post_signal`.

- **`screenshot` returns an image block**, so a GUI frame lands directly in the conversation —
  this is the one host where you can just LOOK at the result instead of asserting on state.
- **Fixed in 7.96:** a screenshot of a minimized window now fails within its budget saying so (it no
  longer wedges later screenshots); the MCP tools pass the server's real error through instead of
  `An error occurred invoking 'x'`; and `instance=0` with several apps running fails naming each app and
  pid. Pass `instance=<chess pid>` when TianWen is also up.
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
