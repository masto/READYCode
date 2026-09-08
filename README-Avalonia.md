# READYCode on macOS and Linux

READYCode's original front end is built on WPF, which runs only on Windows. **ReadyCode.Avalonia**
is a second front end, built on [Avalonia](https://avaloniaui.net/), that runs on macOS and Linux.
It shares every non-UI component - tokenizer, assembler, disassembler, diagnostics, minify and
prettify, the VICE and C64 Ultimate clients, settings - with the Windows app through the
**ReadyCode.Core** library, so both front ends produce byte-identical `.prg` output.

This is a work in progress and is not a full replacement for the Windows app yet. See
[What works](#what-works) below for the current state.

> **Windows users:** nothing here applies to you. Keep using the WPF app; see the
> [main README](README.md). The Avalonia project does not replace it.

## Prerequisites

- **[.NET SDK](https://dotnet.microsoft.com/download) 9.0 or newer.** (The app itself targets
  `net8.0`, but building it needs a newer SDK than that.)
  - macOS: `brew install dotnet`
  - Linux: your distribution's `dotnet-sdk` package, or Microsoft's install script.
- **[VICE](https://vice-emu.sourceforge.io/)**, if you want to run your programs. READYCode drives
  the `x64sc` emulator through its binary monitor.
  - macOS: `brew install vice`
  - Linux: your distribution's `vice` package, or the snap.

  On first run READYCode looks for `x64sc` in the usual locations (`/opt/homebrew/bin`,
  `/usr/local/bin`, `/usr/bin`, `/snap/bin`). If it isn't found, set the path in
  Preferences → VICE.

Neither VICE nor a C64 Ultimate is required to run the app - only to actually load and run
programs. A C64 Ultimate needs no extra setup beyond its own network configuration: set its
REST API's base URL in Preferences → C64U (e.g. `http://192.168.1.50/`), then connect from the
C64U tab of the left panel. The FTP explorer connects to the same host on port 21 using the
device's default `admin` account with a blank password, matching the Windows app.

## Build and run

```bash
git clone -b macos-port https://github.com/masto/READYCode/
cd READYCode
dotnet run --project ReadyCode.Avalonia
```

To build both front ends and both test projects at once, use the solution filter rather than the
full solution:

```bash
dotnet build ReadyCode.slnf -p:EnableWindowsTargeting=true
```

`ReadyCode.slnf` leaves out `ReadyCode.Packaging` (an MSIX `.wapproj`) and `ReadyCode.Installer` (a
WiX project), neither of which builds outside Windows. `EnableWindowsTargeting` is what lets the
WPF project itself compile here - useful for checking changes to shared code before they reach a
Windows machine. It compiles but cannot run: WPF has no non-Windows implementation.

To build only the projects that actually run on this machine, name them directly:

```bash
dotnet build ReadyCode.Avalonia
```

## A double-clickable macOS app

```bash
scripts/make-app-bundle.sh            # dev build   -> ReadyCode.Avalonia/bin/READYCode.app
scripts/make-app-bundle.sh --publish  # self-contained release
                                      #             -> ReadyCode.Avalonia/bin/publish/READYCode.app
```

The dev bundle depends on the .NET SDK that built it: its launcher script sets `DOTNET_ROOT` to
whatever SDK `dotnet --list-sdks` reports, so Finder can start it with an otherwise empty
environment. The `--publish` bundle is a self-contained single-file build that carries its own
runtime and has no such dependency.

Neither bundle is code-signed, so the first launch needs the usual right-click → Open, or an allow
in System Settings → Privacy & Security. To produce something other people can open normally, see
[Releasing a signed macOS build](#releasing-a-signed-macos-build).

On Linux, publish a self-contained build the ordinary way:

```bash
dotnet publish ReadyCode.Avalonia -c Release -r linux-x64 --self-contained true -o out
./out/ReadyCode.Avalonia
```

## Releasing a signed macOS build

A downloaded app that is not signed and notarized is blocked by Gatekeeper with a message that
sounds like malware ("Apple could not verify..."), so a public binary needs both. This needs a
paid Apple Developer account.

### One-time setup

1. **Get a Developer ID Application certificate.** In Xcode, Settings → Accounts → your Apple ID →
   Manage Certificates → **+** → *Developer ID Application*. Confirm it landed:

   ```bash
   security find-identity -v -p codesigning | grep "Developer ID Application"
   ```

   An *Apple Development* certificate is not a substitute: it is for local testing, and
   notarization rejects builds signed with it.

2. **Create an app-specific password** at [appleid.apple.com](https://appleid.apple.com) →
   Sign-In and Security → App-Specific Passwords. This is not your Apple ID password.

3. **Store the notary credentials in your keychain**, so the script never handles them:

   ```bash
   xcrun notarytool store-credentials READYCode-notary \
     --apple-id you@example.com --team-id ABCDE12345 --password <app-specific-password>
   ```

   Your team ID is on the Apple Developer Membership page.

### Each release

```bash
scripts/make-app-bundle.sh --publish              # Apple Silicon
scripts/sign-and-notarize.sh ReadyCode.Avalonia/bin/publish/osx-arm64/READYCode.app
```

That signs the bundle with the hardened runtime, uploads it to Apple, waits for the result,
staples the ticket into the app, and leaves a `READYCode.zip` beside it. Notarization usually
takes a few minutes. Repeat with `osx-x64` for a build that runs on Intel Macs:

```bash
scripts/make-app-bundle.sh --publish osx-x64
scripts/sign-and-notarize.sh ReadyCode.Avalonia/bin/publish/osx-x64/READYCode.app
```

Apple Silicon Macs can run the Intel build under Rosetta, but not the reverse, so shipping both is
worthwhile if you expect Intel users.

Then create the release and attach the zips:

```bash
gh release create v2.3.0-avalonia \
  --title "READYCode 2.3.0 for macOS" \
  --notes-file RELEASE-NOTES.md \
  ReadyCode.Avalonia/bin/publish/osx-arm64/READYCode.zip#READYCode-2.3.0-macos-arm64.zip \
  ReadyCode.Avalonia/bin/publish/osx-x64/READYCode.zip#READYCode-2.3.0-macos-x64.zip
```

Use a tag that will not collide with the Windows app's own `v*` tags, since those trigger the
Windows release workflow.

To check a build before shipping it, from a different account or Mac:

```bash
spctl --assess --type execute --verbose=4 /path/to/READYCode.app   # expect "accepted"
xcrun stapler validate /path/to/READYCode.app
```

### Why the published bundle is single-file

`codesign` treats every `.dll` inside `Contents/MacOS` as nested code that must be signed before
the enclosing bundle can be sealed, but it will not sign the `.deps.json` that .NET requires
alongside them. An ordinary self-contained publish therefore cannot produce a sealed bundle at
all. Publishing single-file leaves exactly one executable in `Contents/MacOS`, which signs and
notarizes cleanly.

The hardened runtime that notarization requires would also block .NET's JIT, so
`scripts/READYCode.entitlements` re-enables what the runtime needs, plus Apple Events for the
"bring VICE to the foreground" feature. That entitlement pairs with the
`NSAppleEventsUsageDescription` string in `scripts/Info.plist`; macOS still asks the user to
approve the automation the first time.

## What works

- **Editing** - multi-tab editing of `.bas`, `.prg` (detokenized on open, re-tokenized on save),
  and `.asm`/`.s`. BASIC and 6502 syntax highlighting, PETSCII rendering through the embedded
  Pet Me 64 font, code folding, C64-style upper-case typing with the keyboard keyword
  abbreviations, zero-padded line numbers, auto-numbering, assembly auto-indent, and a column guide.
- **Folder explorer** - lazy folder tree with file-type badges. `.d64` and `.d81` images expand in
  place, and the programs inside them open and save back into the image. New file and folder,
  rename, delete, Reveal in Finder, Copy Path, and Run or Load on VICE or the C64 Ultimate
  straight from the tree.
- **Find and replace** - `Cmd`/`Ctrl+F`, with match case, whole word, and regex.
- **Diagnostics** - live squiggles for invalid `GOTO`/`GOSUB` targets, unmatched `FOR`/`NEXT`,
  unterminated strings, duplicate line numbers, and assembly errors, with hover messages and a
  Problems panel that jumps to the issue.
- **VICE** - Run, Transfer, Reset, Reboot, Pause, Resume, and Power Off over VICE's binary monitor.
  The emulator is launched automatically if it isn't already running.
- **C64 Ultimate** - Run, Transfer, Reset, Reboot, Pause, Resume, Power Off, and an About My C64
  Ultimate box over its REST API, plus a C64U explorer tab in the left panel: browse the device's
  storage over FTP, `.d64`/`.d81` images expand in place with programs opening and saving back
  into them, new folder and new blank disk image, upload and download, add a local file to a disk
  image, rename, delete, and mount/eject drives A and B. Every runnable item in either explorer
  tree, and the editor itself, offers Run/Load on both VICE and the C64 Ultimate side by side.
- **BASIC debugger on VICE and the C64 Ultimate** - breakpoints in the gutter or with `F9`,
  Start/Continue, Pause, Step Into, Run to Cursor, a highlighted current line, and Variables
  (editable) panel. Step Over/Out and the Call Stack panel are VICE only - the C64 Ultimate's
  REST API has no way to read the 6502 stack pointer they depend on. Breakpoints persist per
  open folder.
- **Preferences** - Application, Text Editor, BASIC, Assembly, VICE, and C64U pages.
- **Native menus** - the menu is declared once and rendered by whatever the platform uses: the
  system menu bar on macOS, where About and Preferences also move into the application menu and
  Quit comes from the system, and an in-window menu bar on Windows and Linux.

Settings live in the same `settings.json` format the Windows app uses:

| Platform | Location |
|---|---|
| macOS | `~/Library/Application Support/READYCode/` |
| Linux | `~/.config/READYCode/` |
| Windows | `%APPDATA%\READYCode\` |

Setting `READYCODE_SETTINGS_DIR` overrides that location. The UI tests use it so they never touch
your real settings.

## Not ported yet

Project-wide search, the hex editor, file compare, the disassembler tabs, the reference panels
(BASIC keywords, PETSCII, Quick Keys, Music Notes), the Variables and Symbols side panel,
ghost-text completion and `Ctrl+Space`, the Minify, Prettify and Renumber dialogs, printing,
recent files, drag-and-drop and cut/copy/paste in either explorer tree, code statistics, and the
Light/Dark/C64 themes (the Avalonia app currently uses Avalonia's own light theme).

The C64U explorer's coverage of the REST API and FTP service is not yet as complete as the
Windows app's: drag-and-drop (both within the tree and dragging files in from the OS) and live
drive-mount highlighting in the tree are still WPF-only. New/rename use a modal text prompt
rather than WPF's inline tree editing, matching how the local Explorer already works here.

Bringing VICE to the foreground after a transfer works on Windows and macOS. On Linux it is a
no-op: there is no portable way to raise another application's window across the various window
managers.

## Testing

```bash
dotnet test ReadyCode.Tests/ReadyCode.Tests.csproj            # shared core logic, any OS
dotnet test ReadyCode.Avalonia.Tests/ReadyCode.Avalonia.Tests.csproj   # Avalonia UI
```

The UI tests run on Avalonia's headless platform with Skia, so they render real frames without a
display. Set `READYCODE_RENDER_DIR` to a directory to have them write those frames as PNGs, which
is how font, highlighting, and layout changes get reviewed without a screen:

```bash
READYCODE_RENDER_DIR=/tmp/readycode-renders dotnet test ReadyCode.Avalonia.Tests/ReadyCode.Avalonia.Tests.csproj
```

## Project layout

| Project | Target | Purpose |
|---|---|---|
| `ReadyCode.Core` | `net8.0` | Cross-platform logic shared by both front ends |
| `ReadyCode` | `net8.0-windows` | The original WPF front end, unchanged in behavior |
| `ReadyCode.Avalonia` | `net8.0` | The cross-platform front end |
| `ReadyCode.Tests` | `net8.0` | Core unit tests, runnable on any OS |
| `ReadyCode.Avalonia.Tests` | `net8.0` | Headless Avalonia UI tests |

`ReadyCode.Core` deliberately references no UI framework. Where the shared models need to reach the
UI thread, they expose a hook each front end installs at startup: `FileTreeItem.DeferToUiThread`
and `MainViewModel.RunOnUiThread`.

## Platform support

Developed and tested on macOS 15 (Apple Silicon) against VICE 3.10. The Linux build is verified in
CI, and has additionally been built, tested, and run headless (via Xvfb) on Ubuntu 24.04, but has
not yet been exercised on a Linux desktop with a display. Reports welcome.

The C64 Ultimate integration was ported from the Windows app's without access to real hardware to
test against - the REST API and FTP client code is unchanged from what the Windows app has used in
production, but the Avalonia-side UI wiring (the C64U explorer tab, the C64U menu, the debugger's
C64U target) has only been exercised against the app's own logic and headless UI tests, not a
physical device. Reports from C64 Ultimate owners are especially welcome.

## Contributing

The coding conventions in [CONTRIBUTING.md](CONTRIBUTING.md) apply to the Avalonia projects too.
Two Avalonia-specific notes:

- The hybrid-MVVM split is the same as the WPF app's: bindable state lives in `MainViewModel`,
  and anything touching the AvaloniaEdit control directly lives in `MainWindow.axaml.cs`.
- The menu is a single `NativeMenu` in `MainWindow.axaml`. Add items there rather than to a
  platform-specific menu, and give anything with a shortcut a `Gesture`: on macOS that becomes a
  real system key equivalent, and where the menu is drawn in-window the window binds the same
  gestures itself, so the two can never disagree.
- Avalonia is not WPF. It uses `.axaml`, `StyledProperty` rather than `DependencyProperty`,
  style selectors and pseudo-classes rather than triggers, `TreeDataTemplate` rather than
  `HierarchicalDataTemplate`, `avares://` rather than `pack://`, and `IsVisible` rather than
  `Visibility`.
