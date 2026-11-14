# Chess on the iPad

`Chess.iOS` is the iPad head. The game is `Chess.Touch`, the same one `Chess.Droid` runs; the head
only says what iOS does differently (`ChessIosApp`, an `ITouchPlatform`). SdlVulkan.Renderer 7.90 is
the first version with the iOS host (`SdlVulkanIosApp`) and draws through MoltenVK.

**Not yet run on a device.** The first install is the test.

## Build

Only on macOS, and CI does it: the `ios` job in `.github/workflows/dotnet-desktop.yml` fetches the
two native frameworks (`scripts/fetch-ios-frameworks.sh`: SDL 3.4.16's `SDL3.xcframework`, MoltenVK
1.4.2's dynamic `MoltenVK.xcframework`), builds for `ios-arm64` with signing off, checks the bundle,
and uploads `Chess.ipa` as the `chess-ios-ipa` artifact (kept 14 days).

The `.ipa` is **unsigned on purpose**. There is no Apple Developer membership for this app, and none
is needed to try it: a sideloading installer signs it on install with the tester's own Apple ID.

## Install with AltStore

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
network. A free ID may have **3** sideloaded apps at once (AltStore is one of them) and register 10 app
ids a week.

SideStore is the alternative that needs a computer only once (to make a pairing file), and then renews
from the iPad itself.

## What to check, in order

1. It launches and the menu draws, sharp (MoltenVK is the untried part).
2. Touch: menu taps commit on release, board taps on touch-down, setup drags.
3. Rotation: the board relayouts and stays clear of the rounded corners and the home indicator.
4. Background and back: the surface must come back (Android recreates it there; iOS is untried).
5. A game against the computer, across the table, Continue after the app is killed.

## The log

The app writes the renderer's diagnostics to `Documents/chess.log`, replaced on each launch, which the
Files app shows under On My iPad > Chess. Send that file with any report.

`chess.log` only starts once SDL and Vulkan are up. A crash before that, or any exception that reaches
native code, is appended to `Documents/chess-crash.log` next to it (hooked first thing in `Main`). If the
app closes the moment it opens, that is the file to send. With no Chess folder at all, send the newest
`Chess.iOS-...ips` from Settings > Privacy & Security > Analytics & Improvements > Analytics Data.

## Known gaps

- **LAN play is not expected to work in a sideloaded build.** Receiving UDP broadcast on iOS 14+ needs
  Apple's multicast networking entitlement, which is granted on request and which a free signing
  account cannot carry. The online (cloud) game does not depend on it, and works when the build carries
  the backend config (the `FIREBASE_CONFIG` secret, as for Android). Only a build from `main` (or a manual
  run) carries it: a pull request's `.ipa` is downloadable by anyone signed in to GitHub, and the config
  holds the native API key, so a PR build has no Online game.
- No back gesture on iOS. An attached keyboard's Esc steps back from a game to the menu, as Android's
  back does; at the menu it does nothing, since an iOS app never sends itself home.
