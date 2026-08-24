---
name: run-tui
description: Build and launch the Chess.Console terminal app in a new Windows console window with stderr redirected to console-stderr.log. Use when the user asks to run, launch, start or open the TUI / terminal / console chess app.
---

Chess.Console takes over stdin/stdout for terminal rendering, so it cannot share the
current console with the agent. Launch it in a **new Windows console window**.

```
cmd //c start "Chess TUI" cmd //k "dotnet run --project Chess.Console -c Release 2> console-stderr.log"
```

**Why a real console and not a pipe.** The backend is chosen from the terminal's
reported `ImageDisplayCapability` (`Chess.Console/Program.cs`): Sixel gets
`SixelGameDisplay`, anything else falls back to `AsciiDisplay`. Under a redirected
pipe the capability probe fails, so a captured run silently exercises the ASCII path
and tells you **nothing** about the sixel display, its layout tree, or the board
renderer. Only a real console window reaches `ConsoleGameDisplayBase`.

Notes:

- Do **not** use `run_in_background: true` — `cmd //c start` detaches the new window
  itself and the parent Bash call returns immediately. Do not try to stream the app's
  output back.
- `cmd //k` keeps the new window open after the app exits, so a final stack trace does
  not vanish with the process.
- The double slashes (`//c`, `//k`) are Git-Bash / MSYS mangling protection, so the
  literal `/c` and `/k` reach `cmd.exe` instead of being rewritten as paths.
- The window title `"Chess TUI"` doubles as `start`'s first positional argument — a
  Windows quirk when the first argument is quoted.
- After the user quits, read `console-stderr.log` for .NET exceptions and terminal
  capability diagnostics. It is covered by `*.log` in `.gitignore`.
- Exit codes 127 / 13x mean the .NET process crashed, not "command not found" — read
  the log before drawing conclusions from the code.

Useful non-interactive flags for a quick check that needs no terminal at all:
`--render-fen <fen>` writes a board as **Sixel to stdout** (`--render-size`, `--move` for
the arrow overlay), and `--mode`/`--side`/`--board` skip the startup wizard.

**`--side` is required by `custom` as well as `pvc`**, not just `pvc` as the option's own
description implies (`Chess.Console/Program.cs`, `rootCommand.Validators`). Custom setup is a
one-human flow, so the requirement is easy to forget: `--mode custom --side white --board
standard` is the full line.

**A launch that only prints the help block is a VALIDATION FAILURE you redirected away, not a
bad `--help` guess.** `System.CommandLine` writes the usage block to stdout and the actual
reason to **stderr**, so the `2> inspector.log` in every launch line here sends the one useful
sentence to the log while the window shows a bare help dump. Before re-guessing the flags, run
`head -3 inspector.log` — the answer is sitting in it (`--side is required when --mode is
'pvc' or 'custom'.`, exit 1).

## Driving it yourself: the debug inspector

Do **not** ask the user to click things you can drive. The TUI has a DEBUG-only inspector —
a loopback JSON command server that reads the screen as TEXT and injects keys and clicks.

**It needs a Debug build AND local siblings**, and that second part is not optional: the
inspector lives in Console.Lib/DIR.Lib behind `#if DEBUG`, and a *published* package is
built in Release, so its inspector is compiled out. Chess gates its wiring on
`CONSOLE_INSPECTOR`, defined only when `UseLocalSiblings=true` and `Configuration=Debug`.
Locally that is the default (siblings auto-detect); in CI it is absent, which is why CI
never compiles this code. Chess.GUI's SDL inspector has exactly the same rule.

```
cmd //c start "Chess TUI" cmd //k "set CHESS_INSPECTOR=1 && dotnet run --project Chess.Console -c Debug -- --mode pvc --side white 2> inspector.log"
grep -oE "command server on 127\.0\.0\.1:([0-9]+)" inspector.log
```

**Launch ONE instance and reuse it.** Each one holds the sibling DLLs open, so a stray
instance makes the next `dotnet build` fail with MSB3021 file-in-use. `taskkill //F //IM
Chess.Console.exe` before rebuilding.

**Pick the mode and flags BEFORE launching — a relaunch to change `--mode` is the most common
way a second window appears.** The startup flags are not adjustable afterwards, so decide from
what you need to drive:
- **`--mode pvp`** when you want to script a known move sequence: you control BOTH sides, so a
  12-move line plays out deterministically (that is what fills the history panel, reaches a
  two-digit move number, and exercises both ply columns).
- **`--mode pvc --side white`** only when you actually need engine replies. You cannot script
  more than a move or two blind — the engine's reply decides what is legal next, so a
  pre-planned white line desynchronises immediately.

Getting this wrong and relaunching leaves a dead window on the user's screen (see below) and
reads to them as "the agent keeps starting instances", which is what they see: the windows, not
the reasoning.

**Say so when you kill it.** `cmd //k` keeps the window open, so a killed app leaves a bare
command prompt on screen — indistinguishable from a crash to whoever is looking at that
window. Tell the user you killed it, in the same message. Told after the fact, they will
reasonably attribute the prompt to whatever they last did (this happened: a `taskkill` was
read as "resizing crashes the app"). To tell the two apart: `taskkill` leaves **no** stack
trace in `inspector.log`, a real crash does; and a live app answers `ping`.

