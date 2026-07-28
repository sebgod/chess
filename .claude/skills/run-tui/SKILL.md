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
`--render-fen <fen>` writes a board PNG (`--render-size`, `--move` for the arrow
overlay), and `--mode`/`--side`/`--board` skip the startup wizard.

$ARGUMENTS
