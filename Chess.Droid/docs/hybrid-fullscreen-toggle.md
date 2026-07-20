# Deferred: hybrid double-tap fullscreen toggle (Android)

**Status:** deferred. The shipped default is **fullscreen immersive (sticky)** — max screen space,
the camera-aligned notch stats strip, and SDL's built-in swipe-from-top-edge to peek at the clock
(auto-hides). This doc captures the alternative the user wanted kept on ice, so it can be built
later without re-deriving it.

## Idea

Start in fullscreen immersive (current default) but let the user **double-tap to toggle** between
fullscreen and system-bars-visible, so they can pin the clock/battery when they want it and reclaim
the space when they don't. Not a standard Android system gesture (there is none for *entering* app
fullscreen) — it's a custom, app-owned gesture.

## Why it's low-risk to add later

Most of the plumbing already exists from the safe-area work:

- **`SdlVulkanWindow.ToggleFullscreen()`** already flips `WindowFlags.Fullscreen`
  (`SdlVulkan.Renderer/src/SdlVulkan.Renderer/SdlVulkanWindow.cs`). No renderer change needed.
- **Safe-area relayout is automatic on the resulting resize.** A fullscreen toggle fires a
  `WindowResized`/`WindowPixelSizeChanged` event; `MainActivity.OnResize` already re-queries
  `SdlWindow.GetSafeAreaInsets()` + `QueryTopCutout()` and calls `_display.OnResize(...)`. So going
  bars-visible→fullscreen (and back) already re-insets the board, notch strip, and status bar
  correctly — this was verified end-to-end for rotation, which is the same resize path.
- **`PixelGameDisplay` already renders correctly at any inset**, including Left>0 (landscape cutout)
  via `GameUI` `topOffset`/`leftOffset`.

## What to build

1. **Gesture detection** in `MainActivity.HandleTap` (or a new tap handler): detect a double-tap
   **outside the 8×8 board** (in the margins / notch strip / status bar / history area) so it can't
   be confused with piece selection or a history-row tap. SDL delivers tap count in the mouse-down
   event (the `clickCount` arg already threaded through `loop.OnMouseDown = (button, x, y, count, _)`
   — currently ignored). A `count == 2` on a non-board hit → toggle.
   - Alternatively bind it to a small explicit affordance (a chevron in the notch strip), which is
     more discoverable than a bare double-tap. Decide during implementation; the user leaned toward
     a gesture.
2. **Toggle call:** `SdlWindow.ToggleFullscreen()` on the SDL thread (tap handler already runs
   there). The resize event does the rest.
3. **Persist the preference** alongside the game save (`game.uci` header, or a tiny separate pref
   file) so the chosen mode survives relaunch. Cold launch should honor it before the first frame.
4. **Nav-bar color** is already handled: `MainActivity` recolors the navigation bar to the app
   background (`Window.SetNavigationBarColor`) so the bars-visible state looks integrated. Keep that
   call (it's a harmless no-op while fullscreen hides the bars, and correct during the sticky peek).

## Edge cases to cover

- Toggle **during playback** or a **pending promotion** popup — relayout must not drop the modal
  state (it won't: `GameUI.Resize` copies `Mode`/`PlaybackPlyIndex`/`PendingPromotion`).
- Toggle **mid-rotation** — both go through the same resize path; make sure the last-applied insets
  win (re-query in OnResize already does this).
- **Double-tap vs. the "▶ Latest" chip / history rows** — the non-board hit-test must exclude the
  history panel's clickable cells, or scope the gesture to the board margins + notch strip only.

## Offline test hook

Extend `Chess.Tests/PixelGameDisplayLayoutTests` with a "toggle" case: render once with insets =
(0, top, 0, bottom) (bars hidden) and once with the fuller system-bars insets, assert the board
draws in both and the chrome bands land in the right row-ranges (the existing `IsChromeBar`
row-band probe already does exactly this).