Check for the process with the filtered form, `tasklist //FI "IMAGENAME eq Chess.Console.exe"`.
`tasklist | grep -i chess | head` is a trap — dozens of `dotnet.exe` rows sort first and the
`head` cuts off before any `Chess.*` line, so a running app reads as absent.

Protocol: newline-delimited JSON over TCP, `{"id":1,"method":"m","params":{}}` →
`{"id":1,"result":...}` or `{"id":1,"error":"..."}`.

| method | what it gives you |
|---|---|
| `ping` | `{ok, protocol, app}` |
| `size` | grid, cell size, and whether the terminal is buffered |
| `screen` | **every row as text** — assert on `"White to move."`, `"1. e2e4"` |
| `row` `{row}` / `cell` `{column,row}` | one row; one cell's glyph, kind and pen |
| `appState` | `selected`, `pendingFile`, `sideToMove`, `plies`, `mode`, `status`, `squareSize` |
| `inputLog` | last 64 events **with the state each changed** |
| `key` `{key}` `{mods}` | a keystroke; `mods` for a chord (`"Ctrl"`, `"Ctrl+Shift"`) |
| `click` `{column,row}` | press+release at that cell's centre |
| `batch` `{steps}` | run `[{method,params}, …]` one per pump, results as an array |
| `wait` `{frames}` | idle N frames — only meaningful as a batch step |

`batch` and `wait` come from the shared core (DIR.Lib 7.3), not from Console.Lib, so the TUI gained
them without a line of terminal code. A failing step is recorded in place (`"error: …"`) and the
batch still completes, so a long script reports *which* step broke.

- **A move is four keys**: file letter then rank digit, twice. e2e4 = `e`,`2`,`e`,`4`.

### Resizing it (the inspector has no verb for this)

A resize is a property of the **window**, not of the app, so it is driven from outside:
`resize_window.ps1` in this skill directory moves the window and reports whether the app
survived. The resize path rebuilds the renderer surface and re-arranges the frame — including a
shape change (flanked ⇄ stacked) when the aspect crosses over — so it is worth exercising after
any layout or Console.Lib change.

```
powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/run-tui/resize_window.ps1 -Width 1000 -Height 760
```

**The window belongs to `WindowsTerminal.exe`** — not to `Chess.Console.exe`, whose
`MainWindowHandle` is `IntPtr.Zero`, and not to its `cmd.exe` parent either. Find it by the
window title `start "Chess TUI"` set, whoever owns it. Then confirm through the inspector that
`size` reports new `columns`/`rows` and that `screen` still has its chrome.

### Two ways to drive it

**MCP (preferred).** `.mcp.json` registers `tui-inspector` via
`dnx Console.Lib.Inspector --yes`, which finds the running app by UDP discovery — no port
to copy. Tools: `list_instances`, `screen`, `row`, `cell`, `app_state`, `input_log`,
`key`, `keys`, `click`, `size`, `ping`.

**Script.** `proof_inspector.py <port>` in this skill directory is a worked driver and a
regression check: it pings, reads the screen, plays `e2e4` by injected keys, and asserts
the move reaches both `app_state` and the history panel.

### Gotchas worth knowing up front

- `screen` shows **cells only**. The board is Sixel, so its region reads as blank text and
  `cell` reports `kind: "Image"` there — correct, not a fault. Assert the board through
  `app_state`; assert chrome through `screen`.
- **To `click` a board square, use the `tui-click-square` skill** — it computes the cell for a
  square name and can verify the hit. Nothing on screen reveals where a square is (the board is
  one Sixel blit), so blind-clicking to find it does not work.
- `key` takes an optional `mods` (`"Ctrl"`, `"Ctrl+Shift"`), so chords like Ctrl+F (flip board) are
  drivable. Unrecognised modifier text is **refused** rather than sent as a bare key — bare `f` is
  the file-f selector, so a silently-dropped Ctrl would do something else entirely.
- `CHESS_INSPECTOR=1` also enables the cell buffer, which is what gives `screen` content.
- **`P:0 (0% partial)` is expected in a wide terminal, and it is not the cell-buffer diff.** In a
  layout with an external captured gutter (`Placement.HasCapturedGutter`, i.e. the flanked frame a
  wide terminal picks) the partial path is unreachable, because the two sets are disjoint: a
  **selection** deliberately returns *empty* clip rects (`GameUI.TrySelect` — legal-move dots land on
  arbitrary squares, so it asks for a full redraw), while a **move** returns rich clip rects but also
  makes the tray stale (`ConsoleGameDisplayBase.TrayIsStale` is true on every ply), which forces a
  full blit. So nothing can be partial. Narrow/stacked frames use in-board strips instead, no gutter,
  and there moves *do* render partially — that is where to look if you are testing the clip logic.
  Separately, a bare file key (`e`) repaints only the status bar, so the frame counter correctly does
  not move at all — don't read that as a dropped frame.
- `input_log` is the fastest route to any input bug: it shows the raw event and what state
  it changed. That is how the mouse-motion-as-click bug was found.

$ARGUMENTS
