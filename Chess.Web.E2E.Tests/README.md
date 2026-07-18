# Chess.Web.E2E.Tests

Browser end-to-end tests for the **Play by Link** correspondence feature in `Chess.Web`, driven with
[Playwright for .NET](https://playwright.dev/dotnet/) + xUnit v3.

The whole UI (startup wizard and board) is drawn into a `<canvas>`, so the tests deliberately assert
only on the observable DOM surface that the unit tests can't reach:

- the **address-bar fragment** (`history.replaceState` — the game *is* the URL),
- the aria-live **status paragraph** (`p.status`),
- the real DOM **share-row buttons** (Copy / Email / Share / Undo),
- the help **`<details>`** explainer,
- and the **clipboard** (Copy link).

Moves are entered through the desktop square-entry keymap — `"e2e4"` is just the keys `e,2,e,4` sent
to the focused canvas — so no pixel math is involved.

## Why it lives outside `Chess.sln`

Exactly like `Chess.Web` itself: this project needs a browser and a running dev server, so it must
**not** be picked up by the solution-wide `dotnet test` that CI runs on every push. It also opts out
of the repo's Central Package Management (carries its Playwright/xUnit versions inline). Run it
explicitly.

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
