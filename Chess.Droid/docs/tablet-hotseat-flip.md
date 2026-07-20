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

## Design sketch

Two ways to implement, roughly in increasing fidelity/cost:

1. **Rotate the final composited image 180° (renderer-level).** Cheapest conceptually: draw the UI as
   usual, then present it rotated 180°. The Vulkan renderer already applies a device `preTransform` for
   screen rotation, so composing an extra content-space 180° is plausible. Text comes out upside down —
   which is exactly what's wanted for the opposite player. Input must be transformed by the same 180°
   (map touch `(x,y)` → `(W-x, H-y)`), the whole-surface analogue of what `DisplayCell`/`LogicalCell`
   do for the board today.
2. **Layout-level mirroring.** Re-lay-out each panel rotated. More faithful (could keep text upright if
   ever wanted) but much more work and largely pointless here — upside-down text is the goal.

Prefer (1).

## Open questions

- **Trigger:** auto-rotate to face the side-to-move after each move, or a manual button? Auto is the
  "wow", but rotating mid-think is jarring — likely rotate only once a move is *committed*, and only in
  hot-seat PvP (no single local side). A manual toggle is the safe first cut.
- **Scope of surface:** rotate the whole window, or just the game area (leaving a fixed toolbar)? Whole
  window is simplest and most legible.
- **Safe-area / display cutout:** the notch is physically fixed, but a 180° content rotation flips which
  logical edge it sits on. `PixelGameDisplay.SafeAreaInsets` would need the top/bottom (and left/right)
  insets swapped for the rotated frame so content still clears the cutout. See the landscape handling in
  [`landscape-polish.md`](landscape-polish.md) for how insets already drive layout.
- **Only tablets:** gate on a large-screen / flat-orientation check; pointless (and disorienting) on a
  phone held by one person.

## Where it would live

- Renderer/host: the 180° present + input transform belongs near `MainActivity`'s render loop and the
  SdlVulkan renderer's `preTransform` path (this is Android-specific; other heads don't have the
  across-the-table use case).
- `GameUI.FlipBoard` stays the board-only primitive; this feature sits *above* it (you'd typically turn
  the per-colour board flip off in this mode, since the whole frame rotates instead).
