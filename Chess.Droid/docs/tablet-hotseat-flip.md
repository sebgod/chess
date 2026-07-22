# Idea: "across-the-table" hot-seat — rotate the whole UI 180° (Android tablet)

**Status:** idea, not started. Captures a design direction so it isn't lost.

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

## Open questions

- **Trigger:** auto-rotate to face the side-to-move after each move, or a manual button? Auto is the
  "wow", but rotating mid-think is jarring — likely rotate only once a move is *committed*, and only in
  hot-seat PvP (no single local side). A manual toggle is the safe first cut.
- **Scope of surface:** rotate the whole window, or just the game area (leaving a fixed toolbar)? Whole
  window is simplest and most legible.
- **Safe-area / display cutout:** the notch is physically fixed, but a 180° content rotation flips which
  logical edge it sits on. This is no longer a special case — transform the safe-area inset rectangle by
  the same `M` before setting `PixelGameDisplay.SafeAreaInsets` (180° swaps top↔bottom and left↔right).
  See [`docs/device-transform.md`](../../docs/device-transform.md) and the landscape handling in
  [`landscape-polish.md`](landscape-polish.md) for how insets already drive layout.
- **Only tablets:** gate on a large-screen / flat-orientation check; pointless (and disorienting) on a
  phone held by one person.

## Where it would live

- Mechanism: `DeviceTransform` on the abstract renderer + backend support — a sibling-repo change (see
  [`docs/device-transform.md`](../../docs/device-transform.md), phases 1 & 3).
- Chess wiring: set the 180° content transform when hot-seat PvP swaps sides, and map `MainActivity`'s
  tap coordinates through the inverse (phase 2). The across-the-table trigger stays Android-specific;
  other heads don't have the use case, but the transform primitive itself is cross-cutting.
- `GameUI.FlipBoard` stays the board-only primitive; this feature sits *above* it (you'd typically turn
  the per-colour board flip off in this mode, since the whole frame rotates instead).
