# "Across the table" PvP — rotate the whole UI 180° (Android tablet)

**Status:** shipped. An explicit game mode ("Across the table" in the startup menu, Android only):
the frame turns 180° to face the side to move after each committed move; the board counter-rotates
and the chrome mirror-swaps, so the armies keep their physical sides and nothing visibly jumps
position. Plain "Player vs Player" (same-seat pass-and-play) never turns.

## The idea

Two people sharing one tablet play one of two ways:

- **Same seat (pass-and-play — the classic "hot-seat")**: the device is held and handed over; both
  players always see the same upright UI. No turn needed. That's plain *Player vs Player*.
- **Opposite seats across a flat tablet**: one player reads everything upside down. The fix is to
  **rotate the entire UI 180°** for the player sitting opposite — board *and* move history *and*
  status bar — so whoever is to move always reads everything upright. That's *Across the table*.

The seating arrangement is a menu choice, not a guess: an earlier cut auto-armed the turn on any
tablet PvP (via a smallest-width ≥ 500dp gate), which misfires for same-seat tablet play — two kids
passing a Tab M8 would watch the frame spin every move.

## How it differs from the board flip we already have

`GameUI.FlipBoard` rotates **only the 8×8 board** 180° and re-letters the coordinates — it's for
"orient the board to my colour" in a normally-oriented UI (vs-AI, LAN). Text stays upright. This
feature is a **superset**: it rotates the *composited frame* — every pixel, text included — so the
far player reads the whole screen upright (their text is deliberately upside down from the near
player's point of view).

## Mechanism — three things track one flag

The 180° turn is one instance of the constrained **content→device transform** — see
[`docs/device-transform.md`](../../docs/device-transform.md). Per committed move,
`MainActivity.UpdateAcrossTheTableTransform` computes one flag (`flip = Black to move`) and drives
three things off it:

1. **`renderer.DeviceTransform = CenteredRotation(Half)` / Identity** — turns the whole frame
   (text included) via the GPU projection. Input comes back through `M.Invert` at the tap boundary.
2. **`GameUI.FlipBoard = flip`** — counter-turns the *board* so the two 180°s cancel for it: the
   armies stay on their physical sides, exactly like a real board on the table (White always
   nearest White's seat). Without this the armies swap elbows every move (found on-device).
3. **`PixelGameDisplay.MirrorChrome = flip`** — mirror-swaps the chrome layout in content space
   (history panel to the left of the board in landscape / above it in portrait), so after the
   device rotation the board and panel sit at the SAME physical positions as before. Without this
   the board visibly jumps side every turn (found on-device). The status bar keeps its content-space
   dock, so it always lands at the mover's own bottom edge — that's the one piece of chrome that
   *should* move.

Safe-area insets and the camera cutout are mapped device→content by `DeviceContentMapping`
(Chess.Lib.UI) — under 180° the notch lands on the content's bottom edge.

## Decisions (as shipped)

- **Trigger:** auto-rotate to face the side-to-move, applied only once a move is *committed*
  (`UpdateAcrossTheTableTransform` runs after the tap that lands the move, on resize, and on game
  start) — never mid-think, and never during playback scrubbing (the committed live side drives it,
  not the playback cursor). No manual toggle: the menu choice IS the toggle.
- **Mode:** explicit `GameMode.AcrossTheTable` via `StartupWizardOptions.AcrossTheTable` (wizard
  item right after "Player vs Player"). Saves don't record the seating — a resumed PvP game opens
  as same-seat (no turn); resume-as-across-the-table is a possible follow-up.
- **Scope of surface:** the whole window rotates (simplest and most legible, as predicted).
- **Board stability:** the board counter-rotates (`FlipBoard` tracking) AND the chrome mirror-swaps
  (`MirrorChrome`) — see "Mechanism". A pure Y-flip was considered and rejected: a mirror composed
  with the far viewer's 180° viewpoint is still a mirror — the far player would read mirrored text.
  Only a true 180° rotation cancels a 180° viewpoint.
- **Game end:** the frame faces the side that would be to move (i.e. the mated side) — consistent
  with the in-play rule; a "both players" endgame orientation is a possible follow-up.

## Where it lives

- Mechanism: `DeviceTransform` on the abstract renderer + backend support — a sibling-repo change (see
  [`docs/device-transform.md`](../../docs/device-transform.md), phases 1 & 3).
- Chess wiring: `MainActivity` sets the transform/FlipBoard/MirrorChrome triple per committed move
  and maps tap coordinates through the inverse (phase 2). The mode itself is a shared-enum value
  (`GameMode.AcrossTheTable`); other heads don't offer it (a desktop monitor isn't flat between two
  players), but the transform primitive is cross-cutting.
- `GameUI.FlipBoard` stays the board-only primitive; this feature sits *above* it and, in this mode,
  drives it.
