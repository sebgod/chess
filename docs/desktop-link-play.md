# Design: Play by Link on the desktop (URL scheme + single-instance hand-off)

**Status:** Not started (see [Phasing](#phasing)). **Repo scope:** almost entirely **chess**. Both
capabilities it leans on already ship and are already in chess's package graph as of the DIR.Lib 8.8
repin: `SharpAstro.AppShell`'s `InstanceGate` (arrives transitively under SdlVulkan.Renderer) and
`SdlVulkanWindow : IActivatableWindow` (SdlVulkan.Renderer 7.23). One *optional* sibling cleanup is
called out as phase 3; nothing here is blocked on a sibling release.

## Why

The README's feature list reads:

> **Play by Link**: serverless correspondence chess **(browser)** — the whole game travels in the URL…

That parenthesis is the whole gap. The desktop app cannot consume a game link at all — not from the
command line, not from the clipboard, not from a `chess://` click. Someone playing correspondence
chess with the native app has to open a browser to make each move, which makes the native app the
worse client for the one mode that has no server to depend on.

The gap is much smaller than it looks, because everything *chess-specific* already exists and is
already in the right assembly. What is missing is an entry point.

## What already exists, and what genuinely doesn't

| Piece | Where | State |
|---|---|---|
| Link codec | `Chess.UCI/GameLinkCodec.cs` — `EncodeFragment(Game)` / `TryDecode(fragment, out game, out error)` | **Exists**, and Chess.GUI already references Chess.UCI |
| Who-plays-what rule | `Chess.Web/Pages/Play.razor:879-902` (`TryApplyFragmentAsync`) | **Exists** — to be mirrored verbatim, see below |
| Wizard menu item | `StartupWizardOptions.LinkPlay` on the shared `Chess.Lib.UI.StartupWizard` (offers "Play by Link" at `StartupWizard.cs:87`) | **Exists** — the GUI simply doesn't pass the flag (`VkStartupMenu.cs:21-22`) |
| Clipboard | `SDL.GetClipboardText` / `SetClipboardText` / `HasClipboardText` in SDL3-CS, which Chess.GUI references **directly** | **Exists** — no backend wrapper needed |
| Window activation | `SdlVulkanWindow : IActivatableWindow`, raised via `WindowActivation.Activate(window)` | **Exists** (SdlVulkan.Renderer 7.23) |
| Single-instance gate | `SharpAstro.AppShell.InstanceGate` — `ChannelFor`, `TryClaim`, `TryHandOff`, `TryDequeue` | **Exists** in the graph; needs an explicit `PackageReference` if used directly |
| Drop target | `SdlEventLoop.OnDropFile` | **Exists**, unused by chess |
| Command-line arguments | `Chess.GUI/Program.cs` | **Missing entirely** — top-level statements go straight to `SdlVulkanWindow.Create`; there is no `args` |
| URL scheme | — | **Missing** |

`StartupWizardOptions.LinkPlay`'s own doc comment already anticipates this: *"Only front-ends that can
produce and consume game links show it (today Chess.Web)."*

## The link format is already portable

`GameLinkCodec` encodes `#g=e2e4.e7e5.g1f3`, and the body parses as `&`-separated `key=value` pairs
with **the leading `#` optional**. A query string is therefore the same grammar, which means all three
shapes a desktop app can receive reduce to one call with no new parsing:

| Received | Reduce to | Then |
|---|---|---|
| `https://sebgod.github.io/chess/#g=e2e4.e7e5` | everything after the first `#` | `TryDecode` |
| `chess://play?g=e2e4.e7e5` | everything after the first `?` | `TryDecode` |
| bare `g=e2e4.e7e5` | as-is | `TryDecode` |

**The desktop must not grow its own parser.** `TryDecode` replays every ply through `Game.TryMove`, so
the rules engine is the parser's watchdog and a tampered or truncated link cannot produce an illegal
position; `MaxPlies = 4096` bounds the replay a hostile link can demand; and the reserved `f=`
custom-start key is *explicitly rejected* so this version can never mis-play a future custom-start link
as a standard-start game. Every one of those properties is lost by a second implementation.

## The turn semantics, stated once

Mirror `Play.razor:879-902` exactly — a link means **"it's your turn"**:

```csharp
if (GameLinkCodec.TryDecode(fragment, out var game, out var error) != GameLinkResult.Ok) { /* show error */ }

localSide = game.CurrentSide;              // the receiver plays whoever is to move
ui.MoveLockSide = localSide;               // the turn gate for link play (NOT the LAN gate)
ui.FlipBoard = localSide == Side.Black;    // orient to the receiving player
```

Two consequences worth writing down because they look like bugs otherwise. An **unstarted** game
encodes as `#g=` — that is the start link a Black-playing creator sends so their opponent opens as
White, so an empty payload is valid input, not an error. And on the web a game link **skips the wizard
entirely** (`Play.razor:323`); the desktop should do the same when a link arrives on the command line,
or the user is asked to pick a mode for a game that already has one.

`MoveLockSide` is the link-play turn gate. It is deliberately *not* the LAN turn gate — do not
generalise the two.

## Single instance: claim always, hand off only with a payload

This is the load-bearing design decision, because the obvious reading ("register a gate, become a
single-instance app") is a **regression**. Two instances on one machine is a scenario chess supports on
purpose — `Chess.Net/LanProfile.cs:13`:

> Persisting it was exactly what made two instances on one machine — sharing one `lan.txt` — load the
> same id and then silently ignore each other as their own echo.

The peer id is minted per process *specifically* so two local windows discover each other for LAN play,
and the SDL/TUI inspector workflows want fresh instances too. So the policy is:

- **Always `TryClaim`.** Owning the pipe is what makes this instance reachable. Cost is one named pipe.
- **`TryClaim` returning `null` is not a reason to exit.** Somebody else owns the channel; this process
  carries on and opens its own window. Two plain windows, LAN intact.
- **Hand off only when there is a payload.** A launch carrying a link tries `TryHandOff` and exits on
  success; a bare launch never hands off. This single rule is what keeps the feature and LAN compatible.
- **A failed hand-off is never fatal.** Fall through and open the link in this process — an extra window
  is a poor outcome, a click that does nothing is an unacceptable one. `InstanceGate` is built for this:
  every failure path returns `false`/`null` rather than throwing.

Channel identity: `InstanceGate.ChannelFor("sharpastro-chess")` — one gate for the whole app. The
per-folder mode (`NormalizePathIdentity`) buys nothing here; a link is not a file in a directory.

## Where the drain goes — the non-obvious part

The AppShell README's frame-loop sketch (`while (gate.TryDequeue(out var r)) { … }` once per frame)
does **not** transplant into `SdlEventLoop` as written, and getting this wrong makes the feature fail in
exactly the case it exists for.

`SdlEventLoop.Run` computes `anyNeedsRedraw` **excluding minimized windows** and then parks in
`WaitEventTimeout(out evt, 16)`. A minimized window therefore never renders, so `OnRender` /
`OnBeforeFrame` / `OnPostFrame` never run — and a minimized window is precisely the state a hand-off
is meant to rescue. Draining in the render path means the link is applied whenever the user next
happens to click the app, i.e. never, from the user's point of view.

What does run every iteration, minimized or not, is the per-window external redraw check —
`CheckNeedsRedraw`. That is the only *public* per-iteration hook in a Release build:
`SdlEventLoop.OnLoopIteration` (which the debug inspector uses for exactly this purpose) is `internal`
**and** `#if DEBUG`, so it is compiled out of the shipping loop.

Two further details:

- **The gate has no `HasPending`.** `TryDequeue` is the only reader, so the drain must dequeue and
  *stash* the payload; a "peek in the predicate, apply in the render" split is not expressible.
- **Wake the loop from the accept thread.** `SdlEventLoop.RequestRedraw()` (and the per-view
  `SdlWindowView.RequestRedraw()`) are public, and the latter is exactly what the debug inspector's
  `Poke()` calls from its own server thread — so the precedent for a cross-thread poke is already set
  in the library. Without it the hand-off waits up to one `WaitEventTimeout` tick: survivable at 16 ms,
  but the poke is free and matches existing practice.

So the shape is: AppShell's accept thread enqueues → chess's drain (in `CheckNeedsRedraw`) dequeues into
a `pendingLink` field, calls `WindowActivation.Activate(sdlWindow)`, and returns `true` → the next frame
applies `pendingLink`. Activation itself is already correct in the backend: restore **only** when
actually minimized (two applications independently got this wrong as restore-then-raise, which knocks a
maximised window back to its floating size), and `TryHandOff` spends the `AllowSetForegroundWindow`
grant on the target before sending, because Windows will not let a background process pull itself
forward.

## Registering the scheme

Chess ships as a published folder, not an installer, so registration has to be an explicit action —
**never a silent write on first run.** A `--register-protocol` / `--unregister-protocol` pair on
Chess.GUI, echoed by a menu item, keeps it visible and reversible.

| Platform | Mechanism | Notes |
|---|---|---|
| Windows | `HKCU\Software\Classes\chess` with an empty `URL Protocol` value + `shell\open\command` | Per-user, no admin. `Microsoft.Win32.Registry` is AOT-safe |
| Linux | `~/.local/share/applications/*.desktop` with `MimeType=x-scheme-handler/chess;` then `update-desktop-database` | Per-user |
| macOS | `CFBundleURLTypes` in `Info.plist` | Needs a real `.app` bundle, which chess does not produce — **out of scope** until it does |

The web page should also learn to *offer* the desktop app (an "open in the app" affordance next to
"Copy link"), but that is a Chess.Web change and deliberately not part of this plan — the scheme has to
exist first.

## Phasing

| Phase | Scope | Where | Status |
|---|---|---|---|
| 1 | Link play in the GUI, end to end and with no new plumbing: `args` on `Program.cs`, `StartupWizardOptions.LinkPlay` on `VkStartupMenu`, paste-a-link (`SDL.GetClipboardText`), the `Play.razor` turn semantics, and "copy reply link" (`SDL.SetClipboardText`) after each committed ply | chess | Not started |
| 2 | `chess://` registration (`--register-protocol`) + `InstanceGate` claim/hand-off + `WindowActivation.Activate`, drained per-iteration via `CheckNeedsRedraw` with a `RequestRedraw()` poke from the accept thread; explicit `PackageReference` on `SharpAstro.AppShell` | chess | Not started |
| 3 | *Optional cleanup:* a public, non-`DEBUG` per-iteration hook on `SdlEventLoop` so the drain stops living in a side-effecting predicate | SdlVulkan.Renderer | Not started |

**Phase 1 is the feature; phase 2 is the polish.** That ordering is deliberate and worth defending: a
link pasted into the app is already the whole of correspondence play, and it needs no registry, no
scheme and no gate. AppShell only earns its keep once a *scheme* exists, because a scheme is what
spawns a fresh process per click — which is the only problem `InstanceGate` solves. Doing phase 2 first
would be plumbing with nothing flowing through it.

## Open questions

- **A link arriving mid-game.** Discard the current game, or prompt? The web has no equivalent (a new
  link is a new tab), so this is a genuinely new decision. Suggested: prompt, and keep the existing
  `game.uci` save untouched until the user confirms.
- **One save slot.** `GameStore` is a single fixed path (`LocalApplicationData/SharpAstro.Chess/game.uci`)
  and "Continue" resumes it. A correspondence game against two different opponents wants two slots.
  Out of scope here, but this is the feature that first makes the single slot feel wrong.
- **Chess.Console.** The same phase 1 applies almost unchanged (the wizard and codec are shared, and the
  TUI has its own clipboard story). Worth doing, not scoped here.
- **Chess.Droid.** An Android intent filter for `chess://` is the natural analogue, and `Activate()` is
  already referenced on the android TFM. Separate plan.
- **Drag and drop.** `OnDropFile` is already wired in the backend and unused, so dropping a saved `.uci`
  game onto the window is nearly free — but nobody receives a correspondence game *as a file*, so this
  is an extra, not a phase.

## Testing

- **Codec parity** is already covered by `Chess.Tests/GameLinkCodecTests.cs`; the desktop adds no codec
  and so needs no codec tests. What it needs is a test that the *argv reduction* (`#`/`?`/bare) hands
  `TryDecode` the same body for all three shapes.
- **Turn semantics** — assert `MoveLockSide` / `FlipBoard` / `localSide` against a decoded link, the same
  assertions `Chess.Web.E2E.Tests/PlayByLinkTests.cs` makes in the browser.
- **The hand-off** is an integration test, not a unit test: launch instance A, launch instance B with a
  link, assert B exits 0 and A's window applied it. The SDL debug inspector can drive and screenshot A
  headlessly, which is what makes this testable at all — and it is the one test that would catch the
  minimized-window drain bug described above, so **minimize A first**.
