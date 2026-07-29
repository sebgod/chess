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
| `key` `{key}` | a keystroke — `"Escape"`, `"F1"`, or a bare char |
| `click` `{column,row}` | press+release at that cell's centre |

- **A move is four keys**: file letter then rank digit, twice. e2e4 = `e`,`2`,`e`,`4`.

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
- `CHESS_INSPECTOR=1` also enables the cell buffer, which is what gives `screen` content.
- `input_log` is the fastest route to any input bug: it shows the raw event and what state
  it changed. That is how the mouse-motion-as-click bug was found.

$ARGUMENTS
