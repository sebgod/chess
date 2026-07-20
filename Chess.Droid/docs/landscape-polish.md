# Deferred: landscape-mode polish (Android)

> **Unblocked by DIR.Lib 6.14:** the new responsive-layout primitives (`CollapseBelow`, clamped
> `Star` min/max, `Wrap`) now cover the panel/strip math these items hand-roll, so items 1–2 below
> can lean on the framework instead of bespoke rect arithmetic.

**Status:** deferred (future improvement). Landscape already **works** — rotation renders upright
(`preTransform = Identity` in the renderer), the game is preserved across the rotate, the display
cutout moves to a **left** inset and the board shifts clear of it via `GameUI` `leftOffset`, and the
move-history label is no longer cropped. What's left is polish, not correctness. This doc captures
the open items from task #31 so they aren't lost.

## What's already in place (grounding)

- **Safe area drives the shift.** `PixelGameDisplay.SafeAreaInsets` is re-queried on every resize
  (`MainActivity.OnResize` → `SdlWindow.GetSafeAreaInsets()`). In **portrait** the cutout is a `Top`
  inset; in **landscape** it becomes a `Left` inset and the board draws to its right
  (`Chess.Lib/UI/PixelGameDisplay.cs`, `SafeAreaInsets` setter + `leftOffset` threading).
- **The stats strip is portrait-only today.** `RenderTopStrip` is called only when
  `SafeAreaInsets.Top > 0` (`PixelGameDisplay.Render`, ~line 213). It paints the mode label
  (`TopStripLabel`) on the left of the camera and a derived move counter on the right. In landscape
  `Top` is ~0, so **that whole strip is skipped** and the left cutout inset is left blank.
- **History placement already branches on orientation.** Right of the board in landscape; a
  below-board panel in portrait when there's room (`MinPortraitHistoryHeight`, ~line 249).

## Open polish items (task #31)

1. **Use the left cutout side-strip for stats (landscape).** Mirror the portrait top strip: when
   `SafeAreaInsets.Left > 0` and `Top == 0`, render a **vertical** strip in the left inset showing
   the mode label + move counter (rotated text, or stacked) instead of wasting that column. Factor
   the portrait `RenderTopStrip` into an orientation-aware `RenderStatsStrip(edge)` so both the top
   (portrait) and left (landscape) insets share one code path — don't re-type the label/counter
   derivation.
2. **Panel spacing (landscape history).** The right-of-board history panel wants tighter, more
   deliberate gutters between board / panel / screen edge. Currently it inherits the generic margin;
   give landscape its own spacing so it doesn't crowd the board or float with a huge gap.
3. **Header top padding.** Add a small top padding to the landscape header/labels so text doesn't sit
   flush against the top edge (portrait gets breathing room from the notch strip; landscape has none).

## Where to build it

- `Chess.Lib/UI/PixelGameDisplay.cs` — the layout funnel: `Render`, `RenderTopStrip` (→ generalize
  to a side-aware stats strip), the history-panel rect math, and the margin/`ChromeFontSize` terms.
- No host change expected: `MainActivity` already re-sets `SafeAreaInsets` / `TopStripLabel` /
  `TopCutout` on resize, so a landscape strip lights up automatically once the display renders it.

## Offline test hook

Extend `Chess.Tests/PixelGameDisplayLayoutTests` with a **landscape** case: render at a wide aspect
with insets `(Left>0, 0, 0, bottom)` and assert (a) the board draws right of the left inset, (b) the
history panel lands to the board's right within the safe area, and (c) once built, the stats strip
occupies the left inset column (reuse the existing `IsChromeBar` band probe, applied to a column
range instead of a row range).
