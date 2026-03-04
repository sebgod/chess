# Chess

A terminal chess game with graphical board rendering via [Sixel](https://en.wikipedia.org/wiki/Sixel), built in C# on .NET 10.

![Example game](board_screen.png)

## Features

- Full chess rules: movement, captures, check, checkmate, stalemate, pawn promotion
- Player vs Player and Player vs Computer modes
- [UCI](https://en.wikipedia.org/wiki/Universal_Chess_Interface) protocol support — the engine runs as a separate process, communicating via standard UCI commands
- Graphical board rendered in the terminal using ImageMagick and the Sixel protocol
- Move history panel with algebraic notation
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

## Project structure

| Directory | Description |
|---|---|
| `Chess.Lib` | Core chess library: board, pieces, rules, AI engine |
| `Chess.UCI` | Shared UCI protocol library (parsing, formatting, client/server) |
| `Chess.Engine` | Standalone UCI engine executable (`chess-engine`) |
| `Chess.Console` | Console application with Sixel rendering |
| `Chess.Tests` | NUnit test suite |
| `BenchmarkSuite1` | BenchmarkDotNet performance benchmarks |
