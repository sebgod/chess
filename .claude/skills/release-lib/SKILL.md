---
name: release-lib
description: Release a SharpAstro sibling library to NuGet and update this chess project to consume it. Use when the user asks to release/publish DIR.Lib, Console.Lib, SdlVulkan.Renderer, WebGl.Renderer, LAN.Lib, Fonts.Lib, or Codecs, to "publish the train" / run the release chain, or to bump the chess project onto a new library version.
argument-hint: <library-name>
---

Release a SharpAstro sibling library to NuGet and update this chess project to consume it.

Usage: /release-lib <library-name>
Example: /release-lib DIR.Lib
Example: /release-lib SdlVulkan.Renderer

## Library locations

Every repo's CI workflow is `.github/workflows/dotnet.yml`. The **version file** is
the ONE place `X.Y` is written, and its path is NOT the same in every repo — the four
libraries with a `src/` props file keep it there, the other three at the repo root:

| Library | NuGet Package(s) | Repo | Version file (`VersionMajorMinor`) |
|---------|------------------|------|------------------------------------|
| Fonts.Lib | `SharpAstro.Fonts`, `SharpAstro.Fonts.Shaping` | `../../sharpastro/Fonts.Lib` | `Directory.Build.props` (root) |
| Codecs | `SharpAstro.Codecs`, `SharpAstro.Png`, `SharpAstro.Jpeg`, … | `../../sharpastro/Codecs` | `Directory.Build.props` (root) |
| DIR.Lib | `DIR.Lib` | `../../sharpastro/DIR.Lib` | `src/Directory.Build.props` |
| Console.Lib | `Console.Lib` | `../../sharpastro/Console.Lib` | `src/Directory.Build.props` |
| SdlVulkan.Renderer | `SdlVulkan.Renderer` | `../../sharpastro/SdlVulkan.Renderer` | `src/Directory.Build.props` |
| WebGl.Renderer | `WebGl.Renderer` | `../../sharpastro/WebGl.Renderer` | `src/Directory.Build.props` |
| LAN.Lib | `LAN.Lib` | `../../sharpastro/LAN.Lib` | `Directory.Build.props` (root) |

Read the value back rather than guessing which file it is in:
`dotnet msbuild <version-file> -getProperty:VersionMajorMinor -nologo` — that is
literally what each CI workflow does, so if the path is wrong here the same command
fails the same way.

All seven repos are also reachable via junctions next to this repo (`../DIR.Lib`,
`../Console.Lib`, `../SdlVulkan.Renderer`, `../WebGl.Renderer`, `../LAN.Lib`,
`../Fonts.Lib`, `../Codecs`) — those junctions are what chess's `UseLocalSiblings`
auto-detection looks at. Note it requires **all five** of DIR.Lib, Console.Lib,
SdlVulkan.Renderer, WebGl.Renderer and LAN.Lib to be present; a missing junction
silently flips the whole build to NuGet.

## Floating pins (X.Y.*)

Downstream `Directory.Packages.props` files use **X.Y.\* floating pins** with
`CentralPackageFloatingVersionsEnabled` (chess, Console.Lib, DIR.Lib all do this).
Consequences:

- A **build-counter republish** (same X.Y, new run number, e.g. `6.9.1421` →
  `6.9.1441`) flows in on plain `dotnet restore` — **no props change needed**
  downstream.
- Only an **X.Y bump** (minor/major) requires editing downstream
  `Directory.Packages.props` to the new `X.Y.*` pin.
- The pinned `X.Y` must have at least one published version on NuGet before any
  downstream repo referencing it is **pushed** (CI restores from NuGet only).

## Steps for a single library release

