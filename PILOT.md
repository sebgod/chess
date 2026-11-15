# PILOT.md

What a plane can and cannot do in this repo. The node quotes this page whole into the planner, worker and reviewer
briefs, so it stays short. `CLAUDE.md` holds the architecture and conventions; this page holds only what a flight
needs to plan, build, check and land.

## What a plane can and cannot do

- A plane is a git worktree at `<repo>/.claude/worktrees/<code>` on `flight/<code>`. Work and commit only there. Do not
  push, open a PR or merge; the controller lands the flight. Leave nothing uncommitted, and put scratch output
  outside the plane.
- Sibling clones are not yours, and a plane cannot see them. `UseLocalSiblings` switches on only when DIR.Lib,
  Console.Lib, SdlVulkan.Renderer, WebGl.Renderer, LAN.Lib and Firebase all sit beside the repo root, which from a
  plane is `.claude/worktrees/`, where none lives. So a plane restores the pinned packages, as CI does. Never force it
  on with `-p:UseLocalSiblings=true`.
- A change that needs one of those libraries (or AppShell) to change is its own flight in that repo: name it and
  leave it. Chess adopts it from a published package or a PR's preview package, never from sibling source.
- No display, GPU or device is reachable from a plane. Chess.GUI and the inspector-driven checks are the
  controller's to run. Chess.Droid needs the android workload, and Chess.iOS needs macOS with Xcode. Neither is in
  `Chess.sln`; CI's `android` and `ios` jobs build them, so a change to `Chess.Touch`, `Chess.Droid` or `Chess.iOS` is
  checked there and named in the leg's report.
- `firebase-config.json` (the backend config) is never in the repo and never copied into a plane: not from CI, and
  not from the per-user `%APPDATA%\chess` copy. Every check runs without it, which turns online play off; the code
  for that is meant to work either way.
- Never wait with a job that sleeps or polls: use `pilot leg wait --for-file <path>` and end the turn. Long builds
  and test runs go through `pilot run -- <command>`. The machine is shared, so a job may wait its turn first.

## Build and check

Always `-c Release`, as CI does. Run from the repo root.

```bash
dotnet build -c Release
dotnet test -c Release --no-build
```

- `Chess.sln` holds the libraries, the desktop GUI, the console, the engine, the MCP server, the web app (Blazor
  WASM) and both test projects. Tests are xunit.v3 with Shouldly on Microsoft.Testing.Platform (`global.json`), not
  VSTest: `--logger` and `--blame-*` fail the run. Narrow with `--filter-class` or `--filter-method`, as in
  `CLAUDE.md`.
- `Chess.Web.E2E.Tests` sits outside the solution: it starts the web app and drives a browser with Playwright. With
  no Playwright browser installed, run it against the system Edge with `CHESS_E2E_CHANNEL=msedge`. `CloudPlayTests`
  skip without the Firebase emulator. A leg that changes `Chess.Web` runs this suite as well.
- `firebase/` holds the database rules and their tests (`npm test`, which needs Node and a JDK 21). CI's `rules` job
  runs them; a plane without Node names a rules change in the leg's report instead.
- Package versions live in `Directory.Packages.props`, never in a `.csproj`. SharpAstro libraries are pinned
  `X.Y.*`, and every library chess loads must be built against the DIR.Lib major chess pins
  ([docs/pinning.md](docs/pinning.md)). CI's `pins` job checks this against nuget.org.
- A release bump is one line, `VersionMajorMinor` in `Directory.Build.props`. Do not hand-edit anything derived from
  it.

## How a flight merges

- main allows **rebase merges only** (no squash, no merge commits) and requires linear history. So a flight's branch
  is a straight line of commits on main. Take main with `git rebase origin/main`, never a merge, because a merge
  commit cannot be rebased onto main. A rebase that hits a conflict in someone else's change is reported, not
  resolved by guessing.
- Every commit lands on main as it is, so keep a flight's commits few, each one whole, and each one building.

## Writing

No em-dashes, and no `--` in their place: a full stop, a comma, a semicolon or parentheses. This holds for prose,
comments and commit messages. In C#, no null-forgiving `!`: check for null, or pattern-match.
