---
name: tui-click-square
description: Work out which terminal CELL a chess square sits in, so the TUI debug inspector can click the board instead of only typing coordinates. Use when driving Chess.Console's board with the inspector's `click` verb, when a click "does nothing", or when you need to prove mouse input end-to-end.
---

The inspector's `click` takes a **cell**; the board is a **Sixel image**, so nothing on screen tells
you which cell a square is in. `screen` shows blanks there and `cell` reports `kind: "Image"`.

**Do not blind-click to find the board.** It burns calls and mislearns: two of the first three probes
below landed outside the board, and the third landed on a legal square that still selected nothing.

## Just run the mapper

`square_cell.py` computes the mapping from the inspector's own `size` reply and clicks for you.

```
python .claude/skills/tui-click-square/square_cell.py <port> --where e2 e4   # print cells only
python .claude/skills/tui-click-square/square_cell.py <port> e2 e4           # play e2e4 by mouse
python .claude/skills/tui-click-square/square_cell.py <port> --verify d2      # click and assert
```

`--verify` is the one that matters. The mapping replicates layout code that lives in C#, so it *can*
drift; verify clicks and then asserts `app_state.selected` is the square you named, which turns drift
into a loud failure instead of a silent mis-click. Run it once before trusting a run of clicks.

Get the port from `inspector.log` (`grep -oE "command server on 127\.0\.0\.1:[0-9]+"`), or use the
`tui-inspector` MCP for everything except the arithmetic.

## Two things that look like bugs and are not

- **A correct click on a piece with no legal move selects nothing.** In the start position the white
  king on e1 reports `selected: null`; after `e2e4` vacates e2 the *identical* click returns
  `selected: "e1"`. This was mistaken for a geometry error. If `--verify` fails, rule this out before
  suspecting the mapping — pick a rank-2 pawn, which always has a move.
- **Probing for `kind: "Image"` finds the CANVAS, not the board.** One `Canvas` is one contiguous
  Sixel blit spanning the captured gutter *and* the board, so `Image` starts at column 0 while the
  board starts ~46 columns in. There is no cell-level signal for the board's edge.

## The chain, if you must derive it by hand

Four layers, each of which moves the board. Terminal 202x63 at 10x20 worked through, as an example
whose numbers you can check against `app_state.squareSize`:

1. **Shape** — `GameFrameLayout.ChooseShape` costs all three shapes in board squares. Here flanked
   132 > side-by-side 118 > stacked 108, so **Flanked**, and `CapturedLayout` is `External`.
2. **Slot** — `ConsoleGameDisplayBase.ArrangeFrame` arranges in cells: a spacer row, then
   `[captured 24 | board 154 | history 24]`, then the status row. Board slot = 154x61 cells at
   (24, 1), so `uiSize` = 1540x1220px and `BoardLeft/BoardTop` = 240/20.
3. **Square size** — `CalculateSquareSize(1540, 1220, External)` = `min(1540/9.5, 1220/9.2)` = 132,
   then `AlignDown` to `lcm(10,20)` = **120**. Matching `app_state.squareSize` confirms steps 1-2.
4. **Centring** — `margin` 60; `topMargin` = `(1220-1080)/2` aligned = 60, `+ BoardTop` = **80**;
   `leftOffset` = 240 + `(1540-1080)/2` aligned = **460**.

Then `SquareRect`: `x = col*120 + 60 + 460`, `y = rowFromTop*120 + 60 + 80` — squares span x 520-1480,
y 140-1100. Divide by the cell size for the cell. `DisplayCell` is `flip ? (7-file, rank) : (file, 7-rank)`.

Verified live: e2 -> (106,46), e4 -> (106,34), e1 -> (106,52); clicking (106,46) then (106,34) played
`e2e4` and the history panel read `1. e2e4`.

## Dragging between squares, not just clicking

The same cell arithmetic addresses a drag. Setup mode moves a piece by press-drag-release, and the
piece follows the cursor while it is in hand — so a drag has something to *look at* mid-gesture,
unlike a click.

**Use `press`/`move`/`release`, not `drag`.** An atomic `drag` arrives in the input queue all at once
and `HumanPlayer.Coalesce` drops the render for every motion event that has another behind it, which
is correct behaviour and which means **no intermediate position is ever painted**. Stepping puts one
event in flight at a time. `move` is refused unless a button is held — a terminal only reports motion
during a drag (mode 1002), so there is no hover to synthesize.

Assert on `app_state`, which reports `pickedUp` and the ghost rect (`ghostX/Y/W/H`) precisely because
the board is one Sixel blit and every cell under it reads back blank. Verified live, e2 → e4 in
setup mode at square size 120:

- after `press` on e2's cell (106,46): `pickedUp=e2`, and **no ghost yet** — the drag point is reset
  at pick-up so nothing paints before the pointer actually moves.
- each `move`: `ghostY` walks 800 → 760 → 680 → 620 while `ghostW`/`ghostH` stay **120, one square**.
- the last rect, (1000, 620), is exactly e4's square: the grab offset captured at pick-up (65, 70) is
  preserved the whole way, so the piece stays held where it was grabbed.
- `partialRenders` advances while `fullRenders` does not — the terminal pays for a region, not a frame.
- after `release`: `pickedUp` is null and the ghost is gone.

If `ghostW`/`ghostH` ever come back as something other than the square size, stop: the four-square
damage bound assumes a one-square ghost, and a scaled one silently makes it nine.

## Chords, and the flipped board

`key` carries modifiers (Console.Lib **4.7**): `mods` is substring-matched and case-insensitive, so
`"Ctrl"`, `"ctrl+shift"` and `"CtrlShift"` all work — the same spelling the SDL inspector takes. The
reply echoes what was resolved (`"mods":"Control"`), so assert on that rather than inferring.

**Unrecognised modifier text is refused, not downgraded.** That matters here more than it looks: a
dropped Ctrl turns Ctrl+F into bare `f`, and bare `f` is chess's **file-f selector** — a different
valid action, not a no-op. Verified live: bare `f` sets `pendingFile=F` while the board stays put.

This is what makes the flipped board reachable, so `--verify` covers both orientations. Ctrl+F
toggles `app_state.flipBoard`, and the mapper reads it: unflipped `d2` is cell `(106,46)`, flipped it
is `(106,16)`, and the app confirms `selected: "d2"` either way.

An injected chord is what the real parser produces — a terminal sends Ctrl+letter as one control byte
that `VirtualTerminal` decodes to `(ConsoleKey.A + n, Control)` — so this drives the app's genuine
binding, not an inspector-only path.

See `run-tui` for launching the app and the full verb table. **Launch ONE instance.**
