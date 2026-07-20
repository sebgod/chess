# Deferred / reference: WASM AOT for Chess.Web

**Status:** reference + deferred lever. Chess.Web runs **interpreted** today (Release + jiterpreter),
which is fine for depth 3–4 negamax. This doc records the AOT findings (measured in the sibling
`sharpastro/tianwen` `feat/web-showcase` session on 2026-07-17, in a Chess.Web-derived host) so the
recipe and its caveats aren't lost, plus a few unrelated Chess.Web deltas worth grabbing. Source of
truth for the full write-up: tianwen `docs/plans/web-showcase.md`.

## Headline — AOT is the proven lever for deeper browser search

TianWen.UI.Web shipped the same Blazor-WASM + WebGl.Renderer + Pages approach as Chess.Web, then
added **Mono WASM AOT** (`RunAOTCompilation=true`) for the deploy publish and measured it A/B in the
browser:

| | interpreted (Release + jiterpreter) | AOT |
|---|---|---|
| catalog init | 13.6 s | 554 ms (24×) |
| planner sweep (pure numeric) | 24.9 s | 591 ms (42×) |
| payload (brotli) | 16 MB | 21 MB (+5 MB, all in `dotnet.native.wasm`) |

Chess runs interpreted today and that is fine for depth 3–4 negamax. **If deeper AI search in the
browser is ever wanted, AOT is the proven lever** — compute-heavy loops (negamax) are its best case.

## Recipe (proven in tianwen)

```
dotnet publish Chess.Web -c Release -p:RunAOTCompilation=true
```

- The `wasm-tools` workload is already installed by chess's `pages.yml` (it's needed for the native
  relink chess already does; AOT uses the same toolchain).
- AOT'd apps still ship the per-assembly `.wasm` IL files (metadata/reflection/fallback) — the native
  code fuses into `dotnet.native.wasm` (chess's would grow from ~0.5 MB to several MB).
- Expect several extra minutes of CI build time (LLVM per assembly).

## win-arm64 local-machine caveats (the dev box)

The mono AOT cross-compiler on win-arm64 fail-fasts (`0xC0000409`, sgen assertion in
`sgen-alloc.c:409`) on some assemblies. tianwen hit it on P/Invoke-dense vendor SDKs and on the
`WasmDedup`-synthesized `aot-instances.dll`. Workarounds that made a local AOT publish succeed:

```xml
<!-- per-assembly opt-out; identity must be the .dll file name (MSBuild %(Filename) batching) -->
<ItemGroup>
  <_AOT_InternalForceInterpretAssemblies Include="Some.Assembly.dll" />
</ItemGroup>
```

plus `-p:WasmDedup=false` on the command line. ubuntu-x64 CI is the mainstream toolchain and may need
neither — try plain first there.

## Unrelated-but-useful Chess.Web deltas tianwen added (grab if wanted)

- **Hi-dpi canvas**: chess's canvas backing == CSS pixels (soft on 2× displays). tianwen sizes the
  drawing buffer to `element CSS size × devicePixelRatio`, passes dpr as the widget dpiScale, and
  scales mouse coords by dpr; a window-resize watcher re-sizes via `WebGlRenderer.Resize`. See
  `TianWen.UI.Web/Pages/Planner.razor` + the `tianwenCanvasMetrics` / `tianwenWatchResize` JS helpers.
  (Candidate to fold into WebGl.Renderer as a shared `<WebGlCanvas>` component.)
- **Page-gesture kill for canvas drags**: `html,body { overflow:hidden; overscroll-behavior:none }`
  + `canvas { user-select:none; touch-action:none }` stops list-drags from tugging the document.
- **Publish fingerprinting reminder**: the `#[.{fingerprint}]` marker + empty `importmap` element
  chess's `index.html` already carries are load-bearing on static hosts — tianwen initially dropped
  them and 404'd on `_framework/blazor.webassembly.js` (only fingerprinted names exist).
