# Contributing to READYCode

Thanks for taking an interest in READYCode. This document covers the practical bits: how to get a dev
environment running, the conventions the existing code follows, and what to do before opening a PR.

## Development setup

See the [README](README.md#getting-started) for prerequisites, cloning, and building. In short:

```bash
git clone https://github.com/jbramwell/READYCode/
cd READYCode
dotnet build READYCode/ReadyCode.csproj -c Debug
dotnet test ReadyCode.Tests/ReadyCode.Tests.csproj
```

(Building/testing via `ReadyCode.sln` instead also works, but additionally tries to build
`ReadyCode.Packaging`, a `.wapproj` that only builds inside Visual Studio - see the README's Build
section for details. `ReadyCode.slnf` is a solution filter that leaves out that project and the WiX
installer, so it builds on any OS.)

### The cross-platform projects

The repository also contains a second front end, `ReadyCode.Avalonia`, which runs on macOS and
Linux, and `ReadyCode.Core`, the UI-framework-free library both front ends share. See
[README-Avalonia.md](README-Avalonia.md) for how to build and run them.

The conventions below apply to those projects as well. Two things worth knowing before working in
them:

- **New non-UI logic belongs in `ReadyCode.Core`**, so both front ends get it. `ReadyCode.Core`
  must not reference WPF, Avalonia, or AvalonEdit; where a shared model needs the UI thread it
  exposes a hook the front ends install at startup (`FileTreeItem.DeferToUiThread`,
  `MainViewModel.RunOnUiThread`).
- **A change to shared behavior should land in both front ends** or explicitly say why it didn't.
  If you change PETSCII rendering, tokenizing, or diagnostics, the WPF and Avalonia editors should
  still agree.

## Reporting bugs / proposing features

Open an issue describing:

- What you expected vs. what happened (for bugs), including the BASIC source or `.prg` file if it's
  reproduction-relevant.
- For feature requests, the motivating use case - this project is scoped around BASIC editing and the
  C64 Ultimate workflow, so it helps to explain how a feature fits that.

## Coding conventions

These reflect patterns already used throughout the codebase - match them rather than introducing a new
style in a new file:

- **Naming conventions follow Microsoft's standard C# guidelines:**
  - PascalCase for classes, public properties, and public methods
  - camelCase with `_` prefix for private fields (e.g. `_myField`)
  - camelCase for method parameters and local variables

- **Class member ordering:** private fields first, then constructors, 
  public properties, public methods, and finally private methods. 
  Static members come before instance members within each group.

- **XML doc comments (`/// <summary>`)** are required on all public 
  classes, properties, and methods. Inline comments should explain 
  *why*, not *what* - identifiers should make the "what" obvious.

- **Hybrid MVVM, not strict MVVM.** Bindable state (open tabs, settings, status bar) lives in
  `MainViewModel`. Anything that has to touch the AvalonEdit control directly lives in
  `MainWindow.xaml.cs` code-behind - don't fight this split by trying to wrap AvalonEdit behind a
  view-model abstraction.

- **Commands** are plain `RelayCommand` instances (`ReadyCode/RelayCommand.cs`) - `new
  RelayCommand(execute, canExecute)`. If a menu item should be disabled under some condition, add a
  `canExecute` predicate rather than silently no-op-ing inside `execute`.

- **Reuse existing helpers before adding new ones.** A few examples worth knowing about before you
  duplicate them:
  - `PetsciiScreenCodeMap` (in `Tokenizer/`) is the single source of truth for PETSCII byte -> C64
    character-ROM screen code, used by both the editor's live renderer (`PetsciiGlyphGenerator`) and by
    printing (`Printing/SourcePrinter.cs`). If you touch PETSCII rendering, both call sites should stay
    in sync.
  - `MainWindow.xaml.cs` has tree-search helpers (`FindTreeViewItem`, `FindVisualChild`,
    `FindItemByPath`) used by inline rename/create and drag-and-drop. Reuse them instead of writing a
    new tree walk.
  
- **No premature abstraction.** Don't introduce an interface, base class, or config flag for a single
  use site. Three similar lines are fine; a generic mechanism for a hypothetical second use case is not.

- **Don't reformat unrelated code** in a PR that's fixing/adding something specific - keep diffs
  focused so they're reviewable.

## Before opening a PR

1. `dotnet build ReadyCode/ReadyCode.csproj -c Debug` - must build with no new warnings. If you
   touched `ReadyCode.Core` or `ReadyCode.Avalonia`, `dotnet build ReadyCode.slnf` as well.
2. `dotnet test ReadyCode.Tests/ReadyCode.Tests.csproj` - must pass (plus
   `ReadyCode.Avalonia.Tests` for changes to the Avalonia front end). Add tests under
   `ReadyCode.Tests/` for new pure-logic code (tokenizer, minify, prettify, and similar are good
   candidates; UI/AvalonEdit-coupled code is harder to unit test and isn't currently covered - manual
   verification is fine there).
3. For any UI-visible change, actually run the app (`dotnet run --project ReadyCode/ReadyCode.csproj`)
   and exercise the change rather than relying on the build succeeding.
4. Keep commits/PRs scoped to one change - separate unrelated fixes into separate PRs where practical.
5. Any new `.cs` files must include the copyright header at the top:
```csharp
   // Copyright (c) 2026 Moonspace Labs, LLC
   // Licensed under the MIT License. See LICENSE in the project root for license information.
```

## Pull requests

- Describe *why* the change is needed, not just what changed (the diff already shows what changed).
- Mention any manual testing you did, especially for anything touching printing, the C64 Ultimate
  integration, or theme files - these are hard to exercise via automated tests.
