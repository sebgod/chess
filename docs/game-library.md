# Design: carving a board-game library out of chess

**Status:** exploration + shipped prerequisites. The Sixel colour-extent optimization (Console.Lib),
the last-move-arrow clip-rect fix, the engine's deadline-aware search, and **the shared turn model
(`GameSession`, now driving all three front-ends)** have landed. The clock and the extraction tiers
below are proposed, not built.

**Scope:** a second board game — "Memory" (concentration) — would live in its **own repo**, the way
Chess.Web and Chess.Droid are their own heads. The question this document answers is what, if
anything, chess should give up to a shared library first, and in what order.

## Why not just extract everything

Most of the genuinely reusable infrastructure *is already extracted*, and that is the strongest
evidence about where the real seam sits:

| Already a sibling | What it carries |
|---|---|
| DIR.Lib | primitives, `Layout`, `PixelMenuWidget`, fonts, `DeviceTransform` |
| SdlVulkan.Renderer | `SdlVulkanActivity`, window/swapchain, surface-loss + resume, safe area |
| Console.Lib | terminal, `Canvas`, `SixelEncoder`, `RgbaImageRenderer` |
| WebGl.Renderer | WebGL2 backend, MSDF text |
| LAN.Lib | UDP peer discovery |

That is why Chess.Droid is essentially one `MainActivity`. A board-game library is therefore not
"extract everything" — it is a fairly specific residue, most of which we cannot yet design well
because we have exactly one game to generalise from.

## The seam is named right and typed wrong

`IGameDisplay`, `IGamePlayer` and `GameLoop` read as though they were already game-agnostic. None of
them are:

```
IGameDisplay.RenderInitial(Game game)          // chess Game
IGamePlayer.TryMakeMove(GameUI ui)             // chess GameUI
GameLoop.RunAsync(GameMode, Side, Side, …)     // chess everything
IEngineBasedPlayer.InitAsync(string? initialFen)
UIResponse.NeedsPromotionType | NeedsPiecePlacement
```

The *shapes* are reusable; the types are chess to the bone. Separately, the grid is not a parameter:
`8` is a literal throughout `GameUI` geometry (`8 * _squareSize`, `idx < 8`, `col is >= 0 and < 8`),
and `Position` is `File`/`Rank` enums with 64 predefined statics. Memory at 4×4 or 6×6 needs that
parameterised — a bounded but real refactor.

## What annotation support already exists

Worth recording because it is easy to assume otherwise: chess **already has** a cell-annotation
layer. `GameUI` draws last-move arrows, legal-move dots, selection and check rings, and last-move
borders, and exposes a public, data-driven `ExplicitArrows` list with cycling colours (used by
Chess.MCP to render puzzle solutions). DIR.Lib supplies `DrawLine`, `DrawEllipse`/`FillEllipse`,
`DrawRectangle`, `FillRectangle(s)`, `DrawScrim`, `DrawText`.

It also already works in Sixel: `ConsoleGameDisplayBase.RenderFrame` calls the same
`ui.Render<TSurface, Renderer<TSurface>>(renderer, clip)` that `PixelGameDisplay` does, and Sixel is
just an `RgbaImage` backend. **ASCII is the only backend that cannot** — it composes characters.

So the gap is not capability, it is genericity: those draws are private methods keyed on `Position`
with chess colours and chess semantics baked in.

## Extraction tiers

| Tier | What | Where it lives now | Confidence it generalises |
|---|---|---|---|
| 1 | Grid geometry: cell rects, hit-test, flip/orientation, coordinate labels | `GameUI` (8×8 literals) | **High** — pure math |
| 1 | Cell annotations: arrows, dots, rings, borders, scrims | `GameUI`, private, `Position`-typed | **High** — `ExplicitArrows` already proves the data-driven form |
| 2 | Frame layout: board + side panel + status bar + insets + centring | `PixelGameDisplay` | Medium — captured-pieces gutter is chess |
| 2 | Wizard state machine | `StartupWizard` | Medium — shape generic, content chess |
| 2 | Turn model | `GameSession` (extracted from `GameLoop`; all three drivers host it) | **High for the shape** — it survived contact with a pull driver, a sync push driver and an async single-threaded one. Still chess-*typed* (`Game`, `Side`, `GameUI`) |
| 2 | LAN lobby, invite dance, session transport | `Chess.Net` | **High** — only `Magic="CHESSLAN"`, `ServiceName="chess"` and a UCI `Move` payload are chess |
| 3 | Rules, `Board`, `AiEngine`, FEN/SAN/UCI | `Chess.Lib` | Never |

