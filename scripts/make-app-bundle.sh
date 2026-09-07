#!/usr/bin/env bash
# Wraps the ReadyCode.Avalonia build output into a macOS .app bundle.
#
#   scripts/make-app-bundle.sh            # Debug build -> ReadyCode.Avalonia/bin/READYCode.app
#   scripts/make-app-bundle.sh --publish  # self-contained Release publish for this Mac's CPU
#                                         #   -> ReadyCode.Avalonia/bin/publish/READYCode.app
#
# The bundle is unsigned; Gatekeeper is happy with it when launched locally via `open` or
# Finder on the machine that built it. Distribution builds will need codesign + notarization.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/ReadyCode.Avalonia/ReadyCode.Avalonia.csproj"

if [[ "${1:-}" == "--publish" ]]; then
    ARCH="$(uname -m)"; RID="osx-arm64"; [[ "$ARCH" == "x86_64" ]] && RID="osx-x64"
    OUT="$ROOT/ReadyCode.Avalonia/bin/publish"
    PAYLOAD="$OUT/payload"
    rm -rf "$OUT"
    dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true -o "$PAYLOAD" -nologo -v q
else
    OUT="$ROOT/ReadyCode.Avalonia/bin"
    PAYLOAD="$OUT/Debug/net8.0"
    dotnet build "$PROJ" -c Debug -nologo -v q
fi

APP="$OUT/READYCode.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/scripts/Info.plist" "$APP/Contents/Info.plist"
# rsync keeps the apphost's executable bit and lets repeated dev builds stay fast.
rsync -a --delete "$PAYLOAD/" "$APP/Contents/MacOS/"
[[ -f "$ROOT/scripts/READYCode.icns" ]] && cp "$ROOT/scripts/READYCode.icns" "$APP/Contents/Resources/"

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
