# Chess on the iPad

`Chess.iOS` is the iPad head. The game is `Chess.Touch`, the same one `Chess.Droid` runs; the head
only says what iOS does differently (`ChessIosApp`, an `ITouchPlatform`). SdlVulkan.Renderer's iOS host
(`SdlVulkanIosApp`, since 7.90) brings up SDL and draws through MoltenVK.

It runs on an iPad (iPadOS 26, sideloaded with AltStore since 2026-11-15): menus, games against the
computer and across the table, and LAN play against the desktop GUI.

## Build

Only on macOS, and CI does it: the `ios` job in `.github/workflows/dotnet-desktop.yml` builds for
`ios-arm64` with signing off, checks the bundle, and uploads `Chess.ipa` as the `chess-ios-ipa`
artifact (kept 14 days). The two native frameworks the app bundles (SDL 3.4.16's `SDL3.xcframework`,
MoltenVK 1.4.2's dynamic `MoltenVK.xcframework`) come with SdlVulkan.Renderer's package since 7.109:
its first build fetches the pinned releases into `obj/`, so chess carries no copy of that plumbing.

The `.ipa` is **unsigned on purpose**. There is no Apple Developer membership for this app, and none
is needed to try it: a sideloading installer signs it on install with the tester's own Apple ID.

## Install with AltStore

The device side in full (the Windows setup and its traps, USB or Wi-Fi, installing in place with pymobiledevice3,
logs and crash reports) is the org's
[docs/ios-device.md](https://github.com/SharpAstro/.github/blob/main/docs/ios-device.md), shared with every iOS app on
the stack. In short:

Once:

1. On a computer on the same Wi-Fi as the iPad (or with it on USB), install AltServer from
   altstore.io. On Windows it needs iTunes and iCloud from Apple's website, NOT the Microsoft Store
   versions.
2. Use AltServer to install AltStore on the iPad, signing in with a **personal** Apple ID (a free one
   is enough; never a work account).
3. On the iPad: Settings > Privacy & Security > Developer Mode, on (iPadOS 16+ asks after the first
   sideloaded install), and trust the Apple ID under Settings > General > VPN & Device Management if
   asked.

Each build:

1. Download `chess-ios-ipa` from the run, unzip it to get `Chess.ipa`, and put it on the iPad (AirDrop,
   iCloud Drive, or the Files app).
2. AltStore > My Apps > **+**, pick `Chess.ipa`.

A free Apple ID's signature lasts **7 days**; AltStore renews it while AltServer is running on the same
network (or open AltStore > My Apps > Refresh All by hand). A free ID may have **3** sideloaded apps at
once (AltStore is one of them) and register 10 app ids a week.

**When the 7 days ran out.** AltStore and Chess stay installed, with their data, but neither opens. Do
not delete them. With the iPad on USB: AltServer > Install AltStore over the old one, then on the iPad
trust the Apple ID again under Settings > General > VPN & Device Management (a new certificate means
"Untrusted Developer" once more), then AltStore > My Apps > Refresh All. The saved game and profile
survive.

SideStore is the alternative that needs a computer only once (to make a pairing file), and then renews
from the iPad itself.

## What to check, in order

1. It launches and the menu draws, sharp.
2. Touch: menu taps commit on release, board taps on touch-down, setup drags.
3. Rotation: the board relayouts and stays clear of the rounded corners and the home indicator.
4. Background and back: the surface must come back (Android recreates it there).
5. A game against the computer, across the table, Continue after the app is killed.

Still untried on the device: rotation in the middle of a game, Continue after a kill, background and
back, and setup drags.

## The log

The renderer's iOS host writes its diagnostics to `Documents/chess.log` (its default, named after the
window title), replaced on each launch and headed by the version, the device model and the OS version.
The Files app shows it under On My iPad > Chess. Send that file with any report. From a PC with the iPad
on USB or the same Wi-Fi it also comes off with
`python -m pymobiledevice3 apps pull org.sebgod.chess.<team> Documents/chess.log <local file>`, where
`<team>` is the suffix AltStore gives the bundle id (`python -m pymobiledevice3 apps list` shows it).

`chess.log` starts when SDL hands control to the host, before Vulkan is up. A crash before that, or any
exception that reaches native code, is appended to `Documents/chess-crash.log` next to it (hooked first
thing in `Main`). If the app closes the moment it opens, that is the file to send. With no Chess folder at
all, send the newest `Chess.iOS-...ips` from Settings > Privacy & Security > Analytics & Improvements >
Analytics Data.

## Known gaps

- **LAN play works in a sideloaded build** (an iPad against the desktop GUI, 2026-11-15), although
  Apple documents its multicast networking entitlement, which a free signing account cannot carry, as
  needed to receive UDP broadcast on iOS 14+. Expected to fail, it did not; if a later iPadOS starts
  enforcing it, that entitlement is the cause.
- **The online (cloud) game** works when the build carries the backend config (the `FIREBASE_CONFIG`
  secret, as for Android). Only a build from `main` (or a manual run) carries it: a pull request's `.ipa`
  is downloadable by anyone signed in to GitHub, and the config holds the native API key, so a PR build
  has no Online game.
- No back gesture on iOS. An attached keyboard's Esc steps back from a game to the menu, as Android's
  back does; at the menu it does nothing, since an iOS app never sends itself home.