**Recommended order.** Extract **tier 1 only** now, into a new sibling (`BoardGame.Lib`, depending on
DIR.Lib), with the grid parameterised on rows×cols and annotations as a data list of cell-indexed
marks. Chess adopts it immediately at 8×8, which validates it against a real consumer *before*
Memory exists. Then build Memory against it, deliberately **copying** the frame/wizard/loop rather
than extracting them — copies are cheap, a wrong abstraction is not. Only then extract tier 2, with
two data points, starting with the LAN lobby.

## The two things chess genuinely lacks

### 1. Time

`GameLoop` is a poll loop: input, or a 16 ms sleep. There is no scheduled state transition and no
animation concept anywhere in the stack. Memory's core mechanic *is* a timed transition (two cards
visible, then hidden). This is the one real architectural addition, and chess benefits too — a clock,
and eventually move animation.

Crucially, **a timed state transition is not an animation**. "Reveal, then hide 800 ms later" is two
redraws 800 ms apart, not 48 frames. Board games need the cheap thing; tweening is separable polish.

**The engine's half of this is now done.** `AiEngine.Search` takes an optional move-time budget and a
`CancellationToken`, checks both every 2048 nodes (a power-of-two mask, so the non-checking path is one
bool test), and on expiry discards the unfinished iteration and returns the deepest one that completed.
Depth 1 is exempt from aborting, which is what guarantees a legal move comes back however tight the
clock. `UciTimeBudget` turns `wtime`/`btime`/`winc`/`binc`/`movestogo` into that budget, and `UciServer`
now runs the search off its read loop so `stop` and `isready` can arrive mid-search — previously they
could not, by construction, since `OnGo` blocked the reader.

Three decisions were settled in the process, recorded here so they are not re-litigated:

| Decision | Outcome |
|---|---|
| Time-control format | `base+increment` in seconds (`"300+5"`), as PGN's `TimeControl` tag and every online site already write it. Milliseconds internally, matching UCI. |
| Does the engine get `go wtime/btime`? | **Yes** — it is the primary time mechanism in the spec, universally implemented, and `Chess.UCI` already parsed it. Only the engine ignored it. |
| Does LAN play sync clocks? | **No.** Turn-based play needs agreement on *elapsed time per move*, and moves are already the sync points, so the mover piggybacks its remaining time on the move message. An NTP-style estimator would correct ~1 ms on a LAN — deferred as [LAN.Lib#3](https://github.com/SharpAstro/LAN.Lib/issues/3), which also records why LAN.Lib's broadcast-only transport is the wrong home for it. |

### 2. Turn model

`Side` is White/Black with strict alternation hardcoded in `GameLoop`. Memory wants N players and
"match → same player goes again". Generalising `Side` to a player index plus a turn policy is the
other structural change.

Per-cell state is a third difference but explicitly *not* a library problem: chess's packed
4-bits-per-square `Board` cannot express face-down/face-up/matched and should not try. Memory brings
its own board type.

## The tick model

**Resolved — there is now one turn model, `Chess.Lib.UI.GameSession`, and all three drivers host it.**
The section below describes the situation it replaced; it is kept because the constraints are still
real and still shape anything built on top (a clock, animation, a second game).

What made the split possible was separating the two things `GameLoop` used to do together:

- **The session never renders.** It reports what changed; the driver paints. Web's CPU-backend blit is
  `await`ed JS interop, so a session that painted could not run in a browser at all.
- **`Tick()` advances at most one ply and is synchronous.** One ply, so a driver can repaint between an
  engine's consecutive moves — Web must get "thinking…" on screen before a search blocks its only
  thread, which is what `IsEngineTurn` signals. Synchronous, so Droid can tick from the SDL thread
  without a second thread touching the same `GameUI`. All async work (opponent start-up, reset,
  teardown) lives in explicitly async members.

`SessionTick.PlyCommitted` is reported separately from `UIResponse`, because `IsUpdate` also fires for
playback navigation. All three front-ends had hand-rolled a before/after ply-count diff to work around
that — for saving, for relaying a LAN move, and for rewriting the link fragment.

Two shared `IGamePlayer` implementations let the push drivers stop bypassing the abstraction:
`QueuedInputPlayer` (driver pushes an event, the next tick applies it) and `LocalEnginePlayer` (the
in-process engine, searching on the calling thread).

**Nothing is left outside it.** Every mode of every front-end now advances through the session,
including Chess.Droid's LAN games (the peer is just a `NetworkPlayer` in the opponent slot) and
Chess.Web's custom-game setup.

Two traps that migration turned up, both worth remembering:

- **`GameUI.MoveLockSide` is not the LAN turn gate.** It looks like the natural home for "only your own
  turn is tappable", but it also gates `TryPerformAction` — which is exactly how `NetworkPlayer`
  applies the *peer's* move. Setting it rejects every incoming move. It exists for Chess.Web's link
  play, where the opponent's move arrives as a freshly decoded game rather than through the board.
  Out-of-turn taps need no gate: the rules already refuse them.
- **Persistence has to be re-stated once everything shares a path.** Droid never saved LAN games, but
  only implicitly — the network tap path just never called `SaveGame`. With every mode advancing
  through the same session, that had to become an explicit guard or "Continue" would start offering
  half a game with nobody on the other end.

**A bug fixed rather than ported:** `GameLoop` only polled the player whose turn it was, so
`NetworkPlayer` — the only thing that reports `PeerLeft` — went unasked while the local human was on
move. A LAN peer that resigned went unnoticed on desktop until you played something. The session polls
the opponent every tick regardless of turn, which is safe because every opponent checks the side to
move first, and is skipped during playback where it really could be the remote's turn.

### The three drivers (what the split had to accommodate)

| | Driver | Paradigm | Idle tick available? |
|---|---|---|---|
| Console, GUI, `NetworkGame` | `GameLoop` on its own thread | **pull** — poll for input | yes — the existing `Task.Delay(16, timeProvider)` idle branch |
| Chess.Droid | SDL callbacks | **push** | yes — `SdlEventLoop.CheckNeedsRedraw`, polled on the ~16 ms `WaitEventTimeout` |
| Chess.Web | Blazor events | **push** | **no** |

This is a threading-model divergence, not neglect. Chess.GUI can afford `GameLoop` because it runs it
as a *concurrent, un-awaited* task (`gameTask = gameLoop.RunAsync(...)`) alongside SDL's own loop on
the main thread, reconciled through the signal bus (`PixelGameDisplay { Bus = bus }` /
`OnPostFrame => bus.ProcessPending()`). Chess.Droid deliberately stays on the SDL thread so the
in-process AI needs no cross-thread move handoff. Chess.Web is single-threaded WASM and cannot block
at all — its AI must yield explicitly.

**Consequence for the clock:** a clock still must be a self-contained `TimeProvider`-driven model that
any driver polls, rather than logic inside a loop. `GameSession` is now where it hangs — every driver
ticks it — but the idle-tick column above is still the constraint: **Web has no timer**, and it does
not need one today only because its opponent is synchronous. A clock is the first thing that would
change that, since it must advance while nobody is doing anything. Chess.Droid has a second
constraint of the same shape: `Render()` only runs when `NeedsRedraw` is set, so a clock needs a
condition added to `CheckNeedsRedraw` or it will simply not tick between moves.

The original reasoning, still accurate: the clock must be a `TimeProvider`-driven model in `Chess.Lib.UI`
that any driver polls, never logic embedded in `GameLoop`, or Droid and Web silently miss it. Two
hosts already have the hook; **Web needs a new one** (a `PeriodicTimer` or JS interval). `GameLoop`
already takes a `TimeProvider`, and `FakeTimeProvider` is already a test dependency, so the model is
testable without real time.

## Backend animation capability

Demand-driven rendering is the spine of this codebase, and Sixel is why. That makes animation a
**backend capability**, not a fork in game logic — the same way `ColorMode` flows through the viewport
chain. Game state says "this cell is revealed until T"; a GPU backend may tween toward it, Sixel and
ASCII snap to it. Neither needs a different game.

| Rate | For | Sixel | ASCII |
|---|---|---|---|
| ~1 Hz | clock, reveal timer, flag fall | fine | fine |
| ~60 Hz | slide/flip/fade tweens | no | never |

A clock rendered through `GameUI.StatusLine()` lands in the status bar, which on the console path is
a `TextBar` rather than the sixel canvas — so **ASCII gets a working clock for free**, the one backend
that can never have arrows or dots.

## Sixel: measured cost profile

Prompted by "can we just make Sixel much faster". Measurements on win-arm64, 800×800, median of 3.

**A hypothesis that was wrong, recorded so it is not retried.** The per-band
`sixelGrid.AsSpan(0, paletteSize * width).Clear()` looked like waste, since it costs the same on a
two-colour band as on a full one. Replacing it with a targeted per-slice clear measured as a **no-op**
(differences within run-to-run noise, occasionally negative) and was reverted. The clear is a few per
cent of runtime at most; `paletteSize` also adapts to the image, so flat content never paid much.

**Where the time actually went.** Confining each colour to a narrow stripe shrank output 15× while
leaving runtime flat — proving the cost was *scanning*, not emitting: every present colour was
RLE-scanned across the full row width regardless of where it appeared. Fixed in Console.Lib by
finding each colour's first/last set column with vectorised `IndexOfAnyExcept`/`LastIndexOfAnyExcept`,
scanning only that span and re-emitting the margins as computed runs — byte-for-byte identical
output, verified against goldens captured from the previous encoder.

| colours | localised, before | after | speedup |
|---|---|---|---|
| 16 | 10.4 ms | 7.0 ms | 1.5× |
| 64 | 19.9 ms | 6.0 ms | 3.3× |
| 254 | 61.7 ms | 9.1 ms | 6.8× |

Colours spread across the full width have no margins to skip and are unchanged.

**Still open — the "video mode".** The encoder is stateless: every frame rebuilds the palette and
re-encodes every band. What it lacks is inter-frame reuse — retaining the palette and per-band output
across frames and re-emitting only bands whose pixels actually changed. That is the single biggest
remaining lever, and it is what would decide whether Sixel can animate a *localised* change like one
card turning. Two caveats: the palette must then be stable across frames (or bands re-emit anyway),
and **delivery is unmeasured** — the benchmark times encoding, not pushing tens of KB of escape
sequences down a pty, which over SSH is likely the real ceiling.

There is deliberately **no benchmark in this repo right now**. An untracked `BenchmarkDotNet.Artifacts/`
used to sit here holding `RenderBoardBenchmark` and `SixelEncoderBenchmark` reports from 2026-03-01,
but the `Chess.Console.Benchmarks` project that produced them was gone from the solution, and the
numbers predated the ImageMagick removal — stale results with no way to re-run them, which is worse
than none, so they were deleted. The `.gitignore` entry stays for whatever replaces them. Standing up
a committed benchmark is therefore a prerequisite for the video-mode work, not an optional extra.

## The partial-render path is under-tested

Found while investigating whether arrows survive partial redraws: they did not.
`TryPerformAction(Action)` invalidated only the *destination* of the previous move, but the last-move
arrow is drawn centre-to-centre and covers squares neither end owns. After `1. Nf3 e5`, g1 was never
repainted and the arrow's tail lingered. Fixed by invalidating both arrows' full spans.

The reason it survived: **`PixelGameDisplay.Render()` always paints its whole `ContentRect` and never
consults the clip rects at all.** The partial path is exercised *only* by
`ConsoleGameDisplayBase`, which unions the rects into one bounding box. So every clip-rect bug is
invisible on GUI, Droid and Web. Anything that adds new overlay geometry — a clock, a reveal timer,
Memory's matched-pair marks — needs its invalidation reasoned about against the console path
specifically, and ideally a test of the same shape as
`GameUITests.Move_ClipRects_CoverThePreviousMoveArrow`.

## Next steps

1. **Optional chess clock** — the tick model's first consumer: a `TimeProvider`-driven clock model in
   `Chess.Lib.UI`, rendered via `StatusLine()`, polled from all three drivers, with flag-fall entering
   `GameStatus`. Off by default. The engine half is **done**, and there is now a single place to hang
   it (`GameSession`). Two driver gaps it will expose, both noted above: Chess.Web has no timer at all,
   and Chess.Droid's `Render()` only runs when `CheckNeedsRedraw` says so. A clock is the first thing
   that must advance while nobody is doing anything, so it is what forces both.
   For LAN, the time control rides the invite alongside the preferred colour, so the invitee agrees to
   the clock when they accept — no separate handshake, and no clock sync (see the table above).
2. **Committed sixel benchmark**, replacing the orphaned artifacts, measuring single-region encode
   *and* write time.
3. **Sixel video mode** — inter-frame band diffing, gated on (2).
4. **Tier-1 extraction** into `BoardGame.Lib`, chess as first consumer.
5. **Memory**, in its own repo, copying tier 2 — but the turn model is now worth *taking* rather than
   copying. Its shape is proven across all three driver paradigms; what still ties it to chess is its
   types (`Game`, `Side`, `GameUI`), not its structure. Generalising those is the tier-1 job.
