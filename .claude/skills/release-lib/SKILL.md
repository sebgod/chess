---
name: release-lib
description: Release a SharpAstro sibling library to NuGet and update this chess project to consume it. Use when the user asks to release/publish DIR.Lib, Console.Lib, SdlVulkan.Renderer, Fonts.Lib, or Codecs, or to bump the chess project onto a new library version.
argument-hint: <library-name>
---

Release a SharpAstro sibling library to NuGet and update this chess project to consume it.

Usage: /release-lib <library-name>
Example: /release-lib DIR.Lib
Example: /release-lib SdlVulkan.Renderer

## Library locations

| Library | NuGet Package(s) | Repo | csproj | CI workflow |
|---------|------------------|------|--------|-------------|
| Fonts.Lib | `SharpAstro.Fonts`, `SharpAstro.Fonts.Shaping` | `../../sharpastro/Fonts.Lib` | `src/SharpAstro.Fonts/SharpAstro.Fonts.csproj` | `.github/workflows/dotnet.yml` |
| Codecs | `SharpAstro.Codecs`, `SharpAstro.Png`, `SharpAstro.Jpeg`, … | `../../sharpastro/Codecs` | `src/SharpAstro.Codecs/SharpAstro.Codecs.csproj` (one per package) | `.github/workflows/dotnet.yml` |
| DIR.Lib | `DIR.Lib` | `../../sharpastro/DIR.Lib` | `src/DIR.Lib/DIR.Lib.csproj` | `.github/workflows/dotnet.yml` |
| Console.Lib | `Console.Lib` | `../../sharpastro/Console.Lib` | `src/Console.Lib/Console.Lib.csproj` | `.github/workflows/dotnet.yml` |
| SdlVulkan.Renderer | `SdlVulkan.Renderer` | `../../sharpastro/SdlVulkan.Renderer` | `src/SdlVulkan.Renderer/SdlVulkan.Renderer.csproj` | `.github/workflows/dotnet.yml` |

The same repos are also reachable via junctions next to this repo (`../DIR.Lib`,
`../Console.Lib`, `../SdlVulkan.Renderer`, `../Fonts.Lib`, `../Codecs`) — those
junctions are what chess's `UseLocalSiblings` auto-detection looks at.

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

1. **Bump version** in the library repo. Update BOTH:
   - `<VersionPrefix>X.Y.0</VersionPrefix>` in the `.csproj`
   - `VERSION_PREFIX: X.Y.${{ github.run_number }}` in `.github/workflows/dotnet.yml`
   - Increment minor for new features, major for breaking changes

2. **Build and test** the library locally:
   ```
   cd <repo>/src && dotnet test
   ```

3. **Commit and push** the version bump in the library repo

4. **Wait for NuGet publication** - CI builds, packs, and publishes to nuget.org.
   Poll until the new version appears (typically 2-5 minutes after CI completes):
   ```
   dotnet package search <PackageName> --exact-match --source https://api.nuget.org/v3/index.json
   ```
   The published version is `X.Y.<run_number>`, e.g. `6.9.1421`. You need the
   new `X.Y` to be live before updating downstream floating pins.

5. **Update downstream `Directory.Packages.props`** to the new `X.Y.*` pin
   (only needed if X.Y changed — see the chain below for ordering).

6. **Build and test** the downstream project:
   ```
   dotnet restore && dotnet build -c Release && dotnet test -c Release
   ```

## Dependency order

```
Fonts.Lib (SharpAstro.Fonts) ─┐
Codecs (SharpAstro.Png) ──────┴─> DIR.Lib ─┬─> Console.Lib        ─┐
Codecs (SharpAstro.Codecs) ────────────────┼─> (Console.Lib)       ├─> chess
                                           └─> SdlVulkan.Renderer ─┘
Codecs (SharpAstro.Png) ───────────────────────────────────────────> chess (Chess.MCP)
```

- **Fonts.Lib** and **Codecs** are roots. Only bump when their own code changes.
- **DIR.Lib** depends on SharpAstro.Fonts + SharpAstro.Png. When DIR.Lib gets an
  X.Y bump, ALL downstream libs need a release even if their code didn't change -
  this keeps all versions in sync and ensures CI builds pick up the new DIR.Lib
  transitively.
- **Console.Lib** and **SdlVulkan.Renderer** both depend on DIR.Lib but NOT
  on each other, so they can be released in parallel. Console.Lib additionally
  depends on SharpAstro.Codecs.

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
2. Update DIR.Lib's `Directory.Packages.props` to the new `X.Y.*` pins (if bumped)
3. Bump + push DIR.Lib, poll NuGet for the new X.Y (e.g. `DIR.Lib 6.9.1421`)
4. In parallel:
   a. Update Console.Lib's `Directory.Packages.props` DIR.Lib pin,
      bump Console.Lib minor, push. Poll NuGet.
   b. Update SdlVulkan.Renderer's `Directory.Packages.props` DIR.Lib pin,
      bump SdlVulkan.Renderer minor, push. Poll NuGet.
5. ONLY AFTER both Console.Lib and SdlVulkan.Renderer are on NuGet:
   Update chess project's `Directory.Packages.props` with the new X.Y pins:
   ```xml
   <PackageVersion Include="DIR.Lib" Version="6.9.*" />
   <PackageVersion Include="Console.Lib" Version="3.6.*" />
   <PackageVersion Include="SdlVulkan.Renderer" Version="6.22.*" />
   ```
6. Commit + push chess project. CI will now restore the correct NuGet versions.

## Polling for NuGet availability

```bash
# Check if package version is available (repeat every 30s until it appears)
dotnet package search DIR.Lib --exact-match --source https://api.nuget.org/v3/index.json
```

The version published by CI is `X.Y.<run_number>` where `<run_number>` comes
from `${{ github.run_number }}` in the workflow. Check the GitHub Actions run
to see the exact run number, or just poll the NuGet search output.

## IMPORTANT: Do NOT push downstream until packages are on NuGet

Chess CI does not have sibling repos. Every push triggers a CI build that
restores from NuGet. If `Directory.Packages.props` references an X.Y that
isn't published yet, CI will fail. This wastes CI minutes and creates noise.

**Rule: never push a repo whose `Directory.Packages.props` references an
unpublished package version.** Commit locally, wait for NuGet, update the
version, THEN push.

The same applies to Console.Lib and SdlVulkan.Renderer when DIR.Lib is bumped:
do not push them until DIR.Lib's new version is confirmed on NuGet and their
`Directory.Packages.props` is updated.

## Notes

- Chess auto-detects sibling working copies (`UseLocalSiblings` in
  `Directory.Build.props`): local builds use ProjectReferences to the junctioned
  siblings, CI uses NuGet. **A green local build therefore does NOT prove the
  NuGet pins work** — verify with `dotnet build -c Release -p:UseLocalSiblings=false`
  before trusting/pushing a pin change.
- Never use `dotnet nuget locals all -c` to clear cache (breaks concurrent
  processes). Just bump the version instead.
- The intermediate libraries (Console.Lib, SdlVulkan.Renderer) also have their
  own `Directory.Packages.props` that reference DIR.Lib - these must be updated
  with a published DIR.Lib X.Y before their own CI push.

The library to release is: $ARGUMENTS
