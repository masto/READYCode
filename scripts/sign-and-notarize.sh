#!/usr/bin/env bash
# Code-signs, notarizes, and staples a READYCode.app built by make-app-bundle.sh --publish,
# producing a .zip that opens on any Mac without a Gatekeeper warning.
#
#   scripts/sign-and-notarize.sh <path-to-READYCode.app> [--sign-only]
#
# Requires, once:
#   1. A "Developer ID Application" certificate in your login keychain:
#      Xcode > Settings > Accounts > Manage Certificates > + > Developer ID Application.
#      (An "Apple Development" certificate will not do - that one is for local testing, and
#      notarization rejects it.)
#   2. Notary credentials stored in the keychain under the profile name below:
#        xcrun notarytool store-credentials READYCode-notary \
#          --apple-id you@example.com --team-id ABCDE12345 --password <app-specific-password>
#      The app-specific password comes from appleid.apple.com > Sign-In and Security, not your
#      Apple ID password. The team ID is on the Apple Developer Membership page.
#
# Environment overrides:
#   SIGN_IDENTITY   identity name or its 40-character hash (default: the only Developer ID
#                   Application certificate in the keychain)
#   NOTARY_PROFILE  notarytool keychain profile name (default: READYCode-notary)
#
# Written for bash 3.2, which is what macOS ships.
set -euo pipefail

APP="${1:-}"
SIGN_ONLY="${2:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENTITLEMENTS="$ROOT/scripts/READYCode.entitlements"
NOTARY_PROFILE="${NOTARY_PROFILE:-READYCode-notary}"

if [[ -z "$APP" || ! -d "$APP" ]]; then
    echo "usage: $0 <path-to-READYCode.app> [--sign-only]" >&2
    exit 2
fi

# A framework-dependent dev bundle has a shell script as its executable and depends on the SDK
# that built it - signing that would produce something that only runs on this machine.
if [[ ! -f "$APP/Contents/MacOS/ReadyCode.Avalonia" ]]; then
    echo "$APP does not look like a --publish bundle." >&2
    echo "Build one with: scripts/make-app-bundle.sh --publish" >&2
    exit 1
fi

# Resolve the signing identity. Ambiguity is an error rather than a guess: signing with the wrong
# certificate produces a bundle that notarization rejects much later, for a confusing reason.
if [[ -z "${SIGN_IDENTITY:-}" ]]; then
    FOUND="$(security find-identity -v -p codesigning | grep 'Developer ID Application' || true)"
    COUNT="$(printf '%s' "$FOUND" | grep -c . || true)"
    if [[ "$COUNT" -eq 0 ]]; then
        echo "No 'Developer ID Application' certificate found in the keychain." >&2
        echo "Create one in Xcode > Settings > Accounts > Manage Certificates, then re-run." >&2
        exit 1
    elif [[ "$COUNT" -gt 1 ]]; then
        echo "More than one Developer ID Application certificate found; set SIGN_IDENTITY to pick one:" >&2
        echo "$FOUND" >&2
        exit 1
    fi
    SIGN_IDENTITY="$(echo "$FOUND" | sed -E 's/.*\) ([0-9A-F]{40}) ".*/\1/')"
fi
echo "Signing identity: $SIGN_IDENTITY"

# codesign refuses to sign a bundle carrying the extended attributes Finder and downloads add.
xattr -cr "$APP"

# The published bundle is single-file, so the only nested code is that one executable, which
# signing the bundle covers. --options runtime turns on the hardened runtime, which notarization
# requires; the entitlements are what let .NET's JIT still work under it.
echo "Signing..."
codesign --force --timestamp --options runtime \
         --entitlements "$ENTITLEMENTS" \
         --sign "$SIGN_IDENTITY" "$APP"

echo "Verifying signature..."
codesign --verify --deep --strict --verbose=2 "$APP"

if [[ "$SIGN_ONLY" == "--sign-only" ]]; then
    echo "Signed (notarization skipped)."
    exit 0
fi

# ditto -c -k --keepParent is the archive format notarytool expects for a .app; plain `zip` loses
# the symlinks and permission bits inside the bundle.
ZIP="${APP%.app}.zip"
echo "Creating $ZIP..."
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "Submitting to Apple (this usually takes a few minutes)..."
xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait

# Staple the ticket into the .app so it validates without a network round trip, then rebuild the
# zip: the one just uploaded contains the un-stapled bundle.
echo "Stapling..."
xcrun stapler staple "$APP"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

echo "Verifying Gatekeeper acceptance..."
spctl --assess --type execute --verbose=4 "$APP"
xcrun stapler validate "$APP"

echo
echo "Done: $ZIP"
