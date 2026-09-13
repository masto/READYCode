# Avalonia UI parity checklist

What's left before the Avalonia UI can retire the WPF front end, from a full pass comparing
`ReadyCode.Avalonia` against `ReadyCode`'s menus, `Views/`, `ViewModels/`, `Editor/`, and
`Settings` structure. This is the canonical list: when a gap closes, tick it here in the same
commit. Grouped by size, not by menu, so it's easier to slice into standalone chunks of work.
Each item notes whether the underlying logic already lives in `ReadyCode.Core` (shared, so this
is UI-only work) or would be new.

Nothing here blocks a release by itself - this is the full gap list for planning, not a gate.

**Agreed order** (2026-09-13): polish items first, then themes, then the reference-panel
sidebar; printing last (high effort, low value). Keyboard shortcuts stay aligned with WPF as
things are added - see `MenuTests.cs`.

## Legend

- **Core logic**: already in `ReadyCode.Core`, shared with WPF - porting is UI work only.
- **New logic**: doesn't exist yet outside the WPF project; needs extracting/writing, ideally
  into `ReadyCode.Core` so both front ends get it (per `CONTRIBUTING.md`'s existing convention).

---

## Large features (their own multi-step effort each)

### Hex Editor
- [ ] Open any file (not just recognized BASIC/asm/disk-image types) as a raw offset/hex/ASCII grid
- [ ] Inline byte editing, its own undo/redo history
- [ ] Works on files inside a mounted `.d64`/`.d81` image, not just on-disk files
- **New logic**: `HexEditorControl.xaml`/`HexGridCanvas.cs` are WPF-only; `HexUndoStack` model is
  already in `ReadyCode.Core/Models/`, so the undo/redo *data structure* is shared - the grid
  rendering and byte-editing UI is not.

### File Compare
- [ ] Side-by-side or unified diff view for two comparable files (`.bas`, `.asm`/`.s`, `.prg` -
  detokenized/disassembled the same as opening it directly would)
- [ ] "Ignore Whitespace" toggle, whole-document change-location strip for navigation
- [ ] Any two comparable kinds against each other (upstream v2.4.0 lifted the same-kind
  requirement - see this session's rebase)
- [ ] Invoked via "Select file for comparison" in either Explorer's context menu
- **Core logic**: `ReadyCode.Core/Diff/FileCompareEngine.cs`, `CompareFileResolver.cs`,
  `FileCompareResult.cs` are all shared already. This is entirely the `FileCompareControl` UI
  (diff panes, change-location strip, the colorizers/margins that render it -
  `DiffChangeIndicatorStrip`, `DiffColors`, `DiffLineColorizer`, `DiffPrefixMargin`,
  `DiffViewportThumb`, all WPF-only) plus the Explorer context-menu wiring.

### Disassembler
- [ ] **Disassemble at...** - reads live memory from a running C64 Ultimate or VICE instance
  starting at a given address, opens a read-only annotated-assembly tab
- [ ] **Disassemble file** - same for a machine-language `.prg`/`.ml` file from either Explorer
  tree, auto-detecting and skipping a BASIC loader stub
- [ ] Disassembly toolbar (address field, re-disassemble, etc.)
- **Core logic**: `ReadyCode.Core/Assembler/Asm6502Disassembler.cs` and
  `PrgFileDisassembler.cs` are shared. Missing: the VICE/C64U memory-read wiring for "at a live
  address" (the emulator clients themselves are shared, so this is mostly UI + a bit of
  glue), the read-only disassembly tab mode, and `DisassemblyToolbarControl`.

### Project-wide Find/Replace in Files
- [ ] Search and replace across `.bas`, `.asm`, `.s`, `.txt`, `.prg` files in the open folder
- [ ] Match case, whole word, regex options; results tree grouped by file
- **Core logic**: `ReadyCode.Core/Search/ProjectSearcher.cs` and `ProjectSearchResultInfo.cs` are
  shared. Missing: the results-tree UI and the Find in Files / Replace in Files dialogs.

### Minify / Prettify / Renumber - DONE
- [x] Minify dialog (with "bytes saved" reporting) and Prettify dialog, remembering their
  selections in the same settings keys as WPF
- [x] Renumber (with WPF's dangling-reference warning)
- [x] Assembly's "Format Code", shown in place of the three above on an assembly tab, as in WPF
- Shortcuts: `Ctrl+R` / `Cmd+R` for Renumber; Minify and Prettify stay on the Control key on
  macOS (`Cmd+M` is Minimize there). Format Code has none - WPF's is a `Ctrl+K` chord.

### Reference panels (the WPF app's whole right-hand "secondary side bar") - DONE
- [x] Quick Keys panel, with the `Ctrl+1–8` / `Ctrl+Shift+1–8` / `Ctrl+Shift+Alt+1–8` /
  `Shift+F1–F8` shortcuts wired (they weren't before). One deliberate difference: `Shift+F5`
  is bound here - free in this app, since Stop Debugging is `Alt+Shift+F5` - where WPF's C64U
  debug scheme claims it.
- [x] PETSCII Reference panel (click-to-insert)
- [x] BASIC Keywords panel, [x] ASM Mnemonics panel (each offered only for its language, as in WPF)
- [x] Music Notes panel
- The PETSCII control-code labels and the Quick Keys card/shortcut table now live in
  `ReadyCode.Core/Tokenizer/PetsciiReference.cs`; the WPF reference table reads from it too. The
  WPF Quick Keys cards are still hand-written XAML - generating them from the same table is a
  small follow-up for the eventual unification.

### Static Variables / Symbols panel
- [ ] Lists every variable in a BASIC program, or every label/constant in an assembly program
- [ ] Click-to-jump navigation, one-step rename (F2)
- [ ] Always available (not tied to a debug session) - **distinct from** the Debug panel's live
  Variables view, which Avalonia already has
- **Core logic**: `ReadyCode.Core/Diagnostics/VariableCrossReference.cs` and
  `AsmSymbolIndex.cs` already compute this. Missing: the always-on side panel UI and the F2
  rename flow.

### Ghost-text completion + Ctrl+Space
- [ ] Inline "ghost text" keyword-completion suggestion as you type
- [ ] `Ctrl+Space` to force-open the completion popup
- **Core logic**: `ReadyCode.Core/Editor/BasicCompletionProvider.cs`,
  `AsmCompletionProvider.cs`, and `KeywordCompletionItem` already exist and are shared. Missing:
  `GhostTextRenderer`-equivalent (AvaloniaEdit adorner) and the completion popup wiring
  (`KeywordCompletionData` is WPF's AvalonEdit-specific adapter over the shared provider - would
  need an AvaloniaEdit equivalent).

### Printing
- [ ] Page Setup, Print Preview, Print
- **New logic**: `ReadyCode/Printing/SourcePrinter.cs` is WPF-only (uses WPF's `System.Printing`
  APIs directly, no toolkit-neutral seam exists yet). Avalonia has no printing API of its own
  either - this would need real research into what's available cross-platform, so it's probably
  the most open-ended item on this whole list. Worth scoping separately before estimating it.

### Explorer file operations: clipboard and drag-and-drop
Originally filed under "polish" below; on inspection it is a real feature, not a small one.
- [ ] **Cut / Copy / Paste** in the local Explorer's context menus. In WPF this is OS
  file-clipboard interop - copy in Windows Explorer, paste into READYCode's tree, and back -
  with Cut carried as the Windows-only `Preferred DropEffect` clipboard format. Avalonia 12's
  clipboard can read and write file lists cross-platform (`DataFormat.File`), but Cut has no
  portable flag, so it would be an in-app "pending cut" marker instead.
- [ ] **Drag-and-drop, local Explorer**: drag an item onto a folder to move it; onto a
  `.d64`/`.d81` to embed it (assembling/tokenizing on the way); drag files in from the OS to
  copy, embed, or open as tabs depending on the drop target.
- [ ] **Drag-and-drop, C64U Explorer**: the same within the device's tree, plus dragging OS
  files onto it to upload.
- [ ] **Live drive-mount highlighting** in the C64U tree (the mounted image's row is marked;
  `C64UMountedPathConverter` in WPF). The mount status footer itself already exists.
- **New logic** for the UI side throughout; the file/disk-image operations they call into
  (`DiskImage`, `C64UFtpClient`, `FileTreeItem`) are shared already.

### Themes (Light / Dark / Commodore 64) - DONE
- [x] Theme picker in Preferences, three themes applied across the app. Reads the WPF theme
  files directly (linked as embedded resources), so there is no second copy of the colors.

---

## Medium items (mostly UI wiring against data that's already shared)

- [ ] **Recent Files** - `File > Open Recent` submenu. `Settings.RecentFiles` already exists and
  is populated by nothing on the Avalonia side.
- [ ] **Reopen Closed Tab** (`Ctrl+Shift+T`) - small in-memory history of closed-tab snapshots,
  WPF-only (`_closedTabHistory` in `MainWindow.xaml.cs`).
- [ ] **Code Statistics dialog** - char/word/line count, tokenized byte count. Trivial logic,
  just needs a dialog.
- [ ] **About VICE dialog** - the VICE-menu counterpart to the C64U menu's "About My C64
  Ultimate…", which Avalonia already has.
- [ ] **Go to Line** (`Ctrl+G`) - simple modal, no Core dependency.
- [ ] **Comment / Uncomment Selection** (`Ctrl+K Ctrl+C` / `Ctrl+K Ctrl+U`) in the Edit menu.
- [ ] **Make Uppercase / Make Lowercase** - plain selection case-conversion in the Edit menu.
  Unrelated to this session's C64-keyboard Upper/Lower Case Mode work - this is a text utility,
  not keyboard emulation.
- [ ] **Export as text / Import from text** (`File > Export…` / `Import…`) - save/open the
  editor's raw text as a plain `.txt`, bypassing PETSCII/tokenizing. ~30 lines each in WPF.

---

## Small / polish items - DONE

- [x] **Help menu** - "Visit READYCode on GitHub" and "View Online Docs" (URLs from the shared
  `AppSettings.GitHubUrl`/`DocsUrl`), with About READYCode moved in from the old Settings menu
  to match WPF; the Settings menu is now "Preferences > Settings…" as in WPF. On macOS, About
  and Settings still live in the application menu.
- [x] **Current-line highlight border** - `CurrentLineBorderRenderer` ported, themed via
  `ThemeEditorCurrentLineBorder`.
- [x] **Keyboard shortcut alignment** - every shortcut matched to WPF's where the same command
  exists, including File > New's three commands (which also surfaced that "New BASIC File" was
  mislabeled and a real untokenized `.bas` command was missing - fixed). Deliberate
  exceptions: `Ctrl+W` for Close Tab (WPF: `Ctrl+F4`, a Windows-only idiom), and WPF's
  `Ctrl+K`-chord shortcuts (Open/Close Folder, Comment/Uncomment, Format Code), which have no
  chord support here and keep their single-combination equivalents.
- [x] **Debug menu layout** - Start Debugging / Continue lives on the VICE and C64U menus (each
  has to name its target); the Debug menu holds only the target-independent commands. Both
  emulator menus use WPF's wording and order: Load, Run Without Debugging, Start Debugging.

---

## Already done (for context - no action needed)

BASIC/asm editing with PETSCII rendering, C64 keyboard emulation including Shift/Caps-Lock
graphics and Upper/Lower Case Mode, folder explorer incl. `.d64`/`.d81` browsing, find/replace,
live diagnostics + Problems panel, VICE + C64 Ultimate integration incl. the BASIC debugger,
Preferences (6 of WPF's ~10 sections, flatter organization - see below), the three themes, native
menu/About box/Help menu, WPF-aligned keyboard shortcuts, code-signing pipeline. The "Licenses" window WPF has as a separate dialog is
already covered by the equivalent content living behind a button in Avalonia's About box - not a
gap, just organized differently.

**Preferences organization**: WPF nests settings three levels deep (e.g. Commodore > VICE
Emulator); Avalonia currently has 6 flat tabs (Application, Text Editor, BASIC, Assembly, VICE,
C64U); the settings themselves all map across even though the grouping looks different.

---

## Judgment calls worth a decision before starting

1. **Printing** has no scoped design yet and may be disproportionately expensive for the value -
   worth deciding up front whether it's in scope for "no regression" or explicitly deferred with
   a documented reason (e.g. "export to PDF is an acceptable substitute").
2. Several items here are "same feature, necessarily different implementation" (printing,
   drag-and-drop, explorer Cut) rather than portable code - worth flagging which of those are must-match-exactly
   vs. fine to reach the same outcome a different way.
