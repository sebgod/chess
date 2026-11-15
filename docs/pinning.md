# Pinning the SharpAstro libraries

Chess pins the libraries it builds on (DIR.Lib, Console.Lib, SdlVulkan.Renderer, WebGl.Renderer, LAN.Lib, AppShell,
and the rest) `X.Y.*` in `Directory.Packages.props`. The rule, the check and what to do with each finding are the org's,
in [SharpAstro/.github docs/pinning.md](https://github.com/SharpAstro/.github/blob/main/docs/pinning.md), written from
what chess found and moved there so every repo uses the same one. In short: every library chess loads must be built
against the DIR.Lib (and other shared dependencies) chess pins, or an additive step behind, never an older MAJOR,
which compiles and then fails at run time.

## Here

- **CI** calls the org's reusable workflow as the `pins` job (`.github/workflows/dotnet-desktop.yml`): two tables in the
  run's summary, a warning on a MAJOR gap, not a gate.
- **Locally**, from this repo's root (the org's clone sits at `../../sharpastro/.github` on the desktop):

  ```bash
  python ../../sharpastro/.github/scripts/check-pins.py Directory.Packages.props
  python ../../sharpastro/.github/scripts/check-pins.py Directory.Packages.props --majors
  ```

- **Before and after a release train**, run it (the `release-lib` skill says when). A library the train skipped shows
  up as a gap: WebGl.Renderer 1.38 sat on DIR.Lib 10.4 while chess loaded 11.x, compiling and passing tests the whole
  time, until the first run of this check found it (2026-11-15; fixed by WebGl.Renderer 1.39).
