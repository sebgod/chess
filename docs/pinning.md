# Pinning the SharpAstro libraries

How chess pins the libraries it builds on (DIR.Lib, Console.Lib, SdlVulkan.Renderer, WebGl.Renderer, LAN.Lib, …),
how to tell whether the pins are right, and what to do when they are not. Written so any SharpAstro app can adopt it:
the rule and the checker (`scripts/check-pins.py`) depend on nothing chess-specific. The release mechanics themselves
(which repo to bump, in which order, how CI publishes) are in `.claude/skills/release-lib/SKILL.md`.

## The rule

1. **Pins float within a minor**: `X.Y.*`, in `Directory.Packages.props`. A CI rebuild of the same `X.Y` arrives on the
   next restore; moving to a new `X.Y` is a one-line commit that says why.
2. **Every library the app loads is built against the shared dependencies the app pins**, or an *additive* step behind
   them. The app's pin wins at restore: NuGet loads one DIR.Lib for the whole process, so a library built against an
   older DIR.Lib runs on the app's. Across additive minors that is fine. **Across a major it is a bug waiting**: a
   member 11.0 removed fails at run time (`MissingMethodException`), never at build time, and only on the code path
   that calls it.
3. **The train runs to the end.** When DIR.Lib moves, every library above it repins and releases (in the order the
   release-lib skill gives), and only then the app. A library left behind is invisible: the app still builds and its
   tests still pass.

## Checking it

```bash
python scripts/check-pins.py                 # this repo's Directory.Packages.props
python scripts/check-pins.py --majors        # also list pins with a newer MAJOR (a deliberate upgrade)
python scripts/check-pins.py --markdown      # tables for a CI step summary
```

It asks nuget.org two things for every pin:

- **Behind**: is there a newer `Major.Minor` within the pin's major? A routine bump. (A newer major, such as the .NET 11
  packages for a .NET 10 app, is never routine, so it is only listed with `--majors`.)
- **Built against vs loaded with**: each pinned package's *published* `.nuspec` lists the versions it was built
  against. Where the app pins one of those dependencies too, the two are compared. A **MAJOR** gap exits 1; a
  **minor** gap is reported, and is fine when the dependency's CHANGELOG calls the steps between additive.

It needs only nuget.org (no GitHub, no sibling clones), so another repo adopts it by copying the script. CI runs it
on every push to `main` and pull request and writes the tables into the run's summary, as information, not a gate:
the gate is the reader of the summary.

## What to do with each finding

| Finding | Do |
|---|---|
| A SharpAstro pin is behind | Read the library's CHANGELOG from your version up; repin; build and test; commit with what changed. |
| A MAJOR gap (library built against an older major of a dependency the app pins) | Repin **that library** to the app's version, build and test it against the package, release it, then move the app's pin to the release. |
| A minor gap | Fine if the dependency's CHANGELOG calls every step additive; repin the library at its next release anyway. |
| A third-party pin is behind (Vortice, the test SDK) | Not part of the train. Vortice moves with SdlVulkan.Renderer, which pins its own copy; bump it there first. |

## Testing a library's repin, the way CI will see it

- **Build against the package, not the sibling source.** A library whose sibling clone sits next to it references the
  source (`UseLocalDirLib`, `UseLocalSiblings`); pass `-p:UseLocalDirLib=false` (the renderer, WebGl.Renderer) so the
  restore takes the *published* package, the one consumers will load. Then check `obj/project.assets.json` names the
  version you meant.
- **nuget.org lists a package before its search index has it.** For several minutes after a push,
  `v3-flatcontainer/<id>/index.json` shows the new version while `dotnet restore` still reports `NU1102 ... Nearest
  version: <the previous one>`. Wait, then `dotnet restore --no-http-cache`. It is propagation, not a bad pin.

## What this caught (2026-11-15)

Getting chess onto an iPad repinned the train, and the checker, run afterwards, found what the repins had missed:

- **WebGl.Renderer 1.38 was built against DIR.Lib 10.4** while chess loaded it with 11.61, across the 11.0 major.
  Chess.Web compiled and its tests passed; nothing would have said so until a removed member was called in a browser.
  WebGl.Renderer 1.39 rebuilt it against 11.62 with no source change.
- **Console.Lib 5.12 was built against DIR.Lib 11.40**, 21 minors behind chess's 11.61 (all additive, so harmless, but
  nothing had said so). Console.Lib 5.16 was already published, built against 11.61.
- **SdlVulkan.Renderer was on DIR.Lib 11.57** while chess was on 11.59 (7.94 repinned it to 11.61).