1. **Bump version** — ONE line, in the repo's version file (see the table above):
   ```xml
   <VersionMajorMinor>X.Y</VersionMajorMinor>
   ```
   Increment minor for new features, major for breaking changes.

   Since 2026-08-02 this is single-sourced: CI reads the value back with
   `dotnet msbuild <version-file> -getProperty:VersionMajorMinor` and stamps
   `-p:Version=X.Y.<run><attempt>+<sha>` across every package in the repo. Do **not**
   restate the version in a `.csproj` or as `VERSION_PREFIX:` in the workflow — an
   older layout did both, they drifted, and a local `dotnet pack` shipped
   `DIR.Lib.Shaping 6.8.0` alongside `DIR.Lib 7.5.0`. (A `VERSION_PREFIX` grep still
   hits SdlVulkan.Renderer's workflow, where it is only passed through to a job — not
   a version declaration.)

   **The invariant (all seven repos, since 2026-08-04): the version file holds exactly
   these three properties, and no `.csproj` holds any version property at all.**
   ```xml
   <VersionMajorMinor>X.Y</VersionMajorMinor>
   <VersionPrefix Condition="'$(VersionPrefix)' == ''">$(VersionMajorMinor).0</VersionPrefix>
   <AssemblyVersion>$(VersionMajorMinor).0.0</AssemblyVersion>
   ```
   Check it before and after any release:
   ```bash
   grep -rn "VersionMajorMinor>\|VersionPrefix>\|AssemblyVersion>\|FileVersion>\|<Version>" \
     <repo> --include=*.csproj --include=*.props | grep -v "/obj/\|/bin/"
   ```
   Any hit in a `.csproj` is a regression, whatever value it holds.

   **`AssemblyVersion` is the trap, because CI does not stamp it.** The workflow passes
   `-p:Version` and `-p:FileVersion` and nothing else, so a csproj literal loses to CI
   for the package version but *wins* for assembly identity — meaning a stale
   `VersionPrefix` only spoils a local pack, while a stale `AssemblyVersion` spoils the
   shipped assembly and no local build reveals it. That is how `DIR.Lib` and
   `DIR.Lib.Shaping` published `6.4.0.0` from 6.5 through 7.8, and
   `SdlVulkan.Renderer` published `6.11.0.0` (plus `6.0.0.0` on both WebView projects)
   through 7.6, each against an informational version two majors ahead. Nothing
   compares the two values, so nothing ever complained.

   The property is deliberately **unconditional** — not
   `Condition="'$(AssemblyVersion)' == ''"` — because a conditional lets a csproj
   literal win again, silently, which is the whole bug. Only Major.Minor is
   significant: the build counter stays out, so a build-counter republish of the same
   X.Y never churns assembly identity.

   To read the *effective* value, build and inspect the generated attributes —
   `-getProperty:AssemblyVersion` returns empty, because the SDK fills it in during
   build, not evaluation:
   ```bash
   dotnet build <proj> -c Release -p:Version=X.Y.9991+deadbeef -p:GeneratePackageOnBuild=false
   grep AssemblyVersionAttribute <proj-dir>/obj/Release/net10.0/*.AssemblyInfo.cs
   ```

   An assembly-identity correction does **not** need a minor bump. The value moves *up*,
   toward what the package already advertised, and the runtime rejects a loaded assembly
   *lower* than the compiled reference, never higher — so consumers built against the old
   identity keep loading, and floating `X.Y.*` pins take the fix on the next restore with
   no props change anywhere downstream. Record it as a "later in X.Y" note appended to
   that version's existing changelog entry.

2. **Write the changelog entry in the same commit as the bump.** Each workflow's
   `env:` block carries a comment block, newest entry last, that is the de-facto
   release notes; it lives in the yml because entries contain `--`, which XML forbids
   inside a comment. Say what changed, what is NOT included, which pins moved, and
   flag any behaviour change explicitly (`BEHAVIOUR CHANGE`) — a consumer left on a
   default whose meaning changed has no other warning.

   Write it **before the first push**, not after. Remembering it later means either an
   extra commit or amending an already-pushed `main`; the amend route force-pushes a
   published commit and burns a second build-counter publish for the same X.Y.

