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

### Hex Editor - DONE
- [x] Machine-language `.prg` files open in it automatically; anything else - a disk image
  included - via "Open in Hex editor" on the explorer, disk-image entry, and C64U context
  menus. A file already open in the other view is reloaded into the requested one, unless it
  has unsaved changes.
- [x] Offset / 16 hex bytes / ASCII grid drawn by one control (`Editor/HexGridCanvas.cs`, a port
  of WPF's), selection by mouse or keyboard, Cut/Copy/Paste/Delete/Select All from the Edit
  menu and a context menu, and its own undo/redo (the shared `HexUndoStack`).
- [x] Editing: double-click or Enter opens the byte in an edit box; or just type a hex digit -
  the second digit commits and moves to the next byte, so a run can be typed straight through.
- [x] Saves the bytes back to the file, into a local disk image, or into a C64U disk image.

### File Compare - DONE
- [x] Split (side-by-side) and unified views, color-coded rows with word-level highlighting of
  a modified line, long unchanged runs folded with an Expand All, previous/next change, an
  Ignore Whitespace toggle (recomputes), and a change map with a draggable viewport thumb
  beside the panes. Each side is resolved to text on its own (`.prg` detokenized, `.ml`
  disassembled), so any two comparable kinds compare.
- [x] "Select file for comparison" then "Compare with <name>" on the explorer, disk-image
  entry, and C64U context menus; the same item reads "Clear comparison selection" on the
  pending file. (WPF's second item is just "Compare file", and clears by re-selecting.)
- The diff is the shared `FileCompareEngine` / `CompareFileResolver`; the view is
  `Views/FileCompareControl` over the ported `Editor/DiffRendering.cs` pieces. One difference
  from WPF: the row tint is a background renderer, so an empty filler row is tinted too.

### Disassembler - DONE
- [x] **Disassemble at…** on the VICE and C64U menus: a read-only listing tab with a Start/End
  address toolbar that reads that range of the machine's live memory and disassembles it.
  Save As turns it into an ordinary editable assembly file, as WPF.
- [x] **Disassemble file** on machine-language files in the explorer, in disk-image entries, and
  in the C64U tree, skipping a BASIC loader stub (left as a comment describing it).
- [x] The assembly gutter (`Editor/AsmLineNumberMargin.cs`, ported) - line numbers for source,
  each line's address for a disassembly or for assembled source with a fixed origin. Assembly
  tabs previously had no gutter at all here.
- The disassembly is the shared `Asm6502Disassembler` / `PrgFileDisassembler`; the tab and
  memory-read handling is `MainViewModel.Disassembly.cs`.

### Project-wide Find/Replace in Files - DONE
- [x] A Search tab on the left activity bar (`Edit > Find in Files` `Ctrl+Shift+F`, `Replace in
  Files` `Ctrl+Shift+H` - on macOS `Cmd+Shift+F` / `Cmd+Shift+H`, which replaced the earlier
  ad-hoc `Cmd+Shift+H` alternate for the single-file Find and Replace): match case, whole
  word, regex; results grouped by file with a match count, double-click or Enter opens the
  file at the match; Replace All (confirmed) edits open tabs through their document and
  rewrites closed files, `.prg` included, re-tokenized.
- The matching and file reading is the shared `ProjectSearcher`; the results and replace are
  `MainViewModel.Search.cs`. As WPF, files are searched as they are on disk.

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

### Static Variables / Symbols panel - DONE
- [x] Every variable in a BASIC program, or every label/constant in an assembly program, with
  each occurrence as a child (read/write, defined/used, by line), under the folder explorer
  with a row splitter; `View > Variables` (`Ctrl+Alt+V`) hides it. Same settings keys as WPF
  (`ShowVariableExplorer`, `ExplorerFolderTreeHeight`).
- [x] Double-click / Enter on an occurrence jumps to its line; F2 or the context menu renames a
  variable everywhere in the file as one undo step. Rename is a text prompt rather than WPF's
  inline box, like the explorer's rename here.
- The index is the shared `VariableCrossReference` / `AsmSymbolIndex`, rebuilt in place after
  each document analysis (`MainViewModel.SymbolIndex.cs`), so expanded nodes stay expanded.

### Ghost-text completion + Ctrl+Space - DONE
- [x] Inline "ghost text" keyword-completion suggestion as you type, Tab to accept
- [x] `Ctrl+Space` to open the completion popup (Control on macOS too, as VS Code does there)
- The keyword tables and matching were already shared (`BasicCompletionProvider`,
  `AsmCompletionProvider`); the Avalonia side is `GhostTextRenderer` (a caret-layer
  background renderer rather than WPF's adorner, so it draws in the caret layer's own render
  pass and never re-enters layout), `KeywordCompletionData` over AvaloniaEdit's own
  `CompletionWindow`, and the key wiring in `MainWindow.Completion.cs`.

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

## Medium items - DONE

All UI wiring against logic that was already shared, plus per-tab caret memory that fell out of
Reopen Closed Tab (switching tabs now keeps each one's caret, as WPF does).

- [x] **Recent Files** - `File > Open Recent`, fed from the same `Settings.RecentFiles` the WPF
  app keeps, so the two share one list. Tracked on open, save, and explorer New File, as WPF.
- [x] **Reopen Closed Tab** (`Ctrl+Shift+T`) - in-memory history of the last 20 user-closed
  tabs, restoring unsaved text, the modified flag, caret, and disk-image identity. Tabs closed
  because their file was deleted are not recorded.
- [x] **Code Statistics dialog** - `View > Code Statistics`, tokenized or assembled byte count
  per the tab's language.
- [x] **About VICE dialog** - `VICE > About VICE`.
- [x] **Go to Line** (`Ctrl+G`, on the Control key on macOS too since `Cmd+G` is Find Next) -
  BASIC line number or file line number, as WPF.
- [x] **Comment / Uncomment Selection** - BASIC tabs only; no shortcuts (WPF's are `Ctrl+K`
  chords, see the note under shortcuts below).
- [x] **Make Uppercase** (`Ctrl+Shift+U`) **/ Make Lowercase**.
- [x] **Export… / Import…** - the editor's raw text as a plain `.txt`, bypassing PETSCII and
  tokenizing.

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
