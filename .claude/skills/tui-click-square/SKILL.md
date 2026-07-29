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

## Limits

- **`key` cannot send modifiers.** `ConsoleDebugInspector` maps a key name or bare character only, so
  **Ctrl+F (flip board) is not injectable** — nor is any other chorded binding. Bare `f` is the file-f
  selector, not the flip.
- The flip branch of the mapper is therefore **proven for `flipBoard: false` only**. It is the exact
  inverse of `DisplayCell` and reads `flipBoard` from `app_state`, so it should hold; to prove it,
  launch with `--side black`, which auto-orients, and run `--verify` on a black pawn.

See `run-tui` for launching the app and the full verb table. **Launch ONE instance.**