3. **Build and test** the library locally. **The solution is not always under `src/`** — a bare
   `cd <repo>/src && dotnet test` fails with `MSB1003: Specify a project or solution file`
   in DIR.Lib, whose solution sits at the repo ROOT. Pass the solution explicitly:
   ```bash
   cd <repo> && dotnet test DIR.Lib.sln        # DIR.Lib: solution at the repo root
   cd <repo>/src && dotnet test                # Console.Lib, SdlVulkan.Renderer, WebGl.Renderer
   cd <repo>/src && dotnet test TianWen.slnx   # tianwen (see the note further down)
   ```

   **Never pipe it through `tail`/`head`.** A pipeline's exit code is the LAST command's, so
   `dotnet test | tail -40` reports success for a failed run, and the truncation drops the
   FIRST assembly's summary — which is how a tianwen run that looked like "371 tests, exit 0"
   was really 4442 tests whose largest assembly had scrolled off. Let the output land in full
   (background it if long) and read the `Passed!`/`Failed!` line per assembly. Count the
   assemblies: a missing one is a silent gap, not a pass.

4. **Commit and push** the bump + changelog in the library repo. Check first that
   `main` has not diverged (`git fetch && git status`): a stale unpushed bump whose
   base is behind origin can duplicate a bump origin **already landed**, in which case
   the local commit is superseded and should be dropped
   (`git rebase --onto origin/main <stale-sha> main`), not merged. Verify by reading
   origin's version file and changelog, not by trusting the local one.

   **`git fetch` BEFORE writing the changelog entry, not just before pushing.** A clean
   `git status` says nothing about origin. The likelier find is not a duplicate bump but
   **unreleased CODE commits** sitting on origin above the last release commit — this repo
   family lands work on `main`, which publishes it as a build-counter republish of the
   CURRENT X.Y, and the X.Y bump comes later as its own commit. So the release entry you are
   writing must describe **everything since the previous release commit**, not just your own
   change:
   ```bash
   git fetch && git log --oneline <last-release-commit>..origin/main
   ```
   On 2026-08-10 SdlVulkan.Renderer was five renderer fixes ahead (device loss, a present-wait
   semaphore VUID, subpass-dependency compatibility, validation reporting, GPU selection) while
   `git status` read clean, and a lockstep entry saying "no renderer code change" was drafted
   and committed before the push revealed them. The push is rejected non-fast-forward, so the
   *pin* cannot go out wrong — but the changelog would have, and the changelog is the only
   place those five fixes are ever described to a consumer.

5. **Wait for NuGet publication** - CI builds, packs, and publishes to nuget.org.
   Poll the flat container, which is authoritative and updates within seconds:
   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/<packageid-lowercase>/index.json
   ```
   Do **not** poll with `dotnet package search` — it reads the search index, which lags
   the flat container by many minutes. It showed nothing for DIR.Lib 7.8 across four
   minutes of polling while the package was already restorable, which reads as a failed
   publish when nothing is wrong. If even the flat container lags, read the publish
   step's log in the Actions run (`gh run view <id> --log`) for the pushed `.nupkg`.

   The published version is `X.Y.<run_number>`, e.g. `6.9.1421`. You need the
   new `X.Y` to be live before updating downstream floating pins.

6. **Update downstream `Directory.Packages.props`** to the new `X.Y.*` pin
   (only needed if X.Y changed — see the chain below for ordering).

7. **Build and test** the downstream project:
   ```
   dotnet restore && dotnet build -c Release && dotnet test -c Release
   ```

## Dependency order

```
Fonts.Lib (SharpAstro.Fonts) ─┐
Codecs (SharpAstro.Png) ──────┴─> DIR.Lib ─┬─> Console.Lib        ─┐
Codecs (SharpAstro.Codecs) ────────────────┼─> (Console.Lib)       │
                                           ├─> SdlVulkan.Renderer ─┼─> chess
                                           └─> WebGl.Renderer     ─┘
