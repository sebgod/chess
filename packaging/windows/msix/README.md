# MSIX package for Chess.GUI

`Chess.GUI`, packaged for the Microsoft Store.

```powershell
# from the repo root
dotnet publish Chess.GUI/Chess.GUI.csproj -r win-arm64 -c Release -p:UseLocalSiblings=false
./packaging/windows/msix/build-msix.ps1 `
    -PublishDir Chess.GUI/bin/Release/net10.0/win-arm64/publish `
    -Arch arm64 -Version 1.0.0.0 -OutFile artifacts/Chess-arm64.msix
```

Pass `-p:UseLocalSiblings=false` unless you want the sibling working copies. It is forwarded to the
inner `dotnet publish` of Chess.Engine (see `PublishEngineNative` in `Chess.GUI.csproj`) — without
that forwarding the inner publish re-runs the auto-detection, picks up the sibling `DIR.Lib`, and
fails with `NETSDK1207` because a netstandard2.0 **source generator** project cannot be AOT compiled.

## Why MSIX, and why only with the Store

**The Store re-signs the package after certification.** That is the whole reason for this format:
free, no certificate to buy or rotate, and no SmartScreen warning at all — better than anything
achievable with a certificate we bought ourselves. An MSI or a `.exe` through the Store gets none of
that; Microsoft does not re-sign those.

**The counterpart trap: MSIX cannot ship unsigned at all.** A signature is structural;
`Add-AppxPackage` refuses an untrusted package outright, with no click-through equivalent to
SmartScreen's "run anyway". So MSIX is better than a tarball *with* the Store behind it and worse
without: today's unsigned `.tar.gz` runs for anyone willing to click past a warning, and an unsigned
`.msix` on the same release page installs for nobody. The tarballs stay.

## Identity is a placeholder

The manifest currently carries a **development** identity (`SharpAstro.Chess.Dev`,
`CN=SharpAstro-Chess-Dev`) so the package can be built, signed and installed locally today. A real
submission needs the three values Partner Center assigns for the reserved app — `Name`, `Publisher`
and `PublisherDisplayName` — and they must match byte for byte, or the upload is rejected with an
identity mismatch that does not say which field was wrong. `build-msix.ps1` warns while the
development identity is still in place.

## chess:// comes from the manifest, not the registry

A packaged build registers the scheme through `uap:Extension Category="windows.protocol"`: Windows
wires it up on install and removes it on uninstall. `--register-protocol` is for the **unpackaged**
builds (the tarballs, and a run-from-folder dev build) and for Linux, where it writes a `.desktop`
file. Do not run it from inside a package — MSIX virtualizes `HKCU`, so it would write into the
package's private hive and achieve nothing.

Because a custom scheme has no `UserChoice` arbitration, declaring it here makes this app the handler
outright. That is *unlike* a file-type association, which an installer cannot claim on Windows 10/11
(see tianwen's `FileAssociation.wxs` for that whole story).

## Testing it locally

**`-AllowUnsigned` cannot work here, and Developer Mode does not change that.** The flag is not
"install without checking a signature"; it admits packages whose *publisher* sits in the unsigned
namespace. Ours does not, so deployment refuses it:

```
Add-AppxPackage: Deployment failed with HRESULT: 0x80073D2C, The package deployment failed
because its publisher is not in the unsigned namespace.
```

Sign it with a throwaway certificate whose subject matches the publisher instead:

```powershell
./packaging/windows/msix/build-msix.ps1 -SignPackage artifacts/Chess-arm64.msix
```

That mints (or reuses) a self-signed code-signing certificate in `Cert:\CurrentUser\My`, signs the
package, and exports the public half next to it. Trust it **once**, from an elevated prompt:

```powershell
Import-Certificate -FilePath artifacts/Chess-arm64.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then install without elevation, and without `-AllowUnsigned` — it is signed now:

```powershell
Add-AppxPackage artifacts/Chess-arm64.msix
Get-AppxPackage SharpAstro.Chess.Dev            # confirm
start "chess://play?g=e2e4"                     # the scheme, with no registry write anywhere
Remove-AppxPackage (Get-AppxPackage SharpAstro.Chess.Dev).PackageFullName
```

The Store signature on a real submission is unrelated to any of this; the local one is discarded at
certification.

## Multi-architecture

Partner Center takes one `.msixbundle` containing a package per architecture:

```powershell
# after building Chess-x64.msix and Chess-arm64.msix into artifacts/
./packaging/windows/msix/build-msix.ps1 `
    -BundleDir artifacts -BundleVersion 1.0.0.0 -BundleOut bundle/Chess.msixbundle
```

## Checks the script runs before packing

Cheap, and each one stands for a failure that is otherwise found late:

| Check | Why |
|---|---|
| Version is a four-part quad, revision 0 | makeappx's schema complaint does not name the version, and Partner Center rejects a non-zero revision at upload |
| Every asset the manifest names exists and is square at its stated size | Accepted by makeappx, rejected by Store certification |
| `Chess.GUI.exe` and `chess-engine.exe` are in the publish tree | A package missing the engine installs, starts, plays humans, and dies the moment anyone picks Player vs Computer |
| `EntryPoint` is `Windows.FullTrustApplication` | Anything else is a UWP entry point, where spawning the engine, the `InstanceGate` named pipe, and LAN loopback between two local instances are all forbidden |
| `windows.protocol` declares `chess` | Dropping it silently loses link handling in packaged builds, with no way to get it back |

`-ValidateOnly` runs the manifest-only subset with no publish and no packing, which is what CI can
afford on every push.
