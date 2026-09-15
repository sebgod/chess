# Google Play package for Chess.Droid

`Chess.Droid`, packaged as an Android App Bundle for Google Play.

```bash
# what CI does, minus the signing
dotnet publish Chess.Droid/Chess.Droid.csproj -c Release -p:UseLocalSiblings=false \
    -p:ApplicationVersion=207 -p:ApplicationDisplayVersion=1.2.207
./packaging/android/check-aab.ps1 \
    -Bundle Chess.Droid/bin/Release/net10.0-android/org.sebgod.chess-Signed.aab
```

The `aab` job in `.github/workflows/dotnet-desktop.yml` builds this on every push to main and
uploads it as the `chess-aab` artifact.

## 16 KB page alignment, which is the one that bites

Play refuses a bundle whose **64-bit** native libraries are not aligned to 16 KB pages. Every `.so`
the .NET Android SDK emits already is; the exposure is entirely third-party natives.

Chess had exactly one: `libSDL3.so`. Under `SDL3-CS.Android 3.4.10.5` it was the **only misaligned
library in a bundle of 90**, and would have failed the upload. `3.4.16` fixes it for `arm64-v8a`
and `x86_64` — the two ABIs a Release build ships. Its 32-bit libraries are still 4 KB aligned,
which does not matter: 16 KB pages are a 64-bit concern and those ABIs are not published.

The build does say so, as warning **XA0141** — but a warning inside a 90-line publish log is not a
gate, which is why `check-aab.ps1` exists and why CI runs it.

**When bumping the SDL pin, keep the Java and native halves coherent.** SDL compares them at startup
and aborts on a mismatch; `3.4.12.x` shipped Java 3.4.10 against native 3.4.12 and was unusable for
exactly that reason. It is checkable from the packages, with no device — see the comment in
`Chess.Droid.csproj`, which gives the two commands.

## Signing: an upload key you keep, an app signing key Google keeps

Play App Signing means Google holds the key that signs what users install. You still sign the
**upload** yourself, with a key Play learns on first upload, so it can tell your uploads from
anyone else's. A bundle signed with the auto-generated debug key is refused by name.

Create the upload key once, and back it up somewhere that is not this repository:

```bash
keytool -genkeypair -v -keystore chess-upload.keystore -alias chess-upload \
    -keyalg RSA -keysize 4096 -validity 10000
```

Then four repository secrets, which is all the `aab` job needs to produce an uploadable bundle:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | `base64 -w0 chess-upload.keystore` |
| `ANDROID_KEYSTORE_PASSWORD` | the store password |
| `ANDROID_KEY_ALIAS` | `chess-upload` |
| `ANDROID_KEY_PASSWORD` | the key password |

Without them the job still runs and still checks the bundle; it just produces a debug-signed one
and says so in the step summary. Losing the upload key is recoverable — Play can reset it — which is
the one way it is less frightening than the app signing key, which Google holds precisely so that
losing yours is not fatal.

## What the build already satisfies

| Requirement | State |
|---|---|
| Target API level | 36, from `net10.0-android` — meets the current floor for new apps |
| Minimum API level | 24, from `SupportedOSPlatformVersion` |
| 64-bit | `arm64-v8a` + `x86_64`; no 32-bit ABI is published at all |
| 16 KB pages | checked by `check-aab.ps1`, enforced in CI |
| App bundle, not APK | `dotnet publish` produces `.aab` |
| `versionCode` strictly increasing | the CI run number |

## What has to happen outside the repository

Roughly in order, because the first one takes the longest by far:

Which console each of these lives in, and what a CLI can do instead, is [docs/google-ops.md](../../docs/google-ops.md); `scripts/google-audit.ps1` reports the
current state of the secrets and the Firebase side.

1. **Closed testing.** A personal developer account registered after 13 November 2023 must run a
   closed test — at the time of writing, **12 testers opted in for 14 continuous days** — before it
   can apply for production access. The console states the rule that applies to your account; check
   it on day one, because it is measured in weeks and nothing else here is.
2. **Create the app** in the Play Console and take `org.sebgod.chess` as the package name. It is
   permanent: it cannot be changed, reused, or recovered after the app is deleted.
3. **Privacy policy URL.** Required for every app. The obvious home is the existing Pages site
   (`sebgod.github.io/chess`), which already deploys from this repository.
4. **Data safety form.** As it stands the Android app collects nothing: LAN play is device-to-device
   on your own network, there is no analytics and no crash reporting. **This stops being true when
   cloud play reaches Android (#51)** — anonymous auth gives each install an identifier, and that is
   a declarable change.
5. **Content rating** questionnaire (chess rates at the bottom of every scale) and the target
   audience declaration.
6. **Store listing:** app icon 512x512, feature graphic 1024x500, at least two phone screenshots,
   plus 7-inch and 10-inch tablet screenshots — worth doing properly here, because the
   across-the-table mode is a tablet feature and the tablet screenshots are where it can be shown.

## Before any of it: the app should be run

`Chess.Droid`'s LAN play has **never been run on a device** (#15). It is offered in the startup menu,
so a tester will find it. Shipping a mode nobody has executed is the kind of thing a closed test is
for, but it is better to know first.
