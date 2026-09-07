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

- **[.NET SDK](https://dotnet.microsoft.com/download) 8.0 or newer.** Newer SDKs are fine: the
  cross-platform projects target `net8.0` with `RollForward=Major`, so they run on whatever
  runtime you have.
  - macOS: `brew install dotnet`
  - Linux: your distribution's `dotnet-sdk` package, or Microsoft's install script.
- **[VICE](https://vice-emu.sourceforge.io/)**, if you want to run your programs. READYCode drives
  the `x64sc` emulator through its binary monitor.
  - macOS: `brew install vice`
  - Linux: your distribution's `vice` package, or the snap.

  On first run READYCode looks for `x64sc` in the usual locations (`/opt/homebrew/bin`,
  `/usr/local/bin`, `/usr/bin`, `/snap/bin`). If it isn't found, set the path in
  Preferences → VICE.

A C64 Ultimate is not required. The Avalonia front end currently targets VICE only.

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
  rename, delete, Reveal in Finder, Copy Path, and Run or Load on VICE straight from the tree.
- **Find and replace** - `Cmd`/`Ctrl+F`, with match case, whole word, and regex.
- **Diagnostics** - live squiggles for invalid `GOTO`/`GOSUB` targets, unmatched `FOR`/`NEXT`,
  unterminated strings, duplicate line numbers, and assembly errors, with hover messages and a
  Problems panel that jumps to the issue.
- **VICE** - Run, Transfer, Reset, Reboot, Pause, Resume, and Power Off over VICE's binary monitor.
  The emulator is launched automatically if it isn't already running.
- **BASIC debugger on VICE** - breakpoints in the gutter or with `F9`, Start/Continue, Pause,
  Step Over/Into/Out, Run to Cursor, a highlighted current line, and Variables (editable),
  Breakpoints, and Call Stack panels. Breakpoints persist per open folder.
- **Preferences** - Application, Text Editor, BASIC, Assembly, and VICE pages.

Settings live in the same `settings.json` format the Windows app uses:

| Platform | Location |
|---|---|
| macOS | `~/Library/Application Support/READYCode/` |
| Linux | `~/.config/READYCode/` |
| Windows | `%APPDATA%\READYCode\` |

Setting `READYCODE_SETTINGS_DIR` overrides that location. The UI tests use it so they never touch
your real settings.

## Not ported yet

The C64 Ultimate menu and FTP explorer, project-wide search, the hex editor, file compare, the
disassembler tabs, the reference panels (BASIC keywords, PETSCII, Quick Keys, Music Notes), the
Variables and Symbols side panel, ghost-text completion and `Ctrl+Space`, the Minify, Prettify and
Renumber dialogs, printing, recent files, drag-and-drop and cut/copy/paste in the explorer, code
statistics, the About dialog, and the Light/Dark/C64 themes (the Avalonia app currently uses
Avalonia's own light theme).

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
CI - it compiles and publishes a working self-contained binary - but has not been exercised on a
Linux desktop. Reports welcome.

## Contributing

The coding conventions in [CONTRIBUTING.md](CONTRIBUTING.md) apply to the Avalonia projects too.
Two Avalonia-specific notes:

- The hybrid-MVVM split is the same as the WPF app's: bindable state lives in `MainViewModel`,
  and anything touching the AvaloniaEdit control directly lives in `MainWindow.axaml.cs`.
- Avalonia is not WPF. It uses `.axaml`, `StyledProperty` rather than `DependencyProperty`,
  style selectors and pseudo-classes rather than triggers, `TreeDataTemplate` rather than
  `HierarchicalDataTemplate`, `avares://` rather than `pack://`, and `IsVisible` rather than
  `Visibility`.
