# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
    Always use extended thinking when analyzing bugs or designing architecture or when refactoring.

## Build & Test Commands

```bash
dotnet build -c Release              # Build all projects
dotnet test -c Release               # Run all tests
dotnet test Chess.Tests --filter "ClassName=Chess.Tests.GameTests"  # Run one test class
dotnet test Chess.Tests --filter "GameTests.EvaluateMoves"          # Run one test method
dotnet run --project Chess.Console -c Release                       # Run the console app
dotnet run --project BenchmarkSuite1 -c Release                     # Run benchmarks
```

CI runs `dotnet test --configuration Release` on ubuntu-latest with .NET 10.0.

## Architecture

Chess game with multiple rendering backends. Ten projects in the solution:

- **DIR.Lib** — Device-independent rendering primitives: `PointInt`, `RectInt`, `RGBAColor32`, `TextAlign`, and abstract `Renderer<TSurface>`. No dependencies. AOT-compatible.
- **Chess.Lib** — Core library: board representation, rules, move validation, AI engine (negamax with alpha-beta pruning). All chess logic lives here. References DIR.Lib. AOT-compatible.
- **Chess.UCI** — Shared UCI (Universal Chess Interface) protocol library. Parsing, formatting, client/server helpers. Referenced by both Console and Engine.
- **Chess.Engine** — Standalone UCI engine executable (`chess-engine`). Wraps `AiEngine` from Chess.Lib behind the UCI protocol. Supports `go depth N` and sends `info` lines during search. AOT-compatible.
- **Console.Lib** — Terminal I/O, virtual terminal abstraction, dock-based layout, widgets (`Canvas`, `TextBar`, `ScrollableList`), `SixelRenderer<TSurface>`, Sixel encoding, and truecolor/SGR-16 styling via `VtStyle`. References DIR.Lib. No chess or ImageMagick dependencies.
- **Chess.ImageMagickSixelRenderer** — Concrete `MagickImageRenderer` (extends `SixelRenderer<MagickImage>`) and Sixel encoding extensions for terminal display. References DIR.Lib, Chess.Lib, Console.Lib.
- **Chess.OpenGL** — Standalone OpenGL chess executable (`WinExe`, no console window). Uses Silk.NET for windowing/input and Magick.NET for font atlas. Implements `Renderer<GL>`, `IGameDisplay`, and `IGamePlayer`. References DIR.Lib, Chess.Lib, Chess.UCI. AOT-compatible.
- **Chess.Console** — Terminal chess application with Sixel and ASCII display backends. Communicates with Chess.Engine via UCI over stdin/stdout.
- **Chess.Tests** — xUnit v3 tests with Shouldly assertions. Uses `[MemberData]` with static `DataSource()` methods for parameterized tests.
- **BenchmarkSuite1** — BenchmarkDotNet performance benchmarks for rendering.

### Key type design

**Board** (`Board.cs`) is a record struct storing 8 `uint` fields (4 bits per square: 1 side + 3 piece type = 256 bits total). Operations return new board instances (value-type copy semantics). `Board.EvaluateAction()` is the central move validation method — handles all rules including castling, en passant, promotion, and check detection.

**Game** holds mutable state (`Board`, `ImmutableList<RecordedPly>`, `Side`, `GameStatus`). `Game.TryMove()` is the primary API for making moves — it delegates to `Board.EvaluateAction()` and updates internal state.

**Action** is an immutable record struct (From, To, IsMove, Promoted). Factory methods: `Action.DoMove()`, `Action.Promote()`.

**Position** uses `File` (A-H) and `Rank` (R1-R8) enums. `Position.ToString()` returns UCI-compatible format ("e2", "a7"). All 64 squares are predefined as static fields.

### Name collisions

`Chess.Lib.Action` collides with `System.Action` and `Chess.Lib.File` collides with `System.IO.File`. Files referencing these types alongside `System` need explicit using aliases:
```csharp
using Action = Chess.Lib.Action;
using File = Chess.Lib.File;
```

### UCI communication flow

Chess.Console launches Chess.Engine as a child process. `UciClient` (GUI side) sends commands via stdin, reads responses from stdout asynchronously. `UciServer` (engine side) reads stdin in a loop and dispatches to `IUciEngine`. Move strings use UCI format: "e2e4", "e7e8q" (promotion).

### Test patterns

Tests use xUnit v3 `[Theory]` with `[MemberData]` and helper factories `FromPlies()` and `Custom()` in `GameTests`. Board positions are constructed by modifying `Board.StandardBoard` with `+` (add move) and `-` (remove piece) operators. Assertions use Shouldly (`result.ShouldBe(expected)`).