LAN.Lib ───────────────────────────────────────────────────────────> chess (Chess.Net)
Codecs (SharpAstro.Png) ───────────────────────────────────────────> chess (Chess.MCP)
DIR.Lib ───────────────────────────────────────────────────────────> tianwen (see below)
```

- **Fonts.Lib**, **Codecs** and **LAN.Lib** are roots. Only bump when their own code
  changes. LAN.Lib does not reference DIR.Lib at all, so a DIR.Lib bump never drags it.
- **DIR.Lib** depends on SharpAstro.Fonts + SharpAstro.Png. When DIR.Lib gets an
  X.Y bump, ALL THREE backends need a release even if their code didn't change -
  this keeps all versions in sync and ensures CI builds pick up the new DIR.Lib
  transitively.
- **Console.Lib**, **SdlVulkan.Renderer** and **WebGl.Renderer** each depend on DIR.Lib
  but NOT on each other, so they release in parallel. Console.Lib additionally depends
  on SharpAstro.Codecs. Move all three together: leaving one behind lets a consumer
  holding two backends restore two DIR.Lib versions and unify on the higher one by luck
  rather than by intent (WebGl.Renderer was found two minors behind twice this way).

**Note — WebGl.Renderer's pin IS centralised now** (`<PackageVersion Include="WebGl.Renderer" ... />`
in `Directory.Packages.props`; `Chess.Web.csproj` carries a versionless `PackageReference`
beside its `UseLocalSiblings` ProjectReference). It used to sit inline in the csproj, which
opted that project out of CPM and hid the pin from every solution-wide sweep — which is how
it twice ended up two minors behind DIR.Lib. The habit the old warning taught is still the
right one: **grep for the package name across the repo** rather than trusting any single pin
file, because that is what catches the next one that escapes.

**Note — `tianwen` is the OTHER DIR.Lib consumer** (`../../sharpastro/tianwen`) and is
not in this repo's chain. It carries its own DIR.Lib `X.Y.*` pin, so it is insulated
until it repins deliberately — but that is exactly why a DIR.Lib **behaviour** change
must be checked against it before release, not after: build it and run its tests
(~3800) while the change is still yours to reconsider.

**Note — `SharpAstro.Png` is always NuGet-sourced by chess.** `UseLocalSiblings`
only redirects DIR.Lib / Console.Lib / SdlVulkan.Renderer, NOT Codecs/Png.
`Chess.MCP` references `SharpAstro.Png` via a plain `PackageReference` with no
sibling branch, so it restores Png from NuGet in **both** local and CI builds.
The `SharpAstro.Png X.Y.*` pin must therefore point at a **published** version
even when iterating locally — a source edit to Png in the Codecs sibling will
NOT reach Chess.MCP.

## CRITICAL: Full release chain when DIR.Lib changes X.Y

Each step MUST wait for the previous NuGet publication before proceeding.
Do NOT push downstream repos until their `Directory.Packages.props` has a
published X.Y pin - CI will fail because it doesn't have sibling repos
and the old NuGet versions won't have the new APIs.

1. (If Fonts.Lib/Codecs changed) Bump + push them, poll NuGet for the new X.Y
2. Update DIR.Lib's `src/Directory.Packages.props` to the new `X.Y.*` pins (if bumped)
3. Bump + push DIR.Lib, poll NuGet for the new X.Y (e.g. `DIR.Lib 7.8.1841`)
4. In parallel — all **three** backends, not two:
   a. Update Console.Lib's `Directory.Packages.props` DIR.Lib pin,
      bump Console.Lib minor, push. Poll NuGet.
   b. Update SdlVulkan.Renderer's `src/SdlVulkan.Renderer/Directory.Packages.props`
      DIR.Lib pin (note the nested path), bump minor, push. Poll NuGet.
   c. Update WebGl.Renderer's `src/Directory.Packages.props` DIR.Lib pin,
      bump minor, push. Poll NuGet.
5. ONLY AFTER all three backends are on NuGet:
   Update chess project's `Directory.Packages.props` with the new X.Y pins — **all four
   in one edit**, since they are one lockstep set (values below are the 2026-08-10 set,
   for shape; read the current ones out of the file):
   ```xml
   <PackageVersion Include="DIR.Lib" Version="7.14.*" />
   <PackageVersion Include="Console.Lib" Version="4.20.*" />
   <PackageVersion Include="SdlVulkan.Renderer" Version="7.11.*" />
   <PackageVersion Include="WebGl.Renderer" Version="1.18.*" />
   ```
6. Verify against the published packages before pushing:
   `dotnet build -c Release -p:UseLocalSiblings=false && dotnet test -c Release -p:UseLocalSiblings=false`.
   This is the only local run that exercises the path CI takes.
7. Commit + push chess project. CI will now restore the correct NuGet versions.

## Polling for NuGet availability

```bash
# Authoritative and near-instant. Package id must be LOWERCASE in the URL.
curl -s https://api.nuget.org/v3-flatcontainer/dir.lib/index.json
```

The version published by CI is `X.Y.<run_number><run_attempt>` where the numbers come
from `${{ github.run_number }}` / `${{ github.run_attempt }}` in the workflow. Check the
Actions run for the exact number, or read the tail of the flat-container list.

**Do not poll with `dotnet package search`.** It queries the search index, which trails
the flat container by many minutes — long enough to look like a failed publish while the
package is already restorable. If the flat container itself has not caught up, the
publish step's log is the ground truth: `gh run view <id> --log` and look for the pushed
`<Package>.X.Y.NNNN.nupkg`.

## IMPORTANT: Do NOT push downstream until packages are on NuGet

Chess CI does not have sibling repos. Every push triggers a CI build that
restores from NuGet. If `Directory.Packages.props` references an X.Y that
isn't published yet, CI will fail. This wastes CI minutes and creates noise.

**Rule: never push a repo whose `Directory.Packages.props` references an
unpublished package version.** Commit locally, wait for NuGet, update the
version, THEN push.

The same applies to Console.Lib, SdlVulkan.Renderer and WebGl.Renderer when DIR.Lib is
bumped: do not push them until DIR.Lib's new version is confirmed on NuGet and their
`Directory.Packages.props` is updated.

## Notes

- Chess auto-detects sibling working copies (`UseLocalSiblings` in
  `Directory.Build.props`): local builds use ProjectReferences to the junctioned
  siblings, CI uses NuGet. **A green local build therefore does NOT prove the
  NuGet pins work** — verify with `dotnet build -c Release -p:UseLocalSiblings=false`
  before trusting/pushing a pin change.
- Never use `dotnet nuget locals all -c` to clear cache (breaks concurrent
  processes). Just bump the version instead.
- The backends (Console.Lib, SdlVulkan.Renderer, WebGl.Renderer) also have their
  own `Directory.Packages.props` that reference DIR.Lib - these must be updated
  with a published DIR.Lib X.Y before their own CI push. The paths differ:
  `src/Directory.Packages.props` for Console.Lib and WebGl.Renderer, but
  `src/SdlVulkan.Renderer/Directory.Packages.props` for SdlVulkan.Renderer.
- **A CI test step must never re-pack.** Every shipped project sets
  `GeneratePackageOnBuild`, and the version file falls back to `$(VersionMajorMinor).0`
  when CI's `-p:Version` is absent — so any step that *rebuilds* one packs a second
  `X.Y.0` into the same `bin/Release` the publish job globs with `**/*.nupkg`, and
  `--skip-duplicate` ships it without complaint. That is where `DIR.Lib 7.5.0` beside
  `7.5.NNNN` came from. Two accepted guards, both in use: `dotnet test --no-build`
  (DIR.Lib, Console.Lib, Fonts.Lib, Codecs, SdlVulkan.Renderer, LAN.Lib) or
  `-p:GeneratePackageOnBuild=false` where the test project restores separately
  (WebGl.Renderer). Don't drop either.
- Watch CI to completion rather than assuming green. `publish`/`release` jobs showing
  `skipping` on a PR run is expected (they are gated on push-to-main); the pre-existing
  Node 20 deprecation warnings from `actions/checkout@v4` / `setup-dotnet@v4` are noise.

The library to release is: $ARGUMENTS
