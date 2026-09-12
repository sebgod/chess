# TODO

## Planned work (design docs)

Each of these has a doc under `docs/` carrying its own **Status** header and **Phasing** table — those
are the source of truth for progress, not this list. The gaps below the line are unplanned known
defects; these are planned changes.

- [**Correspondence play**](docs/correspondence-play.md) — one payload, two couriers. The link
  courier closes the README's "(browser)" gap: the native app can't consume a game link at all, and
  phase 1 (paste/argv + reply link) needs no new plumbing — the codec, the wizard entry and the
  clipboard already exist. The cloud courier then carries the same bytes for people who don't already
  share a messenger, on a free tier that caps rather than bills. They share a spine (a multi-slot
  `GameStore`, one `TryDecode` path, one minimized-safe drain), which is why they are one plan and not
  two. *Phases 1 and 2 are **done**, unit-tested and live-verified through the SDL inspector — link
  play in the GUI (argv link skips the wizard, the board orients to the local side, the one-move gate
  refuses the other colour, Ctrl+L / Ctrl+V carry the game out and back) and the inbox behind it
  (several games at once, "waiting on you" first, stale hidden not deleted, legacy save migrated).
  Phase 3's cloud BACKEND is live and verified (europe-west1 instance, deployed rules with 16 emulator
  tests, anonymous auth, config in a FIREBASE_CONFIG repo secret) but its client is not written. The
  cloud courier is **browser-only** — the desktop keeps link play — which deleted the `ILobby`
  extraction, the hand-rolled REST+SSE client and the second API key. Phases 4-5 not started.*
- [**Content→device transform**](docs/content-transform.md) — DPI and rotation unified as one
  constrained affine map, which is what the Android "across the table" flip is built on. *Phases 1a and
  2 done; WebGL compose and the CPU backend pending.*
- [**Setup-mode drag ghost**](docs/drag-ghost.md) — the dragged piece follows the cursor in setup
  mode. *All four phases **done**: `GameUI` holds the ghost and states its damage, and the terminal,
  the GUI, Android and the browser all feed it motion. Live-verified in the GUI (renderer inspector)
  and in a real browser (Playwright, the suite's only pixel-reading tests). **Not** verified on an
  Android device, and the terminal has no live route at all — Console.Lib's inspector cannot
  synthesize pointer motion, which is the one follow-up this left behind.*
- [**Second board game / game-library carve-out**](docs/game-library.md) — what would actually have to
  be extracted for a second game (Skat, Memory) to share this repo's turn model, wizard, frame and LAN
  lobby. *Design only.*

## Google Play readiness (Chess.Droid)

Two Play quality requirements land in 2027 and only bite if Chess.Droid is actually published there
(today CI only builds it — no keystore, no AAB, no publish step).

- **DEX optimization ≥ 25% coverage — February 2027. Currently NOT met.** Measured from the build:
  `AndroidDexTool = d8` (so the app *does* ship a `classes.dex` — the Java host, `SdlVulkanActivity`
  and the SDL3-CS bindings), but `AndroidLinkTool` is **empty**, i.e. R8 shrinking/optimization is
  off entirely. `AndroidLinkMode = SdkOnly` is the *managed* trimmer and does not count; likewise
  `RunAOTCompilation = true` helps startup and managed memory but is not what this metric measures.
  **The metric is a build-configuration check, not a performance measurement** — being fast does not
  exempt an app. The fix is `<AndroidLinkTool>r8</AndroidLinkTool>`, but it is not a blind flip:
  R8 can strip Java classes that JNI reaches reflectively, and SDL's activity is exactly that shape,
  so it needs `-keep` rules plus an on-device smoke test (the SDL3-CS.Android pin is already
  version-fragile — see the 3.4.10.5 note).
- **Zero-Tap Sign-In — April 2027.** Chess is clear twice over: games are exempt, and link/LAN play
  has no sign-in at all. It would only ever apply if the cloud courier's anonymous auth shipped on
  Android, and even then the exemption holds. Note the word "currently" in the exemption.
- **Memory thresholds (anonymous RSS + swap, bitmap memory) — February 2027. Unmeasured.** This is
  the half where a .NET app carries genuine risk, because the runtime baseline is not free. Needs a
  real measurement on device (the Tab M8 is the rig) before anyone assumes it passes.

## Console Input

### ASCII mode requires a real terminal
`Console.KeyAvailable` throws `InvalidOperationException` when stdin is redirected
(e.g., piped or launched from a non-interactive context). The app builds and starts
correctly but crashes in `VirtualTerminal.InitAsync()`. Needs a guard or fallback
for redirected stdin scenarios.

## Missing Draw Rules

### Fifty-Move Rule
If 50 consecutive moves (100 half-moves/plies) pass without a pawn move or capture,
either player may claim a draw. At 75 moves (150 plies) it becomes automatic.
Needs a halfmove clock: reset on pawn moves and captures, incremented otherwise.
The halfmove clock is also part of the FEN standard (5th field).

### Threefold Repetition
If the same position occurs three times with the same side to move, castling rights,
and en passant square, either player may claim a draw. At fivefold repetition it
becomes automatic. Needs a position history keyed by board state + side + castling
rights + en passant.

### Insufficient Material
Automatic draw when neither side can deliver checkmate:
- King vs King
- King + Bishop vs King
- King + Knight vs King
- King + Bishop vs King + Bishop (same-colored bishops)

### Dead Position
A generalization of insufficient material — drawn if no sequence of legal moves can
lead to checkmate. Rare beyond the insufficient material cases.
