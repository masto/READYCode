#!/usr/bin/env bash
# Wraps the ReadyCode.Avalonia build output into a macOS .app bundle.
#
#   scripts/make-app-bundle.sh                     # Debug build
#                                                  #   -> ReadyCode.Avalonia/bin/READYCode.app
#   scripts/make-app-bundle.sh --publish           # self-contained Release for this Mac's CPU
#                                                  #   -> ReadyCode.Avalonia/bin/publish/READYCode.app
#   scripts/make-app-bundle.sh --publish osx-x64   # ...for Intel Macs instead
#
# The bundle is unsigned. Gatekeeper accepts it when launched locally on the machine that built
# it; to hand it to anyone else, run scripts/sign-and-notarize.sh on the --publish output.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/ReadyCode.Avalonia/ReadyCode.Avalonia.csproj"

if [[ "${1:-}" == "--publish" ]]; then
    # Default to this Mac's own CPU; pass a runtime identifier to cross-build (e.g. osx-x64).
    RID="${2:-}"
    if [[ -z "$RID" ]]; then
        RID="osx-arm64"; [[ "$(uname -m)" == "x86_64" ]] && RID="osx-x64"
    fi
    OUT="$ROOT/ReadyCode.Avalonia/bin/publish/$RID"
    PAYLOAD="$OUT/payload"
    rm -rf "$OUT"
    # Single-file, so Contents/MacOS holds exactly one executable. That is not just tidiness:
    # codesign treats every .dll in Contents/MacOS as nested code that must be signed before the
    # bundle can be sealed, but it will not sign the .deps.json sitting beside them, so the
    # ordinary layout cannot produce a sealed bundle at all. One binary makes signing trivial.
    # Debug symbols are dropped: they are 100 MB of no use to someone running a release.
    dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none -p:DebugSymbols=false \
        -o "$PAYLOAD" -nologo -v q
else
    OUT="$ROOT/ReadyCode.Avalonia/bin"
    PAYLOAD="$OUT/Debug/net8.0"
    dotnet build "$PROJ" -c Debug -nologo -v q
fi

APP="$OUT/READYCode.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/scripts/Info.plist" "$APP/Contents/Info.plist"
# The csproj's <Version> (e.g. 2.3.0.1: upstream base + Avalonia build counter) is the single
# source of truth. Info.plist's two version keys cap at three dotted components apiece, so it
# doesn't fit either field whole - split it: the first three components are the marketing
# version, the fourth is the build number, which is exactly what CFBundleVersion is for.
VERSION_FULL="$(dotnet msbuild "$PROJ" -getProperty:Version -nologo -v:q)"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${VERSION_FULL%.*}" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${VERSION_FULL##*.}" "$APP/Contents/Info.plist"
# rsync keeps the apphost's executable bit and lets repeated dev builds stay fast.
rsync -a --delete "$PAYLOAD/" "$APP/Contents/MacOS/"
[[ -f "$ROOT/scripts/READYCode.icns" ]] && cp "$ROOT/scripts/READYCode.icns" "$APP/Contents/Resources/"

# The Pet Me 64 font is redistributable free of charge provided its license travels with it
# verbatim and Kreative Software is credited. It is embedded in the assembly for rendering; this
# copy is what makes it readable to someone who only has the .app.
cp "$ROOT/LICENSE" "$APP/Contents/Resources/LICENSE.txt"
cp "$ROOT/ReadyCode.Avalonia/Assets/Fonts/LICENSE-PetMe64.txt" "$APP/Contents/Resources/"

if [[ "${1:-}" != "--publish" ]]; then
    # A framework-dependent build needs the shared runtime. Finder launches with an empty
    # environment (and LaunchServices does not reliably apply LSEnvironment to a rebuilt
    # bundle), so the bundle's executable is a launcher that sets DOTNET_ROOT and execs the
    # apphost - the exec keeps the pid and the executable path inside the bundle, so the app is
    # still recognized as READYCode.app.
    # "10.0.400 [/opt/homebrew/Cellar/dotnet/10.0.400/libexec/sdk]" -> the libexec root.
    DOTNET_ROOT_DIR="$(dirname "$(dotnet --list-sdks | tail -1 | sed -E 's/^[^[]*\[(.*)\]$/\1/')")"
    cat > "$APP/Contents/MacOS/READYCode" <<LAUNCHER
#!/bin/sh
export DOTNET_ROOT="$DOTNET_ROOT_DIR"
exec "\$(dirname "\$0")/ReadyCode.Avalonia" "\$@"
LAUNCHER
    chmod +x "$APP/Contents/MacOS/READYCode"
    /usr/libexec/PlistBuddy -c "Set :CFBundleExecutable READYCode" "$APP/Contents/Info.plist"
    echo "DOTNET_ROOT=$DOTNET_ROOT_DIR"
fi
echo "Built $APP"
