# Chess

A terminal chess game with graphical board rendering via [Sixel](https://en.wikipedia.org/wiki/Sixel), built in C# on .NET 10.

![Example game](board_screen.png)

## Features

- Full chess rules: movement, captures, check, checkmate, stalemate, pawn promotion
- Player vs Player, Player vs Computer, and Custom Game (board editor) modes
- [UCI](https://en.wikipedia.org/wiki/Universal_Chess_Interface) protocol support — the engine runs as a separate process, communicating via standard UCI commands
- Graphical board rendered in the terminal using ImageMagick and the Sixel protocol
- Move history panel with algebraic notation — click any move or use Ctrl+Arrow to review past positions
- Cross-platform: Windows, Linux, and macOS (x64 and ARM64)
- Native AOT compiled for fast startup and small footprint

## Requirements

- A terminal with [Sixel](https://en.wikipedia.org/wiki/Sixel) support (e.g. Windows Terminal, mlterm, foot, WezTerm) is preferred, there is a ASCII-only fallback
- .NET 10 SDK (to build from source)

## Getting started

### Download a release

Pre-built binaries are available on the [Releases](https://github.com/sebgod/chess/releases) page for:

| Platform | Archive |
|---|---|
| Windows x64 | `chess-console-win-x64.tar.gz` |
| Windows ARM64 | `chess-console-win-arm64.tar.gz` |
| Linux x64 | `chess-console-linux-x64.tar.gz` |
| Linux ARM64 | `chess-console-linux-arm64.tar.gz` |
| macOS ARM64 | `chess-console-osx-arm64.tar.gz` |
| macOS x64 | `chess-console-osx-x64.tar.gz` |

### Build from source

```bash
git clone https://github.com/sebgod/chess.git
cd chess
dotnet build -c Release
dotnet run --project Chess.Console -c Release
```

## Running tests

```bash
dotnet test -c Release
```

## Keyboard controls

### Gameplay

| Key | Action |
|-----|--------|
| `a`–`h` | Select file (column) |
| `1`–`8` | Select rank (row) — combines with pending file to select a square |
| `Esc` | Clear current selection |
| `F1` | Toggle help |

Select a piece by typing its file + rank (e.g. `e2`), then type the target square (e.g. `e4`) to move. When a piece is already selected, typing just a rank moves it along the same file.

### Playback

During a game, you can review past positions by navigating the move history. Click a move in the history panel, or use:

| Key | Action |
|-----|--------|
| `Ctrl+Left` / `Ctrl+Right` | Step back / forward by one ply |
| `Ctrl+Up` / `Ctrl+Down` | Step back / forward by one full move |
| `Esc` | Exit playback and return to the game |

### Promotion popup

When a pawn reaches the last rank, a popup appears with four piece choices. Click the desired piece, or use:

| Key | Piece |
|-----|-------|
| `n` | Knight |
| `b` | Bishop |
| `r` | Rook |
| `q` | Queen |

### Custom Game setup

In Custom Game mode, you place pieces on the board before playing. The popup appears above the selected square.

| Key | Action |
|-----|--------|
| `a`–`h` + `1`–`8` | Select a square to place a piece on |
| `p` | Place Pawn |
| `n` | Place Knight |
| `b` | Place Bishop |
| `r` | Place Rook |
| `q` | Place Queen |
| `k` | Place King |
| `Tab` | Toggle between placing White and Black pieces |
| `Delete` / `Backspace` | Clear the selected square |
| `Esc` | Cancel the piece popup |
| `s` | Finish setup and start the game |

### Menus

| Key | Action |
|-----|--------|
| `Up` / `Down` | Navigate menu items |
| `Enter` | Confirm selection |
| `1`–`3` | Quick-select by number |

## Project structure

| Directory | Description |
|---|---|
| `Chess.Lib` | Core chess library: board, pieces, rules, AI engine |
| `Chess.UCI` | Shared UCI protocol library (parsing, formatting, client/server) |
| `Chess.Engine` | Standalone UCI engine executable (`chess-engine`) |
| `Chess.Console` | Console application with Sixel rendering |
| `Chess.Tests` | xUnit v3 test suite |
| `BenchmarkSuite1` | BenchmarkDotNet performance benchmarks |

### Architecture

```mermaid
graph TD
    Console["Chess.Console<br/><i>Terminal UI</i>"]
    ConLib["Console.Lib<br/><i>Terminal I/O, menus + Sixel rendering</i>"]
    Engine["Chess.Engine<br/><i>Standalone UCI engine</i>"]
    UCI["Chess.UCI<br/><i>UCI protocol library</i>"]
    Lib["Chess.Lib<br/><i>Board, rules, AI engine</i>"]
    Tests["Chess.Tests<br/><i>xUnit v3 + Shouldly</i>"]
    Bench["BenchmarkSuite1<br/><i>Performance benchmarks</i>"]

    Console -- "project ref" --> ConLib
    Console -- "project ref" --> Lib
    Console -- "project ref" --> UCI
    Console -. "launches as<br/>child process" .-> Engine
    Engine -- "project ref" --> Lib
    Engine -- "project ref" --> UCI
    UCI -- "project ref" --> Lib
    Tests -- "project ref" --> ConLib
    Tests -- "project ref" --> Lib
    Tests -- "project ref" --> UCI
    Bench -- "project ref" --> ConLib
    Bench -- "project ref" --> Console

    Console <-- "UCI over<br/>stdin/stdout" --> Engine
```
