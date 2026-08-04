# Chess.Web.E2E.Tests

Browser end-to-end tests for the **Play by Link** correspondence feature in `Chess.Web`, driven with
[Playwright for .NET](https://playwright.dev/dotnet/) + xUnit v3.

The whole UI (startup wizard and board) is drawn into a `<canvas>`, so the tests deliberately assert
only on the observable DOM surface that the unit tests can't reach:

- the **address-bar fragment** (`history.replaceState` — the game *is* the URL),
- the aria-live **status element** (`.status`),
- the real DOM **share-row buttons** (Copy / Email / Share / Undo),
- the titlebar **help chips** and the `.panel` they open,
- and the **clipboard** (Copy link).

Moves are entered through the desktop square-entry keymap — `"e2e4"` is just the keys `e,2,e,4` sent
to the focused canvas — so no pixel math is involved.

## Why it lives outside `Chess.sln`

This project needs a browser and a running dev server, so it must **not** be picked up by the
solution-wide `dotnet test` that CI runs on every push. Staying out of the solution is what makes
that fail-**safe**: excluding it with a `--filter` instead would mean it runs whenever someone
forgets the filter, and a browser launch failing in CI is a confusing way to find that out. Run it
explicitly.

It is **not** like `Chess.Web`, which it used to cite as precedent — that project joined the
solution, because nothing about it required staying out. And it does **not** opt out of Central
Package Management: versions live in `Directory.Packages.props` like every other project's. CPM is
directory-scoped and independent of solution membership, so the opt-out bought nothing and only
meant the `Microsoft.NET.Test.Sdk` / `xunit.v3` / `xunit.runner.visualstudio` pins here duplicated
the central ones, free to drift the moment either side moved.

## Running

```bash
# 1. bring up the app (leave running in another terminal)
dotnet run --project Chess.Web -c Release        # serves http://localhost:5000

# 2. point the tests at it and run
CHESS_WEB_BASEURL=http://localhost:5000 dotnet test Chess.Web.E2E.Tests
```

If `CHESS_WEB_BASEURL` is **not** set, the fixture starts its own `dotnet run` on port 5177 and tears
it down afterwards (the self-contained path for CI).

### Browser

- **Default:** bundled Chromium. The fixture runs `playwright install chromium` on first use, so a
  clean checkout needs no manual install step.
- **win-arm64:** set `CHESS_E2E_CHANNEL=msedge` to drive the natively-installed Edge instead and skip
  the bundled-Chromium download entirely.

```bash
CHESS_WEB_BASEURL=http://localhost:5000 CHESS_E2E_CHANNEL=msedge dotnet test Chess.Web.E2E.Tests
```
