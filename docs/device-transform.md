# Design: a constrained content→device transform (DPI + rotation, unified)

**Status:** design proposal, not started. **Repo scope:** this describes a change to the
**sibling rendering libraries** (`DIR.Lib` + its backends `SdlVulkan.Renderer`, `WebGl.Renderer`,
and the CPU `RgbaImageRenderer`); chess is only a *consumer*. It lives here because chess is the
driver use case (the [Android across-the-table hot-seat](../Chess.Droid/docs/tablet-hotseat-flip.md)),
but it can't be implemented in this repo — the sibling libs must ship the capability first.

## Why

Three things in the rendering stack are really the same thing wearing different clothes:

- **DPI scaling** — currently a scalar `dpiScale` threaded by hand through the pipeline
  (`ListScrollController.SetExtent(dpiScale)`, `VkFontAtlas`, and — as of DIR.Lib 6.15 —
  `PixelWidgetBase.DpiScale`).
- **Device/screen rotation** — today delegated to the Vulkan compositor via `preTransform`
  (`VulkanContext` prefers `Identity` and lets the presentation engine rotate).
- **The hot-seat 180° flip** — an *app-driven* rotation of the whole composited frame so the player
  across a flat tablet reads everything (text included) upright.

Each is an affine map from content coordinates to device pixels. DPI is the **scale** component;
rotation is the **rotation** component; safe-area/letterbox offset is the **translation** component.
Modelling them separately is why `dpiScale` is a lonely scalar and why the hot-seat looks like it needs
bespoke renderer surgery. Model them as **one transform** and they collapse into a single concept.

## The primitive

A **content→device transform** `M`, deliberately *constrained*:

- rotation ∈ **{0°, 90°, 180°, 270°}** only,
- **uniform** scale (no anisotropy),
- **no shear**,
- plus a translation.

Represent it as a constrained struct, **not** a raw 2×3 matrix, so the invariant is enforced by
construction (shear/anisotropy are unrepresentable):

```csharp
public readonly record struct DeviceTransform(Rotation90 Rotation, float Scale, float Tx, float Ty)
{
    public static readonly DeviceTransform Identity = new(Rotation90.None, 1f, 0f, 0f);
    public PointF Apply(PointF p);          // content → device
    public PointF Invert(PointF p);         // device → content (trivial: Rᵀ · (p−t) / scale)
    public DeviceTransform Compose(DeviceTransform inner);
}

public enum Rotation90 { None, Cw90, Half, Cw270 }
```

`DpiScale` becomes a derived accessor (`DpiScale => transform.Scale`), not an independent field.

### Why the constraints are load-bearing

They are not convenience simplifications — each one keeps a large existing system valid:

1. **Rect → rect.** A 90°-multiple rotation + uniform scale maps an axis-aligned rectangle to an
   axis-aligned rectangle (width/height swap at 90°/270°). So the entire `RectInt`-based `Layout`
   tree, hit-testing, and clip rectangles stay correct **unchanged** — no general polygon
   rasterization, no clipping rework. Chess's `GameUI`/`PixelGameDisplay` keep laying out in *content*
   space; only the renderer's projection and one input mapping learn about `M`.
2. **Trivially invertible.** Input is `M.Invert(pointer)`, applied once at the host boundary, so draw
   and hit-test cannot drift. This is the whole-frame generalization of the `DisplayCell`/`LogicalCell`
   pair that already keeps the board flip consistent (`GameUI.cs`) — and it could eventually subsume
   `FlipBoard` itself.
3. **Text stays legible.** Uniform scale + no shear keeps SDF/glyph-atlas sampling clean; 90° steps
   keep glyph quads on the pixel grid.

## Draw side

```mermaid
flowchart LR
    C[content-space<br/>Layout / GameUI] -->|unchanged| P[primitive calls<br/>FillRect / DrawText / DrawImage]
    P --> M{{DeviceTransform M}}
    M -->|GPU: fold into ortho projection| G[Vulkan / WebGL<br/>text rotates for free]
    M -->|CPU: remap pixel writes| R[RgbaImageRenderer]
```

- **GPU backends (Vulkan, WebGL)** get this nearly for free: compose `M` into the existing orthographic
  projection. Because `DrawText` emits glyph **quads through that same projection**, text rotates with
  everything else — so **no per-call angle parameter on `DrawText`/`DrawImage` is needed**. (An
  earlier objection — "the abstract `Renderer<TSurface>.DrawText` has no rotation argument" — only bites
  for *per-primitive* rotation. A *global* transform sidesteps it.)
- **CPU backend** (`RgbaImageRenderer`, used by Chess.Web's fallback and the console) must remap pixel
  writes explicitly. **180° is trivial** (axis negation). **90°/270° is the hard case** (axis swap +
  glyph orientation) and should be a later phase.

## Input side

The host maps each pointer event through `M.Invert(...)` before dispatch — one place, at the SDL/DOM
boundary. Everything downstream (`GameUI.FindSelected`, history hit-testing, the new
`ListScrollController`) continues to work in content coordinates.

## Safe-area insets under rotation

The safe-area cutout is physically fixed, but a rotated frame flips which *logical* edge it sits on.
Systematize the doc's old open question: **transform the safe-area inset rectangle by `M`** and set the
result on `PixelGameDisplay.SafeAreaInsets`. Under 180° that swaps top↔bottom and left↔right; under
90°/270° it cycles. No special-casing.

## Two decisions (settled)

1. **Content transform, not device transform.** `M` is an *app-driven content* transform layered **on
   top of** the compositor's surface orientation (Vulkan `preTransform`, currently `Identity`). Device/
   screen rotation stays the compositor's job exactly as today; `M` carries DPI-scale × app-rotation
   (the 180° hot-seat). Making `M` *replace* `preTransform` would mean rendering pre-rotated on every
   device turn and owning orientation correctness — more invasive, rejected.
2. **Unify DPI incrementally.** Introduce `DeviceTransform` **alongside** the existing `dpiScale`
   (`DpiScale => transform.Scale`) first; retire the scalar second (it touches every consumer —
   `VkFontAtlas`, `ListScrollController.SetExtent`, `PixelWidgetBase.DpiScale`, …).

## Phasing

| Phase | Scope | Where |
|---|---|---|
| 1 | `DeviceTransform` type + GPU projection compose; **180° only**; `DpiScale => Scale` accessor | DIR.Lib + SdlVulkan.Renderer + WebGl.Renderer |
| 2 | Host input inverse-mapping + safe-area inset transform; wire the hot-seat 180° in Chess.Droid PvP | chess (consumer) |
| 3 | CPU backend 90°/270°; retire the scalar `dpiScale`; consider subsuming `FlipBoard` | DIR.Lib + backends |

## How chess consumes it (once phases 1–2 land)

- Chess.Droid hot-seat PvP sets `renderer.DeviceTransform = new(Rotation90.Half, dpi, …)` to face the
  player to move; the board-only `GameUI.FlipBoard` is turned **off** in this mode (the whole frame
  rotates instead).
- `MainActivity` maps `HandleTap` coordinates through `M.Invert` (replacing the ad-hoc pass-through).
- Trigger, surface scope, and tablet-gating remain product questions — see
  [`tablet-hotseat-flip.md`](../Chess.Droid/docs/tablet-hotseat-flip.md).
