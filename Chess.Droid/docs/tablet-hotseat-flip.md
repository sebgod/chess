# Idea: "across-the-table" hot-seat — rotate the whole UI 180° (Android tablet)

**Status:** shipped (initial cut). Auto-rotates to face the side to move after each committed move in
hot-seat PvP on tablets (smallest-width ≥ 500dp gate — 8" tablets like the Tab M8 report 533dp and
qualify; phones ≤ ~450dp stay out); vs-AI and LAN games keep the fixed orientation.

## The idea

On a tablet lying flat between two players, hot-seat chess is awkward: one player reads everything
upside down. The magic move is to **rotate the entire UI 180°** for the player sitting opposite —
board *and* move history *and* status bar *and* captured strips — so whoever is to move always reads
everything upright. Auto-rotate after each committed move (the board "spins" to face the next player),
or offer it as an explicit toggle.

## How this differs from the board flip we already have

`GameUI.FlipBoard` (shipped) rotates **only the 8×8 board** 180° and re-letters the coordinates — it's
for "orient the board to my colour" in a normally-oriented UI. Text (history panel, status line,
labels) stays upright. This feature is a **superset**: it rotates the *composited frame* — every pixel,
text included — so the far player reads the whole screen upright (their text is deliberately upside
down from the near player's point of view). So it's not "flip the board", it's "rotate the window".

## Mechanism

The 180° flip is one instance of a general, constrained **content→device transform** — see
[`docs/device-transform.md`](../../docs/device-transform.md) for the full design. In short: the
renderer holds a transform (rotation ∈ {0,90,180,270} + uniform scale + translation) that it folds into
its projection, so the whole frame — **text included** — rotates for free on the GPU backends, and
input is mapped back through the inverse at the host boundary (the whole-surface analogue of what
`DisplayCell`/`LogicalCell` do for the board today). The hot-seat is just
`renderer.DeviceTransform = new(Rotation90.Half, dpi, …)`.

That design is a **sibling-repo** change (DIR.Lib + backends); this doc is the product-level driver for
it. The old "re-lay-out each panel rotated" alternative is rejected — upside-down text is the goal, and
a global transform gets it without touching layout.

## Decisions (as shipped)

- **Trigger:** auto-rotate to face the side-to-move, applied only once a move is *committed*
  (`UpdateHotSeatTransform` runs after the tap that lands the move, on resize, and on game start) —
  never mid-think, and never during playback scrubbing (the committed live side drives it, not the
  playback cursor). No manual toggle for now.
- **Scope of surface:** the whole window rotates (simplest and most legible, as predicted).
- **The board counter-flips (found on-device):** the frame flip alone put each army at the WRONG
  elbow — rotating White's picture 180° lands White's pieces at Black's seat, i.e. the armies swapped
  sides every move. A real board across a table never does that. Fix: `GameUI.FlipBoard` **tracks**
  the frame flip in hot-seat mode, so the two 180° rotations cancel for the board — the armies stay
  on their physical sides (White always nearest White's seat) and only the text chrome actually
  turns. Hit-testing composes across both mappings (`M.Invert` then DisplayCell/LogicalCell).
- **Safe-area / display cutout:** the OS reports them in device space; `DeviceContentMapping`
  (Chess.Lib.UI) maps them into content space by the same `M` before setting
  `PixelGameDisplay.SafeAreaInsets`/`TopCutout` — under 180° the notch lands on the content's bottom
  edge. No special-casing in the layout.
- **Only tablets:** gated on `smallestScreenWidthDp >= 500` — pointless (and disorienting) on a phone
  held by one person. 500 rather than the classic 600 because the classic cutoff excludes the 8"
  budget tablets (533dp) this feature is for; phones stay under 500.
- **Game end:** the frame faces the side that would be to move (i.e. the mated side) — consistent
  with the in-play rule; a "both players" endgame orientation is a possible follow-up.

## Where it would live

- Mechanism: `DeviceTransform` on the abstract renderer + backend support — a sibling-repo change (see
  [`docs/device-transform.md`](../../docs/device-transform.md), phases 1 & 3).
- Chess wiring: set the 180° content transform when hot-seat PvP swaps sides, and map `MainActivity`'s
  tap coordinates through the inverse (phase 2). The across-the-table trigger stays Android-specific;
  other heads don't have the use case, but the transform primitive itself is cross-cutting.
- `GameUI.FlipBoard` stays the board-only primitive; this feature sits *above* it and, in hot-seat
  mode, drives it — the board flip tracks the whole-frame flip so the board keeps physical-board
  semantics while the chrome turns (see "The board counter-flips" above).
