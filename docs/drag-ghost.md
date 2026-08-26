# Design: the dragged piece follows the cursor (setup-mode drag ghost)

**Status:** Phases 1–3 done — `GameUI` carries the ghost, states its damage and renders it;
Chess.Console, Chess.GUI and Chess.Droid all feed it motion. **Live-verified in the GUI** (see
[Live verification](#live-verification-only-one-of-the-two-routes-exists-corrected-2026-08-26));
Chess.Droid compiles but has not been run on a device. Phase 4 (the browser) pending — see
[Phasing](#phasing).
**Repo scope:** **chess only** — no sibling change, no package repin. Every capability this needs
already ships: all three renderers already alpha-blend, every host already delivers pointer motion in
content space, and `DrawPiece` already draws a piece into an arbitrary rect.

## Why

Setup-mode drag landed in `8cb7249`/`7c4134a`: press a piece, release over another square, it moves.
The gesture is complete and correct, but **nothing follows the cursor** — the piece stays on its
origin square with the picked-up tint and reappears on the target at release. It works; it does not
feel like dragging, and on a touch host the only feedback that a press picked something up is a tint
under your own finger.

This is an affordance on top of a mechanism that already works, which is exactly why it was carved
out of that PR rather than bolted onto it.

## What already exists, and what genuinely doesn't

| Piece | Where | State |
|---|---|---|
| Translucency, Vulkan | `VkPipelineSet.cs:228-233` — `blendEnable = true`, `SrcAlpha`/`OneMinusSrcAlpha` | **Exists** |
| Translucency, WebGL | `webgl-renderer.js:137` — `blendFuncSeparate(SRC_ALPHA, ONE_MINUS_SRC_ALPHA, …)` | **Exists** |
| Translucency, software | `RgbaImageRenderer.cs:439` — glyph coverage multiplied by `color.Alpha` | **Exists** |
| Drawing a piece anywhere | `GameUI.DrawPiece(renderer, piece, rect, fontSize)` takes an arbitrary `RectInt` | **Exists** |
| "A piece is in hand" | `GameUI.PickedUp` | **Exists** (phase 1 of the drag work) |
| Motion, GUI | `SdlEventLoop.OnPointerInput` delivers `InputEvent.MouseMove`, already content-mapped by `MapPointerToContent` | **Exists**, deliberately dropped in `Chess.GUI/HumanPlayer.cs:21` |
| Motion, Droid | `loop.OnMouseMove` already fires and already inverts `ContentTransform` into `_pointerLast` | **Exists**, used only for the menus' tap-vs-drag test |
| Motion, Web | `WebGlCanvas.OnPointerMove`, backing-space mapped, covers mouse and bridged single-finger touch | **Exists**, unsubscribed |
| Motion, terminal | `\e[?1002h` button-motion tracking (`VirtualTerminal.cs:170`) → `ConsoleInputMapping.cs:108` maps it to `InputEvent.MouseMove` | **Exists**, unconsumed by `Chess.Console/HumanPlayer.cs` |
| Partial repaint | `ConsoleGameDisplayBase.RenderFrame` unions clip rects and renders partially | **Exists** — terminal only |
| Ghost state on `GameUI` | `GameUI.DragPoint` / `GrabOffset` / `GhostRect`, `HandlePointerMove` | **Exists** (phase 1) |
| A render branch for it | `GameUI.DetachedPiece()` + the last draw in `RenderBoard` | **Exists** (phase 1) |

**Nothing is blocked.** The whole change is a piece of `GameUI` state, one render branch, and four
subscriptions — the first two of which are now in.

## The design

Two new pieces of `GameUI` state, both pure render hints:

- `DragPoint` (`PointInt?`) — where the pointer is, in **content space**.
- `GrabOffset` (`PointInt`) — pointer-minus-square-origin, captured at pick-up.

The ghost rect is `DragPoint - GrabOffset`, sized one square. Preserving the grab offset is what stops
the piece **jumping under the cursor** at the moment you start to move: grab a knight near its bottom
-right corner and it stays held there, rather than snapping its centre to the pointer.

Rendering, with the ghost drawn **last** so it sits over everything:

| What | How |
|---|---|
| The ghost | `DrawPiece` into the ghost rect with both glyph colours at ~85% alpha |
| Its origin square | keeps the existing picked-up tint, and draws its piece at ~35% alpha |

The dimmed origin is deliberate over an empty one: "this is where it came from" is worth keeping
legible, and an empty square plus a tint reads as *deleted* rather than *lifted* — which matters here,
because `Del` genuinely does delete.

**There is no z-order conflict with the palette**, and that is not luck: phase 1 made pick-up and the
palette mutually exclusive states, so a piece can never be in hand while the modal is up.

### One method, two delivery routes

The canonical entry point is `GameUI.HandlePointerMove(x, y)`, returning
`(UIResponse.NeedsRefresh, [oldGhostRect, newGhostRect])` — the damage, stated by the only code that
knows where the ghost was. How a host *delivers* motion to it differs, and the split is not arbitrary:

**The terminal goes through the normal player path.** `Chess.Console/HumanPlayer.TryMakeMove` already
reads one terminal event per call and returns a `PlayerMoveResult(response, clips)` that
`RenderMove` honours. Motion is one more arm of that switch — the same one-line addition `MouseUp`
got — and the clip rects it returns are *used*. There is no second thread and no queue to starve.

**The GPU hosts write the drag point directly on their event thread** and let `HasPendingUpdate` drive
the repaint, ignoring the rects. This is the route `HandleHistoryPointer` already takes for the
scrollbar drag, for the reason its comment gives: "bypassing the per-move queue so it stays smooth and
never races game state". It is safe because in every pixel host the pointer callback and `Render` run
on the same thread, so the drag point is never read by the game thread.

Motion must **not** go through `QueuedInputPlayer` or `Chess.GUI/HumanPlayer`'s queue. Both are
deliberately one event deep, latest-wins, because a stale board coordinate is worse than a dropped
one; a per-pixel motion stream through a one-per-tick drain would starve the press and release that
actually matter. That is why `HumanPlayer` drops `MouseMove` today, and it stays dropped.

## Damage: the four-square bound

The ghost's footprint is **at most four squares**, and it is worth writing down *why*, because the
bound is a constraint to preserve rather than a fact to observe:

> A rect exactly one square in size, at an arbitrary sub-square offset, straddles at most a 2×2 block.

That holds **only while the ghost stays exactly one square.** The obvious embellishment — scaling the
dragged piece up slightly, as touch UIs often do — silently makes it 3×3 = nine. If a future change
wants a scaled ghost, it has to revisit this section, not just the draw call.

A repaint needs **old ∪ new**, so a *moving* ghost dirties up to two footprints. Consecutive motion
events overlap heavily, but a fast flick can separate them — worst case eight squares.

### The terminal is the best case here, not the worst

The obvious assumption is that sixel makes the terminal the host that cannot afford a ghost. It is the
opposite, and for three reasons that compound:

1. **It is the only host that honours the bound at all.** `ConsoleGameDisplayBase.RenderFrame` unions
   the clip rects and renders partially; `PixelGameDisplay.RenderMove` ignores them and repaints the
   whole frame. So on the GPU hosts a moving ghost costs *a frame* — which is what they spend anyway —
   while on the terminal it costs *a region*. At `squareSize` 120, four squares is 240×240 of a
   960×960 board: **one sixteenth of the area** that a full-board sixel encode would pay for.
2. **Motion is reported at cell resolution, and only on change.** One event per cell crossed, not per
   pixel — 12 per square horizontally and 6 vertically at a 10×20 cell. That is an order of magnitude
   fewer events than a GPU host's stream, and consecutive events are at most one cell apart, so
   `old ∪ new` stays tight. This matters more than it looks, because `RenderFrame` unions *all* the
   rects into **one**: two far-apart rects would become a bounding box spanning most of the board.
3. **`\e[?1002h` is button-motion tracking, not `?1003h` any-event tracking.** The terminal reports
   motion *only while a button is held* — that is, only during a drag. There is no idle-hover stream
   to filter out at all, which is the one trap below that the terminal gets for free.

The cell quantisation does mean the terminal ghost moves in twelfths of a square horizontally and
sixths vertically. On a surface where everything is cell-quantised that reads as coherent rather than
janky, and it is the same grid the board itself is aligned to.

**`AsciiDisplay` gets no ghost**, and needs no special case to be excluded: it is its own
`IGameDisplay` (`AsciiDisplay.cs:15`), not a `ConsoleGameDisplayBase` subclass, and it has no shared
raster path to composite translucency into. Only `SixelGameDisplay` inherits the machinery.

### Do the GPU hosts need damage-based repaint for this?

Not yet, and the reason is not that chess's framerate is high — **chess has no framerate.** The SDL
loop is gated on `CheckNeedsRedraw` (`display is { HasPendingUpdate: true } || …`), so it paints when
something changed rather than on a clock. TianWen's FITS viewer measured the two states this produces
as 0% GPU when nothing redraws and 8% when everything does; chess sits in the 0% state almost all the
time, and a click costs exactly one frame. There is no continuous cost to attack.

**A ghost is the first thing that would change that**, because it turns a drag into one repaint per
motion event — precisely the "repaint the whole window to alter one number" shape tianwen measured at
**8% GPU on an Adreno X1-85**, which is the GPU this machine's Chess.GUI selects. So the question
becomes live with this feature and not before. It still should not be answered by building anything:
chess's frame is 64 quads and ~32 glyphs against tianwen's 4096² blit plus toolbar, file list, info
panel, histogram and overlays, so the number will not be 8%. **Measure a ghost drag first** — the TUI
already reports `paintMs` in its `app_state`, and the GUI has the same renderer inspector.

If it ever does need fixing, chess's version is far smaller than tianwen's, because chess is ahead on
the hard part. Most of that plan is *deriving* damage: a visual signature over the arranged layout
tree, with four traps (record equality broken by `OnClick` delegates, hover resolved at paint time,
`Content.Fill` leaves opaque to the diff, `TextInputState` held by reference). Chess needs none of it —
`GameUI` already **knows** what changed and has always said so, returning
`ImmutableArray<RectInt>` damage with every response. The rects exist and are already tested; what is
missing is only a backend that does not discard them, since `PixelGameDisplay.RenderMove` takes
`clipRects` and drops them on the floor. That is `loadOp = Load` plus a scissor, not a diff engine.

One premise is worth recording now, because it is the one that would kill that approach silently:
**chess runs with MSAA off** — Chess.GUI passes nothing for `VulkanContext`'s `msaaSamples`, which
defaults to `Count1` — so a single colour attachment can be preserved with `loadOp = Load`. Under MSAA
the multisample attachment is transient (`storeOp = DontCare`) and cannot be reloaded, which would
force a persistent offscreen target and a blit-back. If MSAA is ever enabled, this needs revisiting
rather than re-testing.

#### That backend half shipped while this was being written (2026-08-26)

The repin to **SdlVulkan.Renderer 7.25** (chess `00e3423`) brought exactly the thing described above,
so "if it ever does need fixing" is no longer a project:

- `AddFrameDamage` / `MarkFullFrameDamage` declare a frame's damage, and `BeginFrameRenderPass` picks
  a `loadOp = Load` variant of the swapchain pass, confining render area and scissor to it — the
  viewport deliberately stays full-surface, since an app submits geometry in surface coordinates and a
  shrunken viewport would squash the frame into the region rather than crop it to it.
- Every clip is intersected with the damage, because DIR.Lib intersects a clip with its parents but
  knows nothing about damage — without it a widget clipping to its own pane repaints that whole pane.
- Damage is tracked **per swapchain image**, which is the part chess would have got wrong: with 2–3
  images in rotation, the image acquired this frame holds the frame from 2–3 frames ago, so it needs
  the union of every frame's damage since *that* image was last painted. Using the current frame's
  alone leaves stale pixels that appear only at particular frame counts.
- **MSAA takes the clearing path unconditionally**, for precisely the transient-attachment reason
  recorded above. So the premise is now enforced by the backend rather than only written down here —
  chess being `Count1` is what puts it on the fast path.

Damage must be declared from `SdlWindowView.OnBeforeFrame`, which runs once a frame is committed to
and before the pass opens; `CheckNeedsRedraw` stays a pure predicate and is the wrong place for it.

**What is still chess's to do is unchanged and small**: `PixelGameDisplay.RenderMove` takes
`clipRects` and drops them on the floor. Feeding them to `AddFrameDamage` is the whole of it — and
phase 1 has now made `HandlePointerMove` produce exactly those rects. Do it only if a measured drag
says it is needed; the point here is that the answer changed from "build a scissor path" to "pass the
rects you already have".

DIR.Lib 8.8's `LayoutDamage` came in the same repin and is **not** what chess needs for this. It
derives damage by diffing paint signatures over the arranged tree — the four traps above are its
problem, and it exists for consumers that cannot say what changed. Chess can. It would only earn its
place for the declarative chrome around the board, which is not what a drag touches.

One more thing arrived with 7.25 that phase 3 will want: the renderer inspector gained a **`move`
verb**. Both existing pointer verbs press a button, so hover-driven behaviour was undrivable — which
means trap 3 below (motion arriving with no button held, on hosts that deliver it that way) could not
be tested end-to-end at all before now.

## Per-host wiring

| Host | Motion source | Route | Work |
|---|---|---|---|
| **Chess.Console** (sixel) | `InputEvent.MouseMove`, already mapped | Player path, clip rects honoured | One arm on `HumanPlayer`'s switch — plus a `Coalesce` step, which turned out to be the real work |
| **Chess.GUI** | `OnPointerInput`, already content-mapped | Event thread, rects ignored | Serve beside `HandleHistoryPointer`; leave `HumanPlayer` dropping it |
| **Chess.Droid** | `loop.OnMouseMove`, already `ContentTransform`-inverted | Event thread, rects ignored | Set the drag point beside `_pointerLast`; return `true` for the frame |
| **Chess.Web** | `WebGlCanvas.OnPointerMove` | Event thread, rects ignored | Subscribe and render directly — **not** via `_input`/`AdvanceAsync`, which would tick a session per mouse move |

## Traps

1. **`GameUI.Resize` returns a NEW instance.** `DragPoint`/`GrabOffset` must join the state it carries
   over (`GameUI.cs:479-500`, beside `PlacementSide`/`PendingPlacement`) or a resize mid-drag drops the
   ghost while `PickedUp` survives — an invisible drag, which is worse than no ghost at all.
2. **Record the drag point from already-mapped coordinates.** The GUI and Droid both invert pointer
   events through `ContentTransform` before dispatch; taking raw device coordinates instead would make
   the ghost drift away from the finger under the Android across-the-table rotation, while every other
   hit-test stayed correct.
3. **Gate motion on `PickedUp is not null`** before touching any state — on the GPU hosts, where
   pointer motion arrives whether or not a button is down. The terminal is exempt by construction
   (`?1002h`), but the gate belongs in `GameUI` where all four hosts get it.
4. **Coalesce a backed-up motion queue.** Rendering every event of a fast drag is worse than rendering
   the latest one: on the terminal each costs a partial sixel encode, and the intermediate positions
   are already stale by the time they paint. `HumanPlayer` can peek `terminal.HasInput()` and skip the
   render when the next pending event is also motion.
5. **Clear the drag point on drop, on cancel, and on leaving setup** — the same three exits phase 1
   already handles for `PickedUp`, plus `IsSetupMode`'s setter. *Phase 1 solved this by DERIVING
   instead of clearing:* `DragPoint` reads as null whenever `PickedUp` is, so all four exits close the
   ghost at once and a fifth cannot be forgotten. The backing field is reset on pick-up, which is the
   one case derivation does not cover — a stale point would otherwise paint a ghost across the board
   before the pointer had moved at all.

## Testing

`GameUITests` already renders to an `RgbaImage` (`RenderToImage`), so the ghost is directly assertable
without a window: put a piece in hand, set a drag point, render, and check the pixels under the ghost
rect differ from the bare board while the origin square is dimmed rather than empty. Read the **alpha**
channel to tell "never written" from "written in the wrong colour" — the lesson from the invisible
chess-mcp captions.

The damage rects are worth asserting directly too, since they are the whole of the terminal's cost
model: a ghost moved by one cell should return two rects whose union is at most a 3×3 square block,
and a ghost that has not crossed a square boundary should not widen it.

### Live verification: only one of the two routes exists (corrected 2026-08-26)

This section used to claim "the TUI inspector can `click`-drag and read the screen, and the GUI's
renderer inspector can `drag` and screenshot". Half of that is wrong, and it is the half phase 2
needed.

**Console.Lib's inspector has exactly one pointer verb, `click`**, and it injects a press and a
release *at the same cell* (`ConsoleDebugInspector.cs:324-325`) with no motion in between. There is no
`move` and no `drag`. So the terminal — the host that benefits most from a ghost, and the only one
that reads the damage rects — **cannot have a drag synthesized at all**, and phase 2 rests on unit
tests plus reading the code rather than on a screen read.

The GPU side is the opposite: SdlVulkan.Renderer 7.25 added a `move` verb precisely because
press-based verbs could not drive hover behaviour, so phase 3 *will* have a live route.

Closing this needs a motion verb in Console.Lib's inspector — a **sibling change**, deliberately out
of scope here since this plan is chess-only. Until it lands, a terminal ghost is only ever seen by a
person dragging a real mouse over a real terminal.

**Phase 3 used the GPU route, and it worked** (2026-08-26). Chess.GUI built `-c Debug
-p:UseLocalSiblings=true`, driven over the inspector's TCP protocol: `click` on b1 near its
bottom-right corner to take the knight in hand, then `move {x1,y1,x2,y2}` — note the four-parameter
path form, not a single point — then `screenshot`. Three things were confirmed on the real surface,
none of which a unit test can show:

1. the ghost is drawn **off-grid**, straddling squares, held at the offset it was grabbed by rather
   than snapped to a centre;
2. the origin square keeps a **dimmed** knight under the picked-up tint — lifted, not deleted;
3. moving the pointer off the board **hides the ghost and restores b1 to full strength**, with the
   status line still reading "moving White Knight from b1" — the piece is still in hand.

Chess.Droid takes the same route in the same commit but has **not** been run on a device; it is
compiled only.

## Reuse: this is most of a move animation

A move animation — the piece sliding from its origin to its destination instead of teleporting, which
is what an engine reply does today — is **the same rendering problem**: a piece drawn detached from
any square, with one square suppressed behind it. The ghost is the expensive half of that, already
built.

Shape phase 1's render branch to take its inputs rather than read `DragPoint` directly, and the
animation reuses it with no refactor:

| | The piece | Where it is drawn | Square suppressed |
|---|---|---|---|
| **Drag** | the one in hand | `DragPoint - GrabOffset` | its **origin**, dimmed |
| **Animation** | the one that moved | `lerp(from, to, t)` | its **destination**, hidden |

Note the inversion, because it is the one thing that would bite an implementation that assumed the two
were the same: during a drag the model still has the piece on its origin, so the origin is what must be
toned down. During an animation the ply is **already committed** — `Game.TryMove` has run, the model
has the piece on its destination — so the destination is what must be hidden until the slide lands.

The damage model transfers unchanged, and is in fact *better behaved*: an animation chooses its own
step size, where a drag's per-frame delta is whatever the user's flick decides. The four-square
footprint bound holds identically at every instant.

**What does not transfer is the driving.** A drag is driven by input — each motion event supplies the
next position, and no clock is involved. An animation needs frames to keep arriving with **no input at
all** for ~200 ms, which is a different relationship with the loop on every host:

- **GUI / Droid** have the hook already: `SdlEventLoop` polls `CheckNeedsRedraw` on a ~16 ms
  `WaitEventTimeout`, so a tween in flight simply returns `true`.
- **Chess.Web has no idle hook at all** — it renders on demand from Blazor handlers, so an animation
  needs `requestAnimationFrame` or a timer. This is already recorded as the odd one out in
  [`game-library.md`](game-library.md)'s tick-model section ("Web is the ONLY one with no idle hook"),
  and it is the real work in animating anything, not the drawing.
- **Terminal** can tick a tween from `GameLoop`'s own thread, but each frame is a sixel encode. A
  200 ms slide is a handful of partial encodes — affordable precisely because of the four-square
  bound, and only because of it.

Two further things an animation needs that a drag does not, both design decisions rather than
plumbing: a **time source** (`TimeProvider`, as `GameSession.Start` already takes, so tweens stay
testable without sleeping), and a rule for **what the rest of the frame does while a piece is in
flight** — the captured piles, the history row and the last-move arrow all currently update the instant
the ply commits, which would have them describing a move whose piece is still visibly travelling.

None of this argues for building the abstraction now; it argues for one cheap thing: **make the render
branch take `(piece, rect, suppressedSquare)` rather than reach into `DragPoint`.** That costs nothing
today and is the whole difference between animation reusing this and reimplementing it. See
[`game-library.md`](game-library.md) for why "animation as a backend capability" and
"timed-state-transition ≠ animation" are already the framing here.

## Phasing

| Phase | Scope | Where | Status |
|---|---|---|---|
| 1 | `GameUI` ghost state (`DragPoint`, `GrabOffset`), `HandlePointerMove` returning old ∪ new damage, the render branch (translucent ghost + dimmed origin) **taking `(piece, rect, suppressedSquare)` so a move animation can reuse it**, `Resize` preservation, clearing on every exit, and pixel-level tests | chess | **Done** |
| 2 | Chess.Console wiring — one arm on `HumanPlayer`'s switch plus motion coalescing; the only host that exercises the clip rects, so it validates phase 1's damage model | chess | **Done** (not live-verifiable — see [Live verification](#live-verification-only-one-of-the-two-routes-exists-corrected-2026-08-26)) |
| 3 | Chess.GUI + Chess.Droid — both already deliver a content-mapped motion event on their event thread | chess | **Done** — GUI live-verified through the inspector's `move`; **Droid compiles but has NOT been run on a device** |
| 4 | Chess.Web via `WebGlCanvas.OnPointerMove` | chess | Not started |

Phase 1 is testable and reviewable with no host wired at all, which is what makes it the whole of the
risk. **Phase 2 comes before the GPU hosts on purpose**: the terminal is the only backend that reads
the damage rects, so wiring it second is what proves phase 1's cost model is real rather than
decorative. The GPU hosts would happily accept wrong rects for ever, because they throw them away.

## Open questions

- **Touch: a finger covers the piece it is dragging.** Mobile UIs usually lift the ghost above the
  contact point by an offset. That would break the honest grab-offset behaviour on precisely the hosts
  where the ghost matters most, and it interacts with the four-square bound (an offset ghost can
  straddle a different 2×2). Worth deciding when Droid/Web are wired, not before.
- ~~**Should the ghost show while the pointer is off the board?**~~ **Decided in phase 1: it hides.**
  That keeps the four-square bound absolute, stops the piece drawing over the history panel and the
  captured piles, and truthfully signals the release semantics — a release off the board is already a
  no-op that leaves the piece in hand. The cost, accepted, is that the piece appears to vanish rather
  than be carried. Reversing it means changing one line in `HandlePointerMove` and re-reading the
  bound, since a ghost over the chrome is no longer bounded by squares at all.
- ~~**Does the terminal's partial-render path stay partial here?**~~ **Answered from the code in phase
  2: yes.** `RenderFrame` falls back to a full frame when `TrayIsStale`, and that is a comparison of
  `(ply index, Mode)` (`ConsoleGameDisplayBase.cs:405-411`). A setup drag changes neither — no ply is
  committed and the mode stays `Setup` — so the tray is stale at most once, on entry. Still worth a
  glance at `paintMs` in `app_state` when a motion verb makes a real drag drivable.
- **New, from phase 2: how big can deferred damage get?** Coalescing unions the rects it defers rather
  than listing them, so the accumulation is bounded and `RenderFrame` (which unions everything anyway)
  is handed the same region either way. But a *slow* drag that always has input queued behind it grows
  the union across the whole path travelled, and the union of two distant footprints is their bounding
  box. Not a correctness problem — it repaints a superset — but it is the one shape that could make a
  coalesced drag cost more than an uncoalesced one, and only a measured drag will say whether it
  happens in practice.
