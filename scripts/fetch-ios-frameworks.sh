#!/usr/bin/env bash
# Downloads the two dynamic frameworks Chess.iOS bundles into Chess.iOS/Frameworks/ (gitignored):
#   SDL3.xcframework     from SDL's own release .dmg, the version Chess.Droid's SDL pair uses (see
#                        Chess.iOS.csproj for why the managed bindings may be newer)
#   MoltenVK.xcframework from Khronos's MoltenVK release (Vulkan on Metal; iOS has no Vulkan)
# macOS only (hdiutil). Idempotent: a framework already present is kept.
set -euo pipefail

SDL_VERSION="3.4.16"
MOLTENVK_VERSION="v1.4.2"

here="$(cd "$(dirname "$0")/.." && pwd)"
dest="$here/Chess.iOS/Frameworks"
mkdir -p "$dest"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ ! -d "$dest/SDL3.xcframework" ]; then
  echo "SDL $SDL_VERSION"
  curl -fsSL -o "$work/SDL3.dmg" \
    "https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL3-$SDL_VERSION.dmg"
  hdiutil attach -nobrowse -readonly -mountpoint "$work/sdl" "$work/SDL3.dmg" > /dev/null
  cp -R "$work/sdl/SDL3.xcframework" "$dest/"
  hdiutil detach "$work/sdl" > /dev/null
fi

if [ ! -d "$dest/MoltenVK.xcframework" ]; then
  echo "MoltenVK $MOLTENVK_VERSION"
  curl -fsSL -o "$work/MoltenVK-ios.tar" \
    "https://github.com/KhronosGroup/MoltenVK/releases/download/$MOLTENVK_VERSION/MoltenVK-ios.tar"
  tar -xf "$work/MoltenVK-ios.tar" -C "$work"
  # The DYNAMIC one: the renderer loads it by path, which a static library cannot be.
  framework="$(find "$work" -type d -path '*dynamic*' -name 'MoltenVK.xcframework' | head -n 1)"
  if [ -z "$framework" ]; then
    echo "No dynamic MoltenVK.xcframework in MoltenVK-ios.tar $MOLTENVK_VERSION; its layout was:" >&2
    find "$work" -maxdepth 4 -name '*.xcframework' >&2
    exit 1
  fi
  cp -R "$framework" "$dest/"
fi

# The device slice each must carry, at the path SdlVulkan.Renderer's AppleVulkan loads from.
for f in "SDL3.xcframework/ios-arm64/SDL3.framework/SDL3" "MoltenVK.xcframework/ios-arm64/MoltenVK.framework/MoltenVK"; do
  if [ ! -f "$dest/$f" ]; then
    echo "Missing the device slice $f; the slices are:" >&2
    ls "$dest/${f%%/*}" >&2
    exit 1
  fi
done
echo "Frameworks ready in $dest"
