# READYCode on macOS (and Linux)

READYCode's original front end is WPF, which only runs on Windows. The `ReadyCode.Avalonia`
project is a cross-platform front end built on [Avalonia](https://avaloniaui.net/) that shares
every non-UI component - tokenizer, assembler/disassembler, diagnostics, minify/prettify, the VICE
and C64 Ultimate clients, settings - with the WPF app through the `ReadyCode.Core` library.

It is a work in progress. What works today:

- Multi-tab editing of `.bas`, `.prg` (detokenized on open, re-tokenized on save), `.asm`/`.s`
- BASIC syntax highlighting, PETSCII rendering with the embedded Pet Me 64 font, code folding
- VICE: Run, Transfer (load only), Reset, Reboot, Pause, Resume, Power Off, over VICE's binary
  monitor - the emulator is launched automatically if it isn't already running
- Preferences for the VICE path/monitor and basic editor options, stored in the same
  `settings.json` format as the Windows app (`~/.config/READYCode/` on macOS/Linux)

Not yet ported: the folder/C64U explorers, find/replace, diagnostics squiggles, the hex editor,
file compare, the debugger UI, reference panels, printing, and the C64U menu.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer (`brew install dotnet`)
- [VICE](https://vice-emu.sourceforge.io/) (`brew install vice`) - READYCode looks for `x64sc`
  in the usual Homebrew locations on first run; set the path in Preferences otherwise.

## Build and run

```bash
dotnet run --project ReadyCode.Avalonia
```

To get a double-clickable app bundle:

```bash
scripts/make-app-bundle.sh            # dev build -> ReadyCode.Avalonia/bin/READYCode.app
scripts/make-app-bundle.sh --publish  # self-contained release -> ReadyCode.Avalonia/bin/publish/READYCode.app
```

The dev bundle depends on the .NET SDK that built it (its launcher sets `DOTNET_ROOT`); the
`--publish` bundle carries its own runtime. Neither is code-signed or notarized.

## Tests

```bash
dotnet test ReadyCode.Tests                 # core logic (shared with the WPF app)
dotnet test ReadyCode.Avalonia.Tests        # headless UI rendering smoke tests
```

Set `READYCODE_RENDER_DIR=/some/dir` before the UI tests to have them write the frames they
capture as PNGs, which is handy for checking font and highlighting changes without a screen.

## Project layout

| Project | Purpose |
|---|---|
| `ReadyCode.Core` | Cross-platform logic shared by both front ends (net8.0) |
| `ReadyCode` | The original WPF front end (net8.0-windows); unchanged in behaviour |
| `ReadyCode.Avalonia` | The cross-platform front end |
| `ReadyCode.Tests` | Core unit tests, runnable on any OS |
| `ReadyCode.Avalonia.Tests` | Headless Avalonia UI tests |

The WPF project still compiles on macOS/Linux for checking changes to shared code:

```bash
dotnet build ReadyCode/ReadyCode.csproj -p:EnableWindowsTargeting=true
```
