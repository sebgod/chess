# Design: Correspondence play — the link courier, then the cloud courier

**Status:** Phases 1 and 2 **done and live-verified in a running window**. Phase 3's backend is **live and verified end to end** — europe-west1 instance, deployed rules, anonymous auth — with its client not yet written; phases 4-5 not started. The cloud courier is **the browser first, then Android** (see [Scope](#scope-the-browser-first-then-android)) — the native half puts back the `ILobby` extraction and the second API key that a browser-only scope had struck out. See [Phasing](#phasing). **Repo scope:** almost entirely **chess**; one
*optional* sibling cleanup is called out as the last phase and nothing here is blocked on a sibling
release. Both capabilities the link half leans on already ship and are already in chess's package
graph as of the DIR.Lib 8.8 repin: `SharpAstro.AppShell`'s `InstanceGate` (arrives transitively under
SdlVulkan.Renderer) and `SdlVulkanWindow : IActivatableWindow` (SdlVulkan.Renderer 7.23). The cloud
half needs **no new package at all** on the native side either — see [A native client needs no SDK](#a-native-client-needs-no-sdk).

> **This file was `desktop-link-play.md`.** It was renamed rather than joined by a second plan because
> the two were one plan: see [Why these are one plan](#why-these-are-one-plan).

## Why

The README's feature list reads:

> **Play by Link**: serverless correspondence chess **(browser)** — the whole game travels in the URL…

That parenthesis is the whole gap. The desktop app cannot consume a game link at all — not from the
command line, not from the clipboard, not from a `chess://` click. Someone playing correspondence
chess with the native app has to open a browser to make each move, which makes the native app the
worse client for the one mode that has no server to depend on.

The gap is much smaller than it looks, because everything *chess-specific* already exists and is
already in the right assembly. What is missing is an entry point.

The second gap is the one a link cannot close: **two people who don't already have each other's
messenger open cannot start a game.** A link is a courier the *user* carries. Nothing in the product
carries it for them, so there is no way to sit down and play a stranger, and no way for a move to
arrive while the app is shut.

## Why these are one plan

The naive reading is that these are two features — "paste a link" and "play online" — sharing a
neighbourhood. They are not. They are **two couriers for one payload**, and the payload, the receive
semantics, the storage and the delivery drain are all the same code. Four things make the folding
concrete, and each is a thing that would otherwise be built twice:

**1. The payload is already the same bytes.** `GameLinkCodec.EncodeFragment` produces
`g=e2e4.e7e5.g1f3` — a replay log, deliberately not a position snapshot, because castling and
en-passant rights are derived from ply *history*. "Store players + their games" in a cloud database
means storing exactly that string. The cloud row **is** the link. Get this right and the two couriers
interoperate for free: a cloud game exports as a link the moment the other player's client breaks, and
a link game is adoptable into the cloud when both players want push. Get it wrong — invent a second
server-side game format — and you own a migration between two encodings of the same thing.

**2. The receive semantics are the same three lines.** A link means "it's your turn"; so does a push.
Both end in the block in [The turn semantics](#the-turn-semantics-stated-once). One implementation.

**3. Both need an inbox, and chess has one save slot.** `GameStore` is a single fixed path
(`LocalApplicationData/SharpAstro.Chess/game.uci`) and "Continue" resumes it. That is already the
first thing that feels wrong about desktop link play against two opponents — it was an *open question*
when this was a link-only plan. The cloud makes it **mandatory**, because the entire point of a server
is that several games can be waiting on you at once. Solving it once, in the spine, is the difference
between a plan and two plans that both edit `GameStore`.

**4. Both need a delivery drain that runs while the window is minimized**, and that drain is the
single hardest piece of research in this document (see [Where the drain goes](#where-the-drain-goes--the-non-obvious-part)).
An `InstanceGate` hand-off and an inbound cloud move are the same problem — a payload arriving from
off-thread at a moment when `OnRender` is not running — and they have the same answer.

There is also a semantic reason, which is the one that actually settles it. Chess already has a *live*
courier: `Chess.Net`'s LAN play, where both players are present and `GameLoop` pulls moves on its own
thread. The link is a purely *asynchronous* courier — you close the app between moves. **The cloud is
the first courier that is both**, and it needs the async spine that link play was going to build
anyway plus the live session model LAN already has. Planning it apart from link play means designing
the async half twice and discovering the collision at integration.

What folding does **not** mean is that the cloud half is load-bearing for the link half. The phasing
below keeps them orderable: phases 1–2 ship a complete, account-free feature and can be the end of it.

**Three couriers, and none of them replaces another.** This is worth stating flatly because "add
online play" is exactly the kind of plan that quietly deprecates what came before:

| Courier | Infrastructure | Players present | Status |
|---|---|---|---|
| **Link** | none — no account, no server, no network code | no | the default, and it stays the default |
| **LAN** (`Chess.Net`) | none — UDP discovery + TCP on your own network | yes | ships today, untouched by this plan |
| **Cloud** | a free-tier database | either | the new one, and opt-in |

Link play must keep working with no Google project in existence, on a machine with no network at all
beyond a clipboard — that is the whole point of encoding the game into the URL. LAN play must keep
working on a network with no internet. The cloud earns its place only by covering the case neither
can: two people who share neither a room nor an already-open messenger.

## The spine

Three pieces, shared by both couriers, none of which exist today.

### An inbox, not a save slot

`GameStore` grows from one path to a directory keyed by game id, each entry carrying what
`SavedGame` carries now (moves, computer side, mode) plus: **who the opponent is**, **which side I
play**, **when it last changed**, and — for a cloud game — **the remote id**. The "your move" list is
then a filter over the inbox (`game.CurrentSide == mySide`), which is the same predicate the link
courier uses to decide whether to show "copy reply link".

Backward compatibility matters here exactly as it did for the mode field: an existing `game.uci`
must still load, as the one unnamed entry.

**The timestamp goes in the entry, never in the link.** `lastMoveDate` is metadata about *this
device's copy* of the game, not about the game, and putting it in the payload would cost three things
at once: the link stops being a pure function of the moves (so the web's `_lastAppliedFragment` echo
guard and every "is this the same game?" comparison break), the URL grows for no gain, and the link
starts telling anyone who sees it when you were playing. The same split already exists in
`GameStore`: the file holds the moves, plus local facts about them. For a cloud game the server's
`updated` field plays the same role and is the better clock, because it is server time — a device
clock can be wrong or deliberately lied to.

**Stale entries are hidden, not deleted.** A correspondence game with no move for ~120 days is almost
certainly over, and an inbox that lists it forever is an inbox nobody reads. But it is still a valid
game, and someone who went on sabbatical mid-game should get it back — so the rule is that the
default list filters on recency while the entry stays on disk behind a "show older games". Deleting
on a timer is the one version of this that is hard to forgive, because the whole premise of the
feature is that a correspondence game may legitimately take months.

That same staleness rule is what the cloud's abandoned-game sweep needs (see
[Presence without Cloud Functions](#presence-without-cloud-functions)) — one policy, two couriers,
which is the pattern this whole plan keeps running into.

### One inbound path

Every way a payload can arrive reduces to one call before anything touches the UI:

```
argv  ·  clipboard paste  ·  chess:// hand-off  ·  drop file  ·  cloud push
                              ↓
              GameLinkCodec.TryDecode(body, out game, out error)
                              ↓
              apply turn semantics · write the inbox entry · repaint
```

**No courier may grow its own parser.** `TryDecode` replays every ply through `Game.TryMove`, so the
rules engine is the parser's watchdog and a tampered, truncated or hostile payload cannot produce an
illegal position; `MaxPlies = 4096` bounds the replay it can demand; and the reserved `f=`
custom-start key is *explicitly rejected* so this version can never mis-play a future custom-start
link as a standard-start game. Every one of those properties is lost by a second implementation — and
a cloud courier is precisely where a second implementation would be tempting, because the bytes
arrive over a socket instead of a clipboard.

### One drain

See [Where the drain goes](#where-the-drain-goes--the-non-obvious-part). Whichever of the cloud push
or the `chess://` hand-off lands first builds it; the other one consumes it.

---

# Courier 1 — the link

No accounts, no server, no infrastructure. This is the half that must keep working exactly as it does
today, and it is the whole feature for anyone who already has a messenger open.

## What already exists, and what genuinely doesn't

| Piece | Where | State |
|---|---|---|
| Link codec | `Chess.UCI/GameLinkCodec.cs` — `EncodeFragment(Game)` / `TryDecode(fragment, out game, out error)` | **Exists**, and Chess.GUI already references Chess.UCI |
| Who-plays-what rule | `Chess.Web/Pages/Play.razor` — `TryApplyFragmentAsync` (~934-960) | **Exists** — to be mirrored verbatim, see below |
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

This is also why the cloud row can be the same string: it is a fourth row in this table whose
"received" column is an HTTP response body.

## The turn semantics, stated once

Mirror `Play.razor`'s `TryApplyFragmentAsync` exactly — an arriving payload means **"it's your turn"**:

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

`MoveLockSide` is the *asynchronous* turn gate. It is deliberately **not** the LAN turn gate — LAN
gates by handing `GameLoop` a `NetworkPlayer` in the engine-shaped slot, which only works while both
players are present. A cloud game uses **both**, and which one depends on the mode it is in; see
[Live and correspondence are the same store](#live-and-correspondence-are-the-same-store).

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

**This generalises to the cloud courier unchanged**, and is the reason the drain is spine work rather
than link work: a move pushed by an absent opponent arrives on a background reader thread at a moment
when the window is, by assumption, not being looked at.

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

So the shape is: the producer thread (AppShell's accept thread, or the cloud reader) enqueues →
chess's drain (in `CheckNeedsRedraw`) dequeues into a `pendingLink` field, calls
`WindowActivation.Activate(sdlWindow)`, and returns `true` → the next frame applies `pendingLink`.
Activation itself is already correct in the backend: restore **only** when actually minimized (two
applications independently got this wrong as restore-then-raise, which knocks a maximised window back
to its floating size), and `TryHandOff` spends the `AllowSetForegroundWindow` grant on the target
before sending, because Windows will not let a background process pull itself forward.

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

---

# Courier 2 — the cloud

## It is not storage, it is a database with push

The instinct to reach for "free cloud storage" is half right: the thing to store is small, and there
is a free tier that covers it. But **object storage is the wrong primitive and its free tier is not
close**. Cloud Storage's always-free allowance is 5,000 Class A and 50,000 Class B operations *per
month*; two clients polling a game object every 2 s burn ~1,800 Class B ops in one 30-minute game.
That is about 27 games a month before the polling alone exhausts the quota, and polling is also the
worst possible experience — a move you already made sits invisible for a couple of seconds.

The right primitive gives persistence **and** the delivery channel from the same write, which is
exactly what "just store players + their games" is reaching for without naming it.

## Why chess can use a dumb store at all

Chess is a **perfect-information** game. Both clients already hold the whole truth, and every ply is
validated locally by `Board.EvaluateAction`. This is the same property `GameLinkCodec` is built on:

> Replaying through `Game.TryMove` also validates every ply, making the rules engine the parser's
> watchdog — a corrupted or hand-tampered link cannot produce an illegal position.

So the server never needs to know the rules, never needs to be trusted, and never needs to run our
code. That is a genuinely unusual position to be in, and it is what makes a free tier viable — and it
is **specific to chess**. `docs/game-library.md` records the contrast for the second-game work: Skat
needs a real LAN authority because someone must deal, and per-seat hidden state means a relay-the-move
design is unsound there. Nothing in this plan generalises to a hidden-information game, and it should
not be written as though it does.

## Scope: the browser first, then Android

Scoped to the browser while the backend was standing up, and corrected within the day: **Android is in
scope too.** A phone is where a game that takes days to finish actually gets played, so shipping
correspondence play to every front-end except that one would have been the wrong omission.

The correction is not a small widening. Browser-only struck five things out; Android, being a
**native** front-end that already runs a lobby, puts four of them back:

- **The `ILobby` extraction — the design risk of the whole phase — is back on.** `Chess.Droid` already
  has a LAN lobby (`StartLobby`/`RenderLobby`/`HandleLobbyTap` in `MainActivity.cs`, hand-rendered
  menus and all), so a cloud lobby beside it forks that UI unless both sit behind one interface. The
  wrinkle named [below](#what-chessnet-already-gives-us-and-the-one-thing-it-doesnt) is the real work:
  LAN's *invite → accept* and the cloud's *post → claim a seat* are not one state machine, and
  `LobbyState` currently encodes the LAN one.
- **The `ILanConnection` → `ISessionConnection` rename** comes back with it, for the same reason.
- **The hand-rolled REST + SSE client** comes back, because a .NET Android head cannot use the JS SDK,
  and binding the Firebase Android SDK would cost more than the REST surface it wraps. The sections
  below describe that road as one not currently taken; it is taken now.
- **The second API key** comes back: an HTTP-referrer restriction refuses a request that sends no
  `Referer` — measured against the live key, 403 — and `HttpClient` on Android sends none. See
  [One key per front-end](#one-key-per-front-end-because-a-referrer-restriction-excludes-a-native-client).

What stays struck out is **the minimized-window drain**, for a reason that survives the correction: it
exists because `SdlEventLoop` excludes a minimized desktop window from `anyNeedsRedraw`. Android is an
SDL host, but it has no minimized window, and it already drains off-thread arrivals the obvious way —
`_pendingLobbyStart` and its siblings are volatile flags named in the redraw predicate and consumed in
`Render`. A cloud arrival adds one more flag to that predicate. The drain stays phase 4's, owned by
the `chess://` hand-off alone.

**The browser still goes first**, because the JS SDK reaches a working two-tab game in an afternoon and
proves the deployed rules from our own code rather than from `curl`. Android is the client that pays
for the shared plumbing, so it should not also be the one discovering that the backend is wrong.

**Chess.GUI and Chess.Console stay out of scope, but the gap narrows to almost nothing.** Once
`Chess.Net` carries the transport and `ILobby` exists, the GUI's remaining cost is a `VkCloudLobby`
beside `VkLanLobby`. Whether to spend that is a separate call, worth making when the Android client
works rather than now.

Everything already built and verified was never front-end specific: the schema, the rules, anonymous
auth, the instance.

## Which free tier, and why the cap matters more than the quota

| | Firebase Realtime Database (Spark) | Firestore (always-free) |
|---|---|---|
| Allowance | 100 simultaneous connections, 1 GB stored, 10 GB/month down | 50k reads / 20k writes / 20k deletes per day, 1 GiB |
| In chess terms | **50 concurrent games** | ~80 writes/game → **~250 games/day** |
| Push | WebSocket (and REST + SSE), sub-second | listeners, but each delivered document is a billed read |
| Over the limit | **capped** | **billed**, if the project has billing attached |

**Take Realtime Database on the Spark plan, and the deciding property is not the quota — it is that
Spark has no payment method attached, so exceeding a limit refuses service instead of invoicing.** A
public chess app is an open write endpoint on the internet; for a hobby project that distinction is
the whole risk model, and it outranks the fact that Firestore's daily write allowance is generous.
Firestore's read accounting is also the wrong shape for a lobby, where every client watching a
20-player waiting list re-reads all 20 rows on each change.

Non-negotiable operational rule that follows: **keep it in a Google Cloud project with no billing
account, ever.** Attaching one silently converts the cap into a bill.

## Operational setup (the account, and what it constrains)

The project exists — Firebase console, **Spark plan, no billing account**. Its id, instance and web
config are deliberately NOT written here; see [Why the config is a secret](#why-the-config-is-a-secret). Three
facts about it are load-bearing enough to belong in the design rather than in a setup guide.

**Spark means no billing account, and that is the whole safety model.** A Google Cloud *free trial*
project is not the same thing and must not be used: a trial has a billing account attached, so it is
a Blaze project whose two exits are "deleted after 90 days" and "you clicked Upgrade and now it
bills". Neither is a home for a service meant to run indefinitely for free. The common alternative
advice — Blaze plus a budget alert — is worse than it sounds, because budget alerts are notifications
that fire after the spend, not enforcement. Spark's refusal is the only real ceiling.

**The database region is permanent and there is only one of them.** Spark allows a single database
instance, and its location cannot be changed after provisioning. The three choices are `us-central1`,
`europe-west1` and `asia-southeast1`; **`europe-west1` (Belgium) is the pick**, and *not* on latency
grounds — see below. The consequence for the client is that the database URL
is fixed and ends up in the shipped client either way. There is no server-side deploy: Pages serves
static files and the client talks straight to the database.

**Latency is not what picks the region, and assuming it does picks the wrong one.** A ply is one small
write and the opponent is thinking for minutes or hours; even in live mode a ~300 ms antipodean round
trip is invisible in a game with no real-time input. The two things that do distinguish the regions
are **where the players are** — the browser app sits on a public URL, so strangers are as likely to be
European as anything else — and **data residency**: the rows hold a display name and an anonymous uid,
which is minimal but not nothing, and keeping EU players' rows in the EU sidesteps the transfer
question rather than deferring it. Belgium wins on both; nearness to the author wins on neither.

### Why the config is a secret

The obvious reading is that it needn't be: Firebase documents web API keys and database URLs as
identifiers rather than secrets, and it is right — the config is downloaded into every visitor's
browser and compiled into every desktop binary, so nothing about publishing the app keeps it private.
By that argument a checked-in config costs nothing.

That argument answers the wrong question. The risk is not a *visitor* reading the key, it is a **fork**
inheriting the backend: clone, build, run, and now someone else's app is writing to this database and
spending its 100 connections, without anyone intending it. The database URL is what does that, not the
key. Keeping the config out of the source means a fork builds and runs perfectly well and simply points
nowhere, which is the correct default for somebody else's project.

So the config lives in a **`FIREBASE_CONFIG` repo secret**, injected at deploy time, with **no
checked-in fallback**. Consequences to build to:

- **Absence is a supported state, not a failure.** A build with no config must run with cloud play
  unavailable and everything else working — link play needs no account, LAN play needs no internet,
  and neither should break because a backend was not configured.
- **Local development uses the emulator**, which needs no config, no project and no login at all.
- **A real secret, when one appears.** Deploying rules from CI would need a service-account credential,
  which is secret in the ordinary sense; that is the case where a repo secret is protecting something
  rather than reducing discoverability.

Discoverability is the honest word for what this buys. The identifiers appeared in this document's
earlier revisions and remain in git history, so it is a deterrent rather than a boundary. The boundary
is, as ever, the [security rules](#what-the-rules-can-enforce-without-knowing-chess) — which are
deployed and tested — plus an HTTP-referrer restriction pinning browser use of the key to
`sebgod.github.io/*`.

**App Check cannot cover the desktop, and that caps how much abuse protection is available.** App
Check is the only real lever against a scripted client, but its built-in attestation providers are
reCAPTCHA Enterprise (web), Play Integrity (Android) and DeviceCheck/App Attest (Apple). There is no
desktop provider, and the custom-provider route needs a backend to mint tokens — which Spark cannot
host, because Cloud Functions are Blaze-only. **Enabling App Check enforcement on the database would
therefore reject Chess.GUI and Chess.Console outright.** So the choice is: enforcement off (App Check
in monitor mode for the web at most), with security rules and hard per-field size caps doing the real
work. Design the rules on the assumption that *any* client can reach the database, because on the free
tier that is true.

A related correction to a natural assumption: **API key restrictions do not protect the database.**
Firebase is explicit that restricting a key does not secure Realtime Database or Auth — rules and App
Check do. The rules are the boundary; the key is a project identifier.

### One key per front-end, because a referrer restriction excludes a native client

The browser key is restricted to `sebgod.github.io/*` and `localhost:*`. That is worth having for the
same reason the config is a secret — a fork deployed on another domain cannot use it — and for nothing
more; it guards an origin, not the data.

**The trap it sets is for every native front-end, and it is not theoretical.** An HTTP-referrer
restriction matches on the `Referer` header, and a request that sends none is refused outright. A
`HttpClient` calling Identity Toolkit sends none — from Chess.GUI, and equally from Chess.Droid, which
is the one that matters now. Measured against the live key:

```
no Referer                            -> 403  "Requests from referer <empty> are blocked"
Referer: https://sebgod.github.io/... -> 200  token issued
```

So the restriction that protects the web would make a native courier fail at sign-in, and fail in
the shape of a broken auth implementation rather than a key policy — which is a long way to debug from
the symptom.

Hence **two keys**: the browser key restricted as above, and a second key for the native front-ends
with no referrer restriction (API restrictions to Identity Toolkit and Realtime Database are still
worth setting, since those narrow what a leaked key can reach without depending on a header the caller
cannot send). Mint the second one when the Android transport lands, in phase 3b.

Android has one restriction option the desktop does not — *Android apps*, keyed on package name plus
signing-certificate SHA-1 — but it is worth knowing what it is before leaning on it: it is satisfied by
two request headers (`X-Android-Package`, `X-Android-Cert`), not by attestation, so any client that
chooses to send them passes. It is the same class of control as the referrer restriction, and deserves
the same weight: it keeps the key from being casually reused elsewhere, and it is not a boundary. The
rules are the boundary.

The consequence for `FIREBASE_CONFIG` is that `apiKey` becomes per-front-end while every other field
stays shared, so the secret holds either two configs or one config plus an override. Decide that when
the second key exists, not before.

The security rules themselves belong in the repository as `firebase/database.rules.json`, deployed
from there, never pasted into the console where they are unversioned and untested. See
[Testing](#testing) for why the emulator earns its keep on exactly this file.

## The schema is the link

```
/games/{gameId}
    g        "e2e4.e7e5.g1f3"     <- exactly GameLinkCodec's payload, nothing else
    n        3                    <- ply count, so the rules can gate turns (below)
    w        { uid, name }        <- seats
    b        { uid, name }
    updated  <server timestamp>

/open/{uid}                       <- the lobby: games with an empty seat, ONE PER HOST
    gameId, name, color, updated

/players/{uid}
    name                          <- the only thing stored about a person
```

A 200-ply game is ~1.2 KB. The 1 GB allowance is not a constraint anyone will ever feel; the 100
simultaneous connections is the one that binds.

## What the rules can enforce without knowing chess

Realtime Database security rules are the only server-side logic the Spark plan offers, and they turn
out to be enough for everything the store actually owes us:

- **Append-only history.** `newData.val().beginsWith(data.val())` on `g` — a write may only *extend*
  the move log, never rewrite or truncate it. One line, no chess knowledge, and it removes the entire
  class of "opponent rolled the game back to a position they liked".
- **Turn gating.** With `n` stored alongside, `n` must increase by exactly 1 per write, and the writer
  must be `w.uid` when `n` is even, `b.uid` when odd. That is a real turn gate expressed in arithmetic
  — the rules still have no idea what chess is.
- **Race-free seat claim.** `".write": "!data.exists()"` on a seat makes joining an open game atomic
  with no transaction: the first writer wins, the second is rejected.
- **Hard size caps on every field**, plus `"$other": { ".validate": false }` to reject any key that
  was not named here. Without these the project is a free 1 GB pastebin for whoever finds it. A move
  is 5 characters at most, `g` is bounded by `MaxPlies`, a display name 32.

### Spam, and the one thing rules cannot do

There is no secret to leak — the API key identifies the project, not the caller — so none of the above
depends on concealment. What it does depend on is that every write is *attributable* (`auth != null`,
and the uid must hold a seat) and *shaped* (capped fields, no unknown keys). Together those make abuse
pointless: there is nothing to read worth reading and nowhere to put anything.

The vector they do not close is **lobby spam**. Anonymous auth is free and unlimited, so a script can
mint uids in a loop and post junk open games, and rules cannot count — "at most three games per user"
is not expressible. Two structural answers carry most of the weight:

- **Key open games by uid, not by game id.** `/open/{uid}` makes "one open game per identity" a
  property of the path rather than something to count: a second post overwrites the first. This is why
  the schema above is shaped that way, and it is the single cheapest anti-spam measure available.
- **`onDisconnect()` makes a lobby slot cost a connection.** A mint-and-disconnect loop leaves nothing
  behind, and connections are capped at 100, so spam has to be *sustained* before it means anything.

What remains unclosable is a sustained scripted client, because App Check — the lever built for exactly
that — cannot cover the desktop front-ends (see above). The worst case is bounded and worth stating:
the free tier is exhausted, the app stops working for a while, and **no bill is generated**. That is
the trade Spark buys, and it is the right one here.

Operationally there is also a kill switch: pasting deny-all rules in the console makes the whole
database inert in seconds, with no deploy and nothing to roll back.

**`beginsWith` is verified, not assumed** (2026-09-12). The rules live at `firebase/database.rules.json`
with 16 tests in `firebase/rules.test.mjs`, run against the Firebase emulator: a write that *extends*
the log is accepted, and writes that *rewrite* or *truncate* it are rejected by the server. The turn
gate, the race-free seat claim, the unknown-field rejection and the size caps are pinned there too.

Two things about running them. They need **no Firebase account, project or login** — a `demo-`
prefixed project id makes the emulator run fully offline, which is why this step can be done long
before any console setup. And they need **Java 21+**, while this repo's Android head pins **JDK 17**
(a newer JDK breaks the SDL3-CS.Android build). Do not reconcile that by changing the machine's
`JAVA_HOME`: firebase-tools reads `java` from `PATH` and ignores `JAVA_HOME`, so put a 21+ JDK on
`PATH` for that one command and leave Android's alone.

Identity is **Anonymous Auth** (free to 50k monthly active users, no payment method) — enough to get a
uid to pin writes to, with no email, no password and no personal data stored beyond a display name the
player typed. The web API key is public by design; the rules are the security boundary, not the key.

## Presence without Cloud Functions

**The Spark plan has no Cloud Functions** — they require Blaze. So there is no server-side cron, and
anything that expires has to expire some other way. Two answers, and the first is better than it
sounds:

- **Lobby entries expire by themselves.** RTDB's `onDisconnect()` is part of the database protocol,
  not a Cloud Function, so it works on the free plan: a client registers "delete my `/open` row when
  my connection drops" *at the server* when it arrives. That is precisely the semantic LAN.Lib's
  self-expiring peer table gives on the LAN, and it means a crashed client does not leave a ghost
  opponent in the lobby.
- **Finished and abandoned games need a client-driven sweep.** Delete on game end, and let any client
  opening the lobby prune rows older than N days. Ugly but adequate, and the alternative is a billing
  account.

## Live and correspondence are the same store

The same rows serve both, and this is where courier 2 meets the spine:

- **Both players present** -> the remote side is a `NetworkPlayer` in `GameLoop`'s engine-shaped slot,
  exactly as LAN play works today. `NetworkSession`'s queue-drain shape (`TryDequeueMove`) is already
  transport-agnostic.
- **Opponent absent** -> you make your move, it is written, you close the app. That is the link
  courier's semantics — `MoveLockSide`, the inbox entry, the "your move" list — with the network
  doing the carrying.

The store does not distinguish them. Only the session lifetime differs, which is why the inbox has to
exist before this phase, not after it.

## What Chess.Net already gives us, and the one thing it doesn't

Most of the LAN stack is courier-agnostic already, because `SessionProtocol` was written as plain
line-oriented ASCII ("the same *UCI token you replay through the rules engine* spirit as
`GameLinkCodec`/`GameStore`"), and because the transport sits behind an interface for testability:

| Type | Cloud reuse |
|---|---|
| `SessionProtocol` | **Verbatim.** `CHESSLAN 1 MOVE e2e4` is already the right size and shape for a DB write |
| `NetworkSession` | **Verbatim.** It owns an `ILanConnection` and a queue; neither is TCP-specific |
| `NetworkPlayer`, `NetworkGame`, `LocalNetworkPlayer` | **Verbatim.** `IEngineBasedPlayer` over a session |
| `ISessionTransport` / `ILanConnection` | **Shape fits** — "dial, send lines, receive lines" describes a DB channel as well as a socket |
| `LanLobby` | **Does not fit.** It takes a `LanDiscovery` in its constructor and `Peers => _discovery.PeersOf(...)` |
| `LanPlayStack` | Cloud needs a sibling `CloudPlayStack`; the "open and tear down as a unit" shape carries over |

So the one genuine refactor courier 2 forces is **extracting an `ILobby`** (state, joinable list, an
action, a `NetworkSession` comes out) so the front-ends' lobby widgets don't fork per courier. That
extraction has one real wrinkle worth naming now rather than discovering later: LAN's handshake is
*invite -> accept/decline* between two present peers, while the cloud's is *post an open game ->
someone claims the seat*. These are not the same state machine, and `LobbyState` currently encodes the
LAN one (`Inviting`, `IncomingInvite`, `Declined`). The interface has to be drawn above that
difference or it will leak; that is the design risk in this phase, and it is a small one.

A naming consequence: `ILanConnection`, `LanLobby`, `LanPlayStack` become partial lies. `Chess.Net` is
not a published package and has no external consumers, so renaming `ILanConnection` to
`ISessionConnection` is a free, mechanical change. Do it as part of this phase rather than leaving the
next reader to wonder why the cloud opens a "LAN" connection.

## A native client needs no SDK

This is phase 3b's transport, and Chess.Droid is its first consumer. There is no good Firebase .NET
client for this: the options are not AOT-friendly, and `Chess.Net` is
`IsAotCompatible` with — deliberately — **zero** packages beyond LAN.Lib ("Sockets come from the BCL
... so no extra packages").

It doesn't need one. Realtime Database exposes a REST API where appending `.json` to a path reads or
writes it, and where `Accept: text/event-stream` turns a GET into a **server-sent event stream** of
`put`/`patch` events. That is `HttpClient` plus a line reader — which is exactly the surface
`ILanConnection` already describes. Anonymous sign-in is likewise one HTTPS POST to the Identity
Toolkit REST endpoint, returning a token passed as a query parameter on subsequent calls.

The one caution is JSON: the payloads are tiny (a string, two names, an integer), but reflection-based
`System.Text.Json` would break AOT and the assembly's no-reflection stance. Use a source-generated
`JsonSerializerContext`, or hand-roll — at this size hand-rolling is genuinely defensible and matches
how `SessionProtocol` already treats the wire.

## The browser is the hard part

Chess.Web is where the cloud pays off — it is the front-end with no LAN play, and the one a stranger
can reach without installing anything — and it is also where the work is:

- **It does not reference `Chess.Net` at all** today (only Chess.Lib and Chess.UCI, and Chess.UCI
  "for `UciMove` + `GameLinkCodec` only"). Whether the browser takes a dependency on Chess.Net or the
  session types move somewhere shared is a real decision, not a detail.
- **WASM cannot open a TCP socket**, so the cloud courier is not merely the browser's best transport,
  it is the only one it can ever have.
- The natural client is the **Firebase JS SDK via JS interop**, a pattern the repo already has twice:
  `wwwroot/js/chess-canvas.js` for the canvas blit, and WebGl.Renderer's `[JSImport]` command buffer.
  A single-threaded WASM runtime also means the reader must not block — the same constraint the WebGL
  work already documents.

An alternative worth pricing before committing: the same REST + SSE path the desktop uses works in a
browser too (`EventSource`), which would avoid the SDK and the extra payload entirely. If that holds,
both front-ends share one client and the browser stops being the hard part.

## The README's promise is at stake

> Play by Link ... **No accounts, no server, no logins**

That is currently a selling point, printed twice. Courier 2 adds an account and a server, so the
wording has to change — and the change must keep the promise true for courier 1, which stays the
default. Something like: *link play needs no account; online play with a stranger needs a one-tap
anonymous sign-in.* Deciding this is part of the cloud phase, not an afterthought, because getting it
wrong reads as a bait-and-switch to exactly the audience the feature was written for.

## What this does not fix

**Engine assistance.** A player running a strong engine alongside the app is undetectable by any
backend design, free or paid, and nothing in this plan addresses it. Illegal moves are already handled
(both clients validate, and the append-only rule stops history rewriting). Worth stating plainly so
that no one later reaches for a server-side "anti-cheat" that this architecture cannot support.

---

## Phasing

| Phase | Scope | Where | Status |
|---|---|---|---|
| 1 | **Link play in the GUI**, end to end and with no new plumbing: `args` on `Program.cs`, `StartupWizardOptions.LinkPlay` on `VkStartupMenu`, paste-a-link (Ctrl+V, `SDL.GetClipboardText`), the turn semantics above, and "copy reply link" (Ctrl+L, `SDL.SetClipboardText`) | chess | **Done** — live-verified, see below |
| 2 | **The inbox:** multi-slot store (`GameInbox`) + a "your move" list + staleness, and the GUI picker over it | chess | **Done** — live-verified |
| 3a | **Cloud courier in the browser:** Firebase JS SDK via `[JSImport]`, anonymous auth, the schema and rules above, lobby UI in `Play.razor`, the README wording | chess | Backend live and verified; client not started |
| 3b | **Cloud courier on Android:** REST + SSE in `Chess.Net`, the `ILobby` extraction + `ISessionConnection` rename, a second API key with no referrer restriction, a cloud lobby beside the LAN one in `MainActivity` | chess | Not started |
| 4 | **`chess://` registration** (`--register-protocol`) + `InstanceGate` claim/hand-off + `WindowActivation.Activate`, building or reusing the drain; explicit `PackageReference` on `SharpAstro.AppShell` | chess | Not started |
| 5 | *Optional cleanup:* a public, non-`DEBUG` per-iteration hook on `SdlEventLoop` so the drain stops living in a side-effecting predicate | SdlVulkan.Renderer | Not started |

**The drain belongs to phase 4 alone**, though an earlier version of this table put it in phase 2 and a
later one had it shared with the cloud. It has no producer until a payload can arrive from off-thread
in an **SDL** host *whose window can be minimized*, and neither cloud client is one. The problem the
drain exists to solve is `SdlEventLoop` excluding minimized windows from `anyNeedsRedraw`: the browser
has no such window, and Android — an SDL host, but one with no minimized state — already drains
off-thread arrivals through volatile flags named in its own redraw predicate. That leaves the
`InstanceGate` hand-off as the drain's only producer. Building it before then would
be plumbing with nothing flowing through it — the same objection this document raises against doing the
gate early.

**Phase 1 is done except for being watched.** What landed: `GameLinkCodec.ExtractBody` (one reduction
for a page URL / `chess://` / bare fragment / bare body, folded into `TryDecode` so no host parses) and
`GameLinkCodec.IsContinuationOf` (the opponent's reply vs a different game — compares decoded plies,
never the encoded string); `GameSession` handling `PlayByLink` (`LocalSide`, the `MoveLockSide` gate
re-armed across reset, flip-to-local, and `CorrespondentSideFor` stating the turn rule once); the GUI
wizard entry; a link on argv skipping the wizard; and Ctrl+L / Ctrl+V. No engine process is spawned for
a link game, and `Continue` resumes one for free because `GameStore` already persists the mode and the
correspondent's colour.

**Verified in a running window**, driven through the SDL debug inspector: a link on argv opens straight
into the game with no wizard (`screen=game`, `plyCount=3`), the board is flipped to the local player's
side, the one local move commits (ply 4), a move for the *other* colour is refused with the ply count
unmoved — the correspondence gate doing its whole job — Ctrl+L puts
`https://sebgod.github.io/chess/#g=e2e4.e7e5.g1f3.b8c6` on the clipboard (the web URL, so a recipient
with nothing installed can still play it), and Ctrl+V takes the correspondent's reply and continues the
game at ply 5 with the gate re-armed.

That verification was blocked for most of its development: the SDL inspector needs a **Debug** build
against **local siblings**, and the sibling renderer's unreleased `OnKeyDown` change broke exactly that
build while CI, pinned lower, stayed green. Releasing the sibling set onto DIR.Lib 8.19 is what
unblocked it — which is a fair warning that "CI is green" and "the thing can be looked at" are
different claims.

**Everything after phase 1 is reach.** That ordering is deliberate and worth
defending: a link pasted into the app is already the whole of correspondence play, and it needs no
registry, no scheme, no gate, no account and no Google project. **Phases 1-2 are a complete, shippable
feature with no external dependency, and stopping there strands nothing.**

Two orderings inside that are less obvious:

- **The scheme is last among the chess phases, not second.** `InstanceGate` only earns its keep once a
  *scheme* exists, because a scheme is what spawns a fresh process per click — the only problem the
  gate solves. It was phase 2 when this was a link-only plan; the cloud work outranks it because the
  cloud adds a capability and the scheme adds convenience to one that already works.
- **The cloud courier goes to the browser first and Android second**, and the browser's turn is first
  on cost rather than on merit: the JS SDK is an afternoon, and it proves the deployed rules from our
  own code before Android pays for the shared plumbing. Android is where correspondence play is
  actually used, so it is not a follow-up to be quietly dropped. Chess.GUI and Chess.Console keep link
  play, which is cross-platform — a desktop player and a browser player can already play each other by
  swapping links — so the gap left open is a *desktop* player meeting a stranger, which LAN covers for
  the same room and links cover for anyone reachable by message.

## Open questions

- **A link or push arriving mid-game.** Discard the current game, or prompt? The web has no equivalent
  (a new link is a new tab), so this is a genuinely new decision. Suggested: prompt. With the phase-2
  inbox the answer gets easier — a new game is a new entry, and nothing has to be discarded at all.
- **Who is allowed to claim an open game?** Anyone, or only whoever has the game id? "Anyone" is a
  lobby and is the point; it is also the abuse surface. A "private game" that only appears to whoever
  holds the id is the cheap middle — and it is, again, a link.
- **Chess.Console.** Phase 1 applies almost unchanged (the wizard and codec are shared, and the TUI has
  its own clipboard story), and the console already references `Chess.Net`, so phase 3 reaches it too.
  Worth doing, not scoped here.
- **Chess.Droid.** An Android intent filter for `chess://` is the natural analogue of phase 5, and
  `Activate()` is already referenced on the android TFM. Separate plan.
- **Drag and drop.** `OnDropFile` is already wired in the backend and unused, so dropping a saved
  `.uci` game onto the window is nearly free — but nobody receives a correspondence game *as a file*,
  so this is an extra, not a phase.

## Identity: anonymous by default, Google as an upgrade

This was an open question — anonymous auth gives a fresh uid per browser profile, and on a phone one
bound to the device, so switching handsets makes you a new person with no games. The answer is to
offer **Google sign-in as an optional upgrade**, not to require accounts.

**The rule that matters, because the obvious implementation gets it wrong: link, do not replace.**
Firebase can *link* an anonymous account to a Google credential and keep the SAME uid. Signing in with
Google as a separate action instead mints a DIFFERENT uid — and every seat in this design is pinned to
uid (`data.child('w/uid').val() === auth.uid`), so the player is locked out of their own games by a
rule working exactly as intended. State it once, here: **anything that changes a player's uid orphans
their games.** The same trap arrives from another direction as Firebase Auth's "auto clean-up", which
deletes anonymous accounts older than 30 days — deliberately left OFF, because a correspondence game
may legitimately run for months (this document's own staleness threshold is 120 days), and an identity
that expires inside a live game is worse than no cleanup at all.

Anonymous therefore stays the default: it is what keeps the README's "no accounts, no logins" true, and
nobody is ever forced through a sign-in wall. Google is there for a player who wants their games to
survive a reinstall or follow them to another machine.

Uneven across the front-ends, which is why it is web-first: a popup on the web, natural on Android, and
genuinely awkward on the SDL desktop, where OAuth needs a system browser plus a loopback redirect
listener. The desktop stays anonymous-only until somebody wants it enough to build that.

Not enabled yet — nothing can use it, and it carries a small authorised-domains tail. This records the
decision so that whoever implements sign-in does not reach for the obvious `signInWithPopup` and
silently strand every game on the device.

Worth knowing that the Android escape hatch is not "add real accounts" either: the **Restore
Credentials API** carries a sign-in across a device transfer, which is the mechanism behind Google
Play's April 2027 Zero-Tap Sign-In requirement (a requirement chess is doubly clear of — games are
exempt, and Chess.Droid is not distributed through Play).

## Testing

- **Codec parity** is already covered by `Chess.Tests/GameLinkCodecTests.cs`; no courier adds a codec
  and so none needs codec tests. What they need is a test that the *payload reduction* (`#` / `?` /
  bare / HTTP body) hands `TryDecode` the same body for all four shapes.
- **Turn semantics** — assert `MoveLockSide` / `FlipBoard` / `localSide` against a decoded payload, the
  same assertions `Chess.Web.E2E.Tests/PlayByLinkTests.cs` makes in the browser.
- **The cloud courier needs no cloud to test.** `Chess.Tests/Lan/FakeLanBus.cs` already stands in for
  the whole LAN "so `LanLobby` can be tested with no real sockets (CI-safe)", wiring the node under
  test exactly as `LanPlayStack` wires it. A fake DB behind `ISessionTransport` is the same trick in
  the same file's pattern — a further argument for drawing `ILobby` so that both lobbies are testable
  by one harness.
- **The append-only rule deserves one test against a real database**, because it is the only piece of
  logic that does not live in this repository. The Firebase emulator runs locally and free; a rules
  test asserting that a truncating write is rejected is worth more than any amount of client-side care.
- **The hand-off and the minimized push** are integration tests, not unit tests: launch instance A,
  deliver a payload, assert A applied it. The SDL debug inspector can drive and screenshot A
  headlessly, which is what makes this testable at all — and it is the one test that would catch the
  minimized-window drain bug described above, so **minimize A first**.
