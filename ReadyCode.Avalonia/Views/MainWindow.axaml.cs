// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Avalonia.Models;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Models;
using ReadyCode.Search;
using ReadyCode.Diagnostics;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;
using ReadyCode.Formatting;
using ReadyCode.Debugger;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The main window. Following the same hybrid-MVVM split as the WPF app, everything that has to
/// touch the AvaloniaEdit control directly (colorizers, folding, the breakpoint gutter, find
/// highlighting, key handling) lives here, while bindable state lives in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    #region Private Fields

    private static readonly FontFamily _petsciiFont = new("avares://ReadyCode.Avalonia/Assets/Fonts#Pet Me 64");
    private static readonly FontFamily _asciiFont = new("Menlo,Consolas,DejaVu Sans Mono,monospace");

    private static readonly FilePickerFileType _c64Files = new("C64 programs")
    {
        Patterns = ["*.prg", "*.bas", "*.asm", "*.s"],
    };

    private readonly PetsciiGlyphGenerator _petsciiGlyphGenerator = new();
    private readonly LineNumberColorizer _lineNumberColorizer = new() { LineNumberBrush = Brush("#808080"), ActiveLineNumberBrush = Brush("#000000") };
    private readonly BasicKeywordColorizer _keywordColorizer = new() { KeywordBrush = Brush("#0000FF") };
    private readonly NumberLiteralColorizer _numberLiteralColorizer = new() { NumberBrush = Brush("#008080") };
    private readonly StringLiteralColorizer _stringLiteralColorizer = new() { StringBrush = Brush("#A31515") };
    private readonly RemCommentColorizer _remCommentColorizer = new() { CommentBrush = Brush("#2d8a3e") };
    private readonly AsmMnemonicColorizer _asmMnemonicColorizer = new() { MnemonicBrush = Brush("#0000FF") };
    private readonly AsmNumberLiteralColorizer _asmNumberLiteralColorizer = new() { NumberBrush = Brush("#008080") };
    private readonly AsmLabelColorizer _asmLabelColorizer = new() { LabelBrush = Brush("#A31515") };
    private readonly AsmCommentColorizer _asmCommentColorizer = new() { CommentBrush = Brush("#2d8a3e") };
    private readonly BasicFoldingStrategy _basicFoldingStrategy = new();
    private readonly AsmFoldingStrategy _asmFoldingStrategy = new();
    private readonly DispatcherTimer _foldingTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly FindHighlightColorizer _findHighlightColorizer = new()
    {
        MatchBrush = Brush("#EEEE77"), MatchFgBrush = Brush("#000000"),
        CurrentMatchBrush = Brush("#DD8855"), CurrentMatchFgBrush = Brush("#000000"),
    };
    private readonly DispatcherTimer _findUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private ErrorSquiggleRenderer _errorSquiggleRenderer = null!;
    private readonly ColumnGuideRenderer _columnGuideRenderer = new();
    private readonly DebugCurrentLineRenderer _debugCurrentLineRenderer = new();
    private readonly BreakpointMargin _breakpointMargin = new();
    private BasicLineAddressTable? _activeTabLineAddressTable;
    private IReadOnlyList<EditorDiagnostic> _currentDiagnostics = Array.Empty<EditorDiagnostic>();
    private double _problemsHeight = 160;
    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);
    private readonly List<(int Offset, int Length)> _findMatches = new();
    private int _findMatchIndex = -1;

    private FoldingManager? _foldingManager;
    private double _explorerWidth = 230;
    private EditorTab? _boundTab;
    private bool _closeConfirmed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        Editor.TextArea.TextView.ElementGenerators.Add(_petsciiGlyphGenerator);
        _errorSquiggleRenderer = new ErrorSquiggleRenderer(Editor) ;
        _errorSquiggleRenderer.SetColor(Color.Parse("#E51400"));
        Editor.TextArea.TextView.BackgroundRenderers.Add(_errorSquiggleRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_columnGuideRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugCurrentLineRenderer);
        _breakpointMargin.BreakpointToggleRequested += async (_, line) => await ToggleBreakpointAtDocumentLineAsync(line);
        Editor.TextArea.TextView.PointerHover += Editor_PointerHover;
        Editor.TextArea.TextView.PointerHoverStopped += (_, _) => HideDiagnosticTip();
        _diagnosticsTimer.Tick += (_, _) => { _diagnosticsTimer.Stop(); RunDiagnostics(); };
        Editor.TextArea.TextEntering += Editor_TextEntering;
        Editor.AddHandler(KeyDownEvent, Editor_PreviewKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateActiveLine();
        Editor.TextChanged += (_, _) =>
        {
            _foldingTimer.Stop(); _foldingTimer.Start();
            _diagnosticsTimer.Stop(); _diagnosticsTimer.Start();
            if (FindBar.IsVisible) { _findUpdateTimer.Stop(); _findUpdateTimer.Start(); }
        };
        _foldingTimer.Tick += (_, _) => { _foldingTimer.Stop(); UpdateFoldings(); };
        _findUpdateTimer.Tick += (_, _) => { _findUpdateTimer.Stop(); UpdateFindMatches(); };

        FindBar.CloseRequested        += (_, _) => CloseFind();
        FindBar.SearchChanged         += (_, _) => UpdateFindMatches(revealCurrent: true);
        FindBar.FindNextRequested     += (_, _) => FindNext();
        FindBar.FindPreviousRequested += (_, _) => FindPrev();
        FindBar.ReplaceRequested      += (_, _) => ExecuteReplace();
        FindBar.ReplaceAllRequested   += (_, _) => ExecuteReplaceAll();

        // Cmd+Alt+F (the menu's gesture) is fine when nothing else claims it; Cmd+Shift+H is an
        // alternate that avoids Option-key text-composition quirks.
        if (OperatingSystem.IsMacOS())
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Shift), Command = new AsyncCommand(() => { OpenFind(replaceMode: true); return Task.CompletedTask; }) });

        DataContextChanged += (_, _) => AttachViewModel();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainViewModel vm)
        {
            if (vm.Settings.MainWindowWidth >= MinWidth) Width = vm.Settings.MainWindowWidth;
            if (vm.Settings.MainWindowHeight >= MinHeight) Height = vm.Settings.MainWindowHeight;
            if (vm.Settings.IsMainWindowMaximized) WindowState = WindowState.Maximized;
        }
    }

    #endregion

    #region Private Properties

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    #endregion

    #region Protected Methods

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        PersistLayout(vm);

        if (vm.IsDebugging) _ = vm.DebugStopAsync();

        var dirty = vm.OpenTabs.Where(t => t.IsModified).ToList();
        if (dirty.Count == 0)
        {
            vm.SaveSettingsAndSession();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        string names = string.Join(", ", dirty.Select(t => t.FileName));
        string? choice = await MessageDialog.ShowAsync(this, "Unsaved Changes",
            $"Save changes to {names} before closing?", "Save", "Don't Save", "Cancel");

        if (choice == "Cancel" || choice == null) return;

        if (choice == "Save")
        {
            foreach (var tab in dirty)
            {
                vm.ActiveTab = tab;
                if (!await SaveTabAsync(tab, forceDialog: false)) return;
            }
        }

        vm.SaveSettingsAndSession();
        _closeConfirmed = true;
        Close();
    }

    #endregion

    #region Private Methods

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private void AttachViewModel()
    {
        if (DataContext is not MainViewModel vm) return;

        vm.PropertyChanged += ViewModel_PropertyChanged;
        vm.BreakpointStore.Breakpoints.CollectionChanged += BreakpointStore_CollectionChanged;
        foreach (var breakpoint in vm.BreakpointStore.Breakpoints)
            breakpoint.PropertyChanged += Breakpoint_PropertyChanged;
        vm.ErrorRaised += (title, message) => Dispatcher.UIThread.Post(async () => await MessageDialog.ShowAsync(this, title, message));
        ApplyEditorSettings();
        _explorerWidth = vm.Settings.LeftPanelWidth > 60 ? vm.Settings.LeftPanelWidth : 230;
        ApplyExplorerLayout();
        _problemsHeight = vm.Settings.BottomPanelHeight > 60 ? vm.Settings.BottomPanelHeight : 160;
        ApplyProblemsLayout();
        UpdateDebugPanelText();
        BindActiveTab();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ActiveTab):
                BindActiveTab();
                break;
            case nameof(MainViewModel.StatusType):
                UpdateStatusColors();
                break;
            case nameof(MainViewModel.IsExplorerOpen):
                ApplyExplorerLayout();
                break;
            case nameof(MainViewModel.IsBottomPanelOpen):
                ApplyProblemsLayout();
                break;
            case nameof(MainViewModel.ShowColumnGuide):
            case nameof(MainViewModel.WordWrap):
                ApplyEditorSettings();
                break;
            case nameof(MainViewModel.DebugCurrentDocumentLine):
                ApplyDebugCurrentLine();
                break;
            case nameof(MainViewModel.IsDebugging):
                UpdateDebugPanelText();
                if (ViewModel.IsDebugging) { ViewModel.IsBottomPanelOpen = true; ViewModel.BottomPanelTabIndex = 1; }
                break;
            case nameof(MainViewModel.IsDebugStopped):
                UpdateDebugPanelText();
                break;
        }
    }

    // Points the single editor control at the active tab's document and restyles it for that
    // tab's language/kind - the same one-editor-many-documents shape as the WPF app.
    private void BindActiveTab()
    {
        var tab = ViewModel.ActiveTab;
        if (ReferenceEquals(tab, _boundTab)) return;

        if (_boundTab != null)
            _boundTab.PropertyChanged -= BoundTab_PropertyChanged;

        _boundTab = tab;
        if (tab == null) return;

        tab.PropertyChanged += BoundTab_PropertyChanged;

        UninstallFolding();
        _findHighlightColorizer.Clear();
        Editor.Document = tab.Document;
        ApplyLanguageStyling(tab);
        InstallFolding(tab);
        _diagnosticsTimer.Stop();
        RunDiagnostics();
        if (FindBar.IsVisible) UpdateFindMatches();
        Editor.Focus();
    }

    private void BoundTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorTab.Language) or nameof(EditorTab.Kind) && _boundTab != null)
            ApplyLanguageStyling(_boundTab);
    }

    private void ApplyLanguageStyling(EditorTab tab)
    {
        var transformers = Editor.TextArea.TextView.LineTransformers;
        transformers.Clear();

        bool isAsm = tab.Language == EditorLanguage.Asm;
        var margins = Editor.TextArea.LeftMargins;
        if (!isAsm && !margins.Contains(_breakpointMargin)) margins.Insert(0, _breakpointMargin);
        if (isAsm) margins.Remove(_breakpointMargin);
        if (isAsm)
        {
            transformers.Add(_asmMnemonicColorizer);
            transformers.Add(_asmNumberLiteralColorizer);
            transformers.Add(_asmLabelColorizer);
            transformers.Add(_asmCommentColorizer);
        }
        else
        {
            transformers.Add(_lineNumberColorizer);
            transformers.Add(_keywordColorizer);
            transformers.Add(_numberLiteralColorizer);
            transformers.Add(_stringLiteralColorizer);
            transformers.Add(_remCommentColorizer);
        }

        transformers.Add(_findHighlightColorizer);

        // A .bas file is plain ASCII source; a detokenized .prg is styled to look like what ends
        // up on a real C64 screen, which needs the PETSCII font and glyph substitution.
        bool isAsciiStyled = isAsm || tab.Kind == C64UFileKind.Bas;
        Editor.FontFamily = isAsciiStyled ? _asciiFont : _petsciiFont;
        _petsciiGlyphGenerator.IsAsmMode = isAsciiStyled;
        ApplyEditorSettings();
        Editor.TextArea.TextView.Redraw();
    }

    // Applies every setting that affects the editor control itself; safe to call repeatedly.
    private void ApplyEditorSettings()
    {
        var settings = ViewModel.Settings;
        Editor.FontSize = Math.Clamp(settings.EditorFontSize, 6, 72);
        Editor.WordWrap = settings.WordWrap;
        _columnGuideRenderer.Column = settings.ShowColumnGuide
            ? Math.Max(1, ViewModel.ActiveTab?.Language == EditorLanguage.Asm ? settings.AsmColumnGuideColumn : settings.BasicColumnGuideColumn)
            : 0;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void InstallFolding(EditorTab tab)
    {
        if (!(tab.Language == EditorLanguage.Asm ? ViewModel.Settings.AsmEnableCodeFolding : ViewModel.Settings.EnableCodeFolding))
            return;

        _foldingManager = FoldingManager.Install(Editor.TextArea);
        UpdateFoldings();
    }

    private void UninstallFolding()
    {
        if (_foldingManager == null) return;
        FoldingManager.Uninstall(_foldingManager);
        _foldingManager = null;
    }

    private void UpdateFoldings()
    {
        if (_foldingManager == null || _boundTab == null) return;

        if (_boundTab.Language == EditorLanguage.Asm)
            _asmFoldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
        else
            _basicFoldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
    }

    private void UpdateActiveLine()
    {
        int line = Editor.TextArea.Caret.Line;
        if (_lineNumberColorizer.ActiveDocumentLineNumber == line) return;
        _lineNumberColorizer.ActiveDocumentLineNumber = line;
        Editor.TextArea.TextView.Redraw();
    }

    private void UpdateStatusColors()
    {
        (StatusBar.Background, StatusTextBlock.Foreground) = ViewModel.StatusType switch
        {
            StatusType.Error => (Brush("#880000"), Brush("#FFFFFF")),
            StatusType.Warning => (Brush("#EEEE77"), Brush("#000000")),
            _ => (Brush("#EEEEEE"), Brush("#000000")),
        };
    }

    private async Task<IStorageFolder?> SuggestedFolderAsync()
    {
        string folder = ViewModel.LastDialogFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        return await StorageProvider.TryGetFolderFromPathAsync(folder);
    }

    // Collapses the explorer column (and its splitter) to nothing while the panel is hidden,
    // remembering the width so it comes back the same size.
    private void ApplyExplorerLayout()
    {
        var column = MainGrid.ColumnDefinitions[0];
        if (ViewModel.IsExplorerOpen)
        {
            column.Width = new GridLength(_explorerWidth);
            column.MinWidth = 120;
            MainGrid.ColumnDefinitions[1].Width = new GridLength(4);
        }
        else
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0) _explorerWidth = column.Width.Value;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
            MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
        }
    }

    private void ApplyProblemsLayout()
    {
        var row = EditorGrid.RowDefinitions[2];
        if (ViewModel.IsBottomPanelOpen)
        {
            row.Height = new GridLength(_problemsHeight);
            row.MinHeight = 60;
            EditorGrid.RowDefinitions[1].Height = new GridLength(4);
        }
        else
        {
            if (row.Height.IsAbsolute && row.Height.Value > 0) _problemsHeight = row.Height.Value;
            row.MinHeight = 0;
            row.Height = new GridLength(0);
            EditorGrid.RowDefinitions[1].Height = new GridLength(0);
        }
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>Runs the debounced document analysis immediately (for tests).</summary>
    internal void RunDiagnosticsNow()
    {
        _diagnosticsTimer.Stop();
        RunDiagnostics();
    }

    private void RunDiagnostics()
    {
        var tab = ViewModel.ActiveTab;
        _currentDiagnostics = tab == null ? Array.Empty<EditorDiagnostic>() : ViewModel.AnalyzeTab(tab);
        _errorSquiggleRenderer.SetDiagnostics(_currentDiagnostics);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        RefreshBreakpointMargin();
    }

    // ── Debugger ──────────────────────────────────────────────────────────────

    private void RefreshBreakpointMargin()
    {
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic })
        {
            _activeTabLineAddressTable = null;
            _breakpointMargin.EnabledBreakpointLines = new HashSet<int>();
            _breakpointMargin.DisabledBreakpointLines = new HashSet<int>();
            _breakpointMargin.InvalidateVisual();
            return;
        }

        var lineTable = BasicLineAddressTable.Build(tab.Document.Text);
        _activeTabLineAddressTable = lineTable;
        string fileKey = MainViewModel.BreakpointFileKey(tab);

        var enabled = new HashSet<int>();
        foreach (ushort basicLine in ViewModel.BreakpointStore.EnabledLinesFor(fileKey))
            if (lineTable.BasicLineToDocumentLine.TryGetValue(basicLine, out int documentLine)) enabled.Add(documentLine);

        var disabled = new HashSet<int>();
        foreach (ushort basicLine in ViewModel.BreakpointStore.DisabledLinesFor(fileKey))
            if (lineTable.BasicLineToDocumentLine.TryGetValue(basicLine, out int documentLine)) disabled.Add(documentLine);

        _breakpointMargin.EnabledBreakpointLines = enabled;
        _breakpointMargin.DisabledBreakpointLines = disabled;
        _breakpointMargin.InvalidateVisual();
    }

    private bool TryGetBasicLineAtDocumentLine(int documentLine, out ushort basicLine)
    {
        basicLine = 0;
        var tab = ViewModel.ActiveTab;
        if (tab is not { Language: EditorLanguage.Basic }) return false;
        var lineTable = _activeTabLineAddressTable ?? BasicLineAddressTable.Build(tab.Document.Text);
        return lineTable.DocumentLineToBasicLine.TryGetValue(documentLine, out basicLine);
    }

    private async Task ToggleBreakpointAtDocumentLineAsync(int documentLine)
    {
        if (ViewModel.ActiveTab is not { } tab) return;
        if (!TryGetBasicLineAtDocumentLine(documentLine, out ushort basicLine)) return; // no code on that line
        await ViewModel.ToggleBreakpointAsync(tab, basicLine);
        RefreshBreakpointMargin();
    }

    private void ApplyDebugCurrentLine()
    {
        int? line = ViewModel.DebugCurrentDocumentLine;
        _debugCurrentLineRenderer.CurrentLine = line;

        if (line is { } documentLine)
        {
            if (ViewModel.DebugTab != null && !ReferenceEquals(ViewModel.ActiveTab, ViewModel.DebugTab))
                ViewModel.ActiveTab = ViewModel.DebugTab;
            if (documentLine <= Editor.Document.LineCount)
            {
                Editor.CaretOffset = Editor.Document.GetLineByNumber(documentLine).Offset;
                RevealCaretLine();
            }
        }

        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void UpdateDebugPanelText()
    {
        string text = !ViewModel.IsDebugging
            ? "Not debugging. Use Debug > Start Debugging to run the active BASIC tab in VICE with breakpoints."
            : ViewModel.IsDebugStopped ? "No variables yet." : "Running… pause or hit a breakpoint to inspect.";
        VariablesEmptyText.Text = text;
        CallStackEmptyText.Text = !ViewModel.IsDebugging ? "Not debugging." : ViewModel.IsDebugStopped ? "Not inside a GOSUB." : "Running…";
    }

    private void MoveCaretToDocumentLine(int documentLine)
    {
        if (documentLine < 1 || documentLine > Editor.Document.LineCount) return;
        Editor.CaretOffset = Editor.Document.GetLineByNumber(documentLine).Offset;
        RevealCaretLine();
        Editor.Focus();
    }

    private void BreakpointStore_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Breakpoint breakpoint in e.OldItems) breakpoint.PropertyChanged -= Breakpoint_PropertyChanged;
        if (e.NewItems != null)
            foreach (Breakpoint breakpoint in e.NewItems) breakpoint.PropertyChanged += Breakpoint_PropertyChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var breakpoint in ViewModel.BreakpointStore.Breakpoints) { breakpoint.PropertyChanged -= Breakpoint_PropertyChanged; breakpoint.PropertyChanged += Breakpoint_PropertyChanged; }
        RefreshBreakpointMargin();
    }

    private async void Breakpoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Breakpoint.IsEnabled) || sender is not Breakpoint breakpoint) return;
        RefreshBreakpointMargin();
        await ViewModel.OnBreakpointEnabledChangedAsync(breakpoint);
    }

    private async void DebugStart_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugStartOrContinueAsync();
    private async void DebugPause_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugPauseAsync();
    private async void DebugRestart_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugRestartAsync();
    private async void DebugStop_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugStopAsync();
    private async void DebugStepOver_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugStepOverAsync();
    private async void DebugStepInto_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugStepIntoAsync();
    private async void DebugStepOut_Click(object? sender, RoutedEventArgs e) => await ViewModel.DebugStepOutAsync();

    private async void DebugRunToCursor_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetBasicLineAtDocumentLine(Editor.TextArea.Caret.Line, out ushort basicLine))
        {
            ViewModel.SetStatus("Run to Cursor requires the caret to be on a line with code.", StatusType.Error);
            return;
        }
        await ViewModel.RunToLineAsync(basicLine);
    }

    private async void DebugToggleBreakpoint_Click(object? sender, RoutedEventArgs e) => await ToggleBreakpointAtDocumentLineAsync(Editor.TextArea.Caret.Line);

    private void DebugToggleBreakpointEnabled_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab && TryGetBasicLineAtDocumentLine(Editor.TextArea.Caret.Line, out ushort basicLine))
            ViewModel.ToggleBreakpointEnabled(tab, basicLine);
    }

    private async void DebugDeleteAllBreakpoints_Click(object? sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteAllBreakpointsAsync();
        RefreshBreakpointMargin();
    }

    private void ViewDebugPanel_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBottomPanelOpen && ViewModel.BottomPanelTabIndex == 1) { ViewModel.IsBottomPanelOpen = false; return; }
        ViewModel.IsBottomPanelOpen = true;
        ViewModel.BottomPanelTabIndex = 1;
    }

    private void ViewBottomPanelClose_Click(object? sender, RoutedEventArgs e) => ViewModel.IsBottomPanelOpen = false;

    private async void VariablesList_DoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedVariableAsync();

    private async void VariablesList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await EditSelectedVariableAsync(); }
    }

    private async Task EditSelectedVariableAsync()
    {
        if (VariablesList.SelectedItem is not BasicVariable variable) return;
        if (!ViewModel.IsDebugStopped)
        {
            ViewModel.SetStatus($"Can't update {variable.Name} while running - pause first.", StatusType.Error);
            return;
        }

        string current = MainViewModel.FormatVariableValue(variable);
        if (variable.Type == BasicVariableType.String && current.Length >= 2) current = current[1..^1];
        string? text = await TextPromptDialog.ShowAsync(this, "Set Variable", $"New value for {variable.Name}:", current);
        if (text == null) return;
        await ViewModel.SetVariableAsync(variable, text);
    }

    private void BreakpointsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (BreakpointsList.SelectedItem is not Breakpoint breakpoint) return;
        var tab = ViewModel.OpenTabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, breakpoint.FilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.FileName, breakpoint.FilePath, StringComparison.OrdinalIgnoreCase));
        if (tab == null) return;

        ViewModel.ActiveTab = tab;
        var lineTable = BasicLineAddressTable.Build(tab.Document.Text);
        if (lineTable.BasicLineToDocumentLine.TryGetValue(breakpoint.LineNumber, out int documentLine))
            MoveCaretToDocumentLine(documentLine);
    }

    private void CallStackList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CallStackList.SelectedItem is not GosubFrame { DocumentLine: int documentLine }) return;
        if (ViewModel.DebugTab == null) return;
        ViewModel.ActiveTab = ViewModel.DebugTab;
        MoveCaretToDocumentLine(documentLine);
    }

    // After a save or a transfer, surface the Problems panel if the tab has issues - but never
    // from the live-typing path, so it doesn't pop up mid-edit.
    private void ShowProblemsIfAny(EditorTab tab)
    {
        if (ReferenceEquals(tab, ViewModel.ActiveTab))
        {
            _diagnosticsTimer.Stop();
            RunDiagnostics();
        }
        if (tab.Diagnostics.Count > 0)
        {
            ViewModel.IsBottomPanelOpen = true;
            ViewModel.BottomPanelTabIndex = 0;
        }
    }

    private bool TryGetDiagnosticAt(int offset, out EditorDiagnostic diagnostic)
    {
        foreach (var d in _currentDiagnostics)
        {
            if (offset >= d.Offset && offset < d.Offset + d.Length) { diagnostic = d; return true; }
        }
        diagnostic = default;
        return false;
    }

    private void Editor_PointerHover(object? sender, PointerEventArgs e)
    {
        var textView = Editor.TextArea.TextView;
        var pointInView = e.GetPosition(textView);
        // Only the text itself carries diagnostics - not the padding/margins around it.
        if (pointInView.X < 0 || pointInView.Y < 0 || pointInView.X > textView.Bounds.Width) { HideDiagnosticTip(); return; }

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position == null) { HideDiagnosticTip(); return; }

        var line = Editor.Document.GetLineByNumber(position.Value.Line);
        int column = position.Value.Column - 1;
        if (column >= line.Length) { HideDiagnosticTip(); return; } // past the end of the line
        int offset = line.Offset + column;
        if (!TryGetDiagnosticAt(offset, out var diagnostic)) { HideDiagnosticTip(); return; }

        ToolTip.SetTip(Editor, diagnostic.Message);
        ToolTip.SetIsOpen(Editor, true);
    }

    // The tip must be cleared, not just closed: while a tip is set, Avalonia's own tooltip
    // service re-shows it on any hover over the editor, diagnostic or not.
    private void HideDiagnosticTip()
    {
        ToolTip.SetIsOpen(Editor, false);
        ToolTip.SetTip(Editor, null);
    }

    private void JumpToProblem(ErrorListRow row)
    {
        ViewModel.ActiveTab = row.Tab;
        int offset = Math.Min(row.Offset, Editor.Document.TextLength);
        Editor.CaretOffset = offset;
        RevealCaretLine();
        Editor.Focus();
    }

    // Scrolls so the caret's line is comfortably inside the viewport - vertically centred when
    // it was outside - rather than relying on BringCaretToView, whose first pass works from
    // estimated line heights and can leave the target line just above the top edge.
    private void RevealCaretLine()
    {
        void Reveal()
        {
            var textView = Editor.TextArea.TextView;
            var line = Editor.Document.GetLineByOffset(Math.Min(Editor.CaretOffset, Editor.Document.TextLength));
            double top = textView.GetVisualTopByDocumentLine(line.LineNumber);
            double lineHeight = textView.DefaultLineHeight;
            double viewportTop = textView.ScrollOffset.Y;
            double viewportHeight = textView.Bounds.Height;
            if (viewportHeight <= 0) return;

            bool visible = top >= viewportTop + lineHeight && top + lineHeight <= viewportTop + viewportHeight - lineHeight;
            if (visible) return;

            Editor.ScrollToVerticalOffset(Math.Max(0, top - viewportHeight / 2 + lineHeight / 2));
        }

        Reveal();
        Editor.TextArea.Caret.BringCaretToView();
        // A second pass after layout corrects for line heights that weren't measured yet.
        Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Loaded);
    }

    private void ViewProblems_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBottomPanelOpen && ViewModel.BottomPanelTabIndex == 0) { ViewModel.IsBottomPanelOpen = false; return; }
        ViewModel.IsBottomPanelOpen = true;
        ViewModel.BottomPanelTabIndex = 0;
    }

    private void ProblemsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProblemsList.SelectedItem is ErrorListRow row) JumpToProblem(row);
    }

    private void ProblemsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProblemsList.SelectedItem is ErrorListRow row) { JumpToProblem(row); e.Handled = true; }
    }

    private void PersistLayout(MainViewModel vm)
    {
        var problemsRow = EditorGrid.RowDefinitions[2];
        if (vm.IsBottomPanelOpen && problemsRow.Height.IsAbsolute && problemsRow.Height.Value > 0)
            _problemsHeight = problemsRow.Height.Value;
        vm.Settings.BottomPanelHeight = _problemsHeight;

        var column = MainGrid.ColumnDefinitions[0];
        if (vm.IsExplorerOpen && column.Width.IsAbsolute && column.Width.Value > 0)
            _explorerWidth = column.Width.Value;
        vm.Settings.LeftPanelWidth = _explorerWidth;
        vm.Settings.IsMainWindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            vm.Settings.MainWindowWidth = Width;
            vm.Settings.MainWindowHeight = Height;
        }
    }

    private FileTreeItem? SelectedTreeItem => FileTree.SelectedItem as FileTreeItem;

    // The folder an explorer "new" action should target: the selected folder, the selected
    // file's folder, or the root when nothing is selected.
    private (string Directory, FileTreeItem? Folder) ExplorerTargetFolder()
    {
        var selected = SelectedTreeItem;
        if (selected == null || selected.IsVirtual) return (ViewModel.RootFolderPath, null);
        if (selected.IsFolder) return (selected.FullPath, selected);
        var parent = ViewModel.FindParentFolder(selected);
        return parent == null ? (ViewModel.RootFolderPath, null) : (parent.FullPath, parent);
    }

    private void OpenTreeItem(FileTreeItem item)
    {
        if (item.IsFolder || item.IsDiskImage)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        if (item.IsVirtual)
            ViewModel.OpenVirtualEntry(item);
        else if (item.FullPath.Length > 0)
            ViewModel.OpenFile(item.FullPath);
    }

    private async Task NewFileAsync()
    {
        if (!ViewModel.IsFolderOpen) return;
        var (directory, folder) = ExplorerTargetFolder();
        string? name = await TextPromptDialog.ShowAsync(this, "New File", "File name:", "untitled.prg");
        if (name == null) return;
        if (folder != null) folder.IsExpanded = true;
        ViewModel.CreateFile(directory, name);
    }

    private async Task NewFolderAsync()
    {
        if (!ViewModel.IsFolderOpen) return;
        var (directory, folder) = ExplorerTargetFolder();
        string? name = await TextPromptDialog.ShowAsync(this, "New Folder", "Folder name:");
        if (name == null) return;
        if (folder != null) folder.IsExpanded = true;
        ViewModel.CreateFolder(directory, name);
    }

    private async Task RenameItemAsync(FileTreeItem item)
    {
        string? name = await TextPromptDialog.ShowAsync(this, "Rename", $"New name for \"{item.Name}\":", item.Name);
        if (name == null || name == item.Name) return;
        ViewModel.RenameItem(item, name);
    }

    private async Task DeleteItemAsync(FileTreeItem item)
    {
        string what = item.IsFolder ? $"folder \"{item.Name}\" and all its contents" : $"\"{item.Name}\"";
        string? choice = await MessageDialog.ShowAsync(this, "Delete", $"Permanently delete {what}?", "Delete", "Cancel");
        if (choice != "Delete") return;
        ViewModel.DeleteItem(item);
    }

    private static void RevealInFileManager(FileTreeItem item)
    {
        string path = item.IsVirtual ? item.SourcePath ?? "" : item.FullPath;
        if (path.Length == 0) return;

        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", ["-R", path]) { UseShellExecute = false });
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", [Path.GetDirectoryName(path) ?? path]) { UseShellExecute = false });
        }
        catch
        {
            // Best effort - nothing sensible to do if there is no file manager.
        }
    }

    private ContextMenu BuildTreeContextMenu(FileTreeItem item)
    {
        var menu = new ContextMenu();
        var items = menu.Items;

        void Add(string header, Func<Task> action) =>
            items.Add(new MenuItem { Header = header, Command = new AsyncCommand(action) });
        void AddSync(string header, Action action) => Add(header, () => { action(); return Task.CompletedTask; });
        void Sep() => items.Add(new Separator());

        if (item.IsFolder)
        {
            Add("New File…", async () => { FileTree.SelectedItem = item; await NewFileAsync(); });
            Add("New Folder…", async () => { FileTree.SelectedItem = item; await NewFolderAsync(); });
            Sep();
            AddSync("Refresh", item.RefreshChildren);
            Sep();
        }
        else if (item.IsDiskImage)
        {
            AddSync("Refresh", item.RefreshChildren);
            Sep();
        }
        else
        {
            if (item.IsOpenable)
                AddSync(item.Kind == C64UFileKind.Asm ? "Open in Assembly editor" : "Open in BASIC editor", () => OpenTreeItem(item));
            if (item.Kind is C64UFileKind.Prg or C64UFileKind.Ml or C64UFileKind.Asm or C64UFileKind.Bas)
            {
                Add("Run on VICE", () => ViewModel.SendFileToViceAsync(item, run: true));
                Add("Load on VICE", () => ViewModel.SendFileToViceAsync(item, run: false));
            }
            Sep();
        }

        if (!item.IsVirtual)
        {
            AddSync(OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Reveal in File Manager", () => RevealInFileManager(item));
            Add("Copy Path", async () =>
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(item.FullPath);
            });
            Sep();
        }

        Add("Rename…", () => RenameItemAsync(item));
        Add("Delete", () => DeleteItemAsync(item));
        return menu;
    }

    // Saves a tab to its existing path, or prompts for one when it has none (or when forced).
    private async Task<bool> SaveTabAsync(EditorTab tab, bool forceDialog)
    {
        if (tab.IsVirtual && !forceDialog)
        {
            bool savedVirtual = ViewModel.SaveVirtualTab(tab);
            if (savedVirtual) ShowProblemsIfAny(tab);
            return savedVirtual;
        }

        string? path = tab.FilePath;

        if (forceDialog || path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save File",
                SuggestedFileName = tab.FilePath != null ? tab.FileName : (tab.Language == EditorLanguage.Asm ? "program.asm" : "program.prg"),
                DefaultExtension = tab.Language == EditorLanguage.Asm ? "asm" : "prg",
                SuggestedStartLocation = await SuggestedFolderAsync(),
                FileTypeChoices =
                [
                    new FilePickerFileType("Tokenized BASIC program") { Patterns = ["*.prg"] },
                    new FilePickerFileType("BASIC source listing") { Patterns = ["*.bas"] },
                    new FilePickerFileType("6502 assembly source") { Patterns = ["*.asm", "*.s"] },
                    FilePickerFileTypes.All,
                ],
            });

            path = file?.TryGetLocalPath();
            if (path == null) return false;
        }

        bool saved = ViewModel.SaveTab(tab, path);
        if (saved) ShowProblemsIfAny(tab);
        return saved;
    }

    #endregion

    #region Event Handlers

    private void FileNew_Click(object? sender, RoutedEventArgs e) => ViewModel.NewTab(EditorLanguage.Basic);

    private void FileNewAsm_Click(object? sender, RoutedEventArgs e) => ViewModel.NewTab(EditorLanguage.Asm);

    private async void FileOpen_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true,
            SuggestedStartLocation = await SuggestedFolderAsync(),
            FileTypeFilter = [_c64Files, FilePickerFileTypes.All],
        });

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;

            // Re-opening a file that has unsaved edits is a "revert to saved" - make sure that's
            // what the user meant before throwing the edits away.
            if (ViewModel.FindOpenTab(path) is { IsModified: true } dirty)
            {
                string? choice = await MessageDialog.ShowAsync(this, "Reload File",
                    $"{dirty.FileName} has unsaved changes. Reload it from disk and discard them?", "Reload", "Cancel");
                if (choice != "Reload") continue;
            }

            ViewModel.OpenFile(path);
        }
    }

    private async void FileSave_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab)
            await SaveTabAsync(tab, forceDialog: false);
    }

    private async void FileSaveAs_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab)
            await SaveTabAsync(tab, forceDialog: true);
    }

    private async void FileClose_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is not { } tab) return;

        if (tab.IsModified)
        {
            string? choice = await MessageDialog.ShowAsync(this, "Unsaved Changes",
                $"Save changes to {tab.FileName}?", "Save", "Don't Save", "Cancel");
            if (choice == "Cancel" || choice == null) return;
            if (choice == "Save" && !await SaveTabAsync(tab, forceDialog: false)) return;
        }

        ViewModel.CloseTab(tab);
    }

    private void FileExit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void FileOpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false,
            SuggestedStartLocation = ViewModel.IsFolderOpen ? await StorageProvider.TryGetFolderFromPathAsync(ViewModel.RootFolderPath) : null,
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
        {
            ViewModel.LoadFolder(path);
            ViewModel.IsExplorerOpen = true;
            ViewModel.SaveSettings();
        }
    }

    private void FileCloseFolder_Click(object? sender, RoutedEventArgs e) => ViewModel.CloseFolder();

    private void ViewExplorer_Click(object? sender, RoutedEventArgs e) => ViewModel.IsExplorerOpen = !ViewModel.IsExplorerOpen;
    private void ViewColumnGuide_Click(object? sender, RoutedEventArgs e) => ViewModel.ShowColumnGuide = !ViewModel.ShowColumnGuide;
    private void ViewWordWrap_Click(object? sender, RoutedEventArgs e) => ViewModel.WordWrap = !ViewModel.WordWrap;
    private void ViewStatusBar_Click(object? sender, RoutedEventArgs e) => ViewModel.ShowStatusBar = !ViewModel.ShowStatusBar;

    private async void ExplorerNewFile_Click(object? sender, RoutedEventArgs e) => await NewFileAsync();
    private async void ExplorerNewFolder_Click(object? sender, RoutedEventArgs e) => await NewFolderAsync();
    private void ExplorerRefresh_Click(object? sender, RoutedEventArgs e) => ViewModel.RefreshRootItems();
    private void ExplorerCollapse_Click(object? sender, RoutedEventArgs e) => ViewModel.CollapseAllFolders();

    private void FileTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not FileTreeItem item) return;
        if (item.IsFolder || item.IsDiskImage) return; // TreeView already toggles expansion
        OpenTreeItem(item);
        e.Handled = true;
    }

    private async void FileTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (SelectedTreeItem is not { } item) return;

        switch (e.Key)
        {
            case Key.Enter:
                OpenTreeItem(item);
                e.Handled = true;
                break;
            case Key.F2:
                e.Handled = true;
                await RenameItemAsync(item);
                break;
            case Key.Delete:
            case Key.Back when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                e.Handled = true;
                await DeleteItemAsync(item);
                break;
        }
    }

    private void FileTree_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Right-click (or the menu key) on a row: select it and show its menu. The row is found
        // from the event source so the menu always matches the item under the pointer.
        if ((e.Source as Control)?.DataContext is not FileTreeItem item || item.Name.Length == 0) return;

        FileTree.SelectedItem = item;
        var container = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        BuildTreeContextMenu(item).Open(container ?? (Control)FileTree);
        e.Handled = true;
    }

    // ── Find / replace ────────────────────────────────────────────────────────

    private void OpenFind(bool replaceMode)
    {
        FindBar.Open(Editor.SelectedText, replaceMode);
        UpdateFindMatches();
    }

    private void CloseFind()
    {
        var previous = _findMatches.ToArray();
        _findMatches.Clear();
        _findMatchIndex = -1;
        _findHighlightColorizer.Clear();
        RedrawMatches(previous);
        Editor.Focus();
    }

    /// <summary>Re-runs the find (for tests), the way the post-edit debounce does.</summary>
    internal void UpdateFindMatchesNow() => UpdateFindMatches();

    // revealCurrent: scroll the nearest match into view and select it, as when typing a search
    // term (VS Code style). The debounced re-search after an edit leaves the view alone.
    private void UpdateFindMatches(bool revealCurrent = false)
    {
        var previousMatches = _findMatches.Count > 0 ? _findMatches.ToArray() : null;
        _findMatches.Clear();
        string searchText = FindBar.SearchText;

        if (string.IsNullOrEmpty(searchText))
        {
            _findHighlightColorizer.Clear();
            if (previousMatches != null) RedrawMatches(previousMatches);
            _findMatchIndex = -1;
            FindBar.SetMatchCount(0, 0);
            return;
        }

        _findMatches.AddRange(ProjectSearcher.FindMatches(Editor.Document.Text, searchText, FindBar.MatchCase, FindBar.WholeWord, FindBar.UseRegex));

        _findMatchIndex = FindNearestMatchIndex();
        _findHighlightColorizer.SetMatches(Editor.Document, _findMatches, _findMatchIndex);
        if (previousMatches != null) RedrawMatches(previousMatches);
        RedrawMatches(_findMatches);
        FindBar.SetMatchCount(_findMatchIndex + 1, _findMatches.Count);

        if (revealCurrent && _findMatchIndex >= 0)
            NavigateToCurrentMatch();
    }

    // Redraws only the visual lines overlapping the given segments rather than the whole view.
    private void RedrawMatches(IEnumerable<(int Offset, int Length)> matches)
    {
        foreach (var (offset, length) in matches)
            Editor.TextArea.TextView.Redraw(offset, length);
    }

    // The match at or after the selection start (a selected match stays current, even though the
    // caret sits at its end), else the one containing the caret, else the first.
    private int FindNearestMatchIndex()
    {
        if (_findMatches.Count == 0) return -1;
        int anchor = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        for (int i = 0; i < _findMatches.Count; i++)
        {
            var (offset, length) = _findMatches[i];
            if (offset >= anchor || (anchor > offset && anchor < offset + length)) return i;
        }
        return 0;
    }

    private void FindNext()
    {
        if (!FindBar.IsVisible) { OpenFind(replaceMode: false); return; }
        if (_findMatches.Count == 0) return;
        _findMatchIndex = (_findMatchIndex + 1) % _findMatches.Count;
        NavigateToCurrentMatch();
    }

    private void FindPrev()
    {
        if (!FindBar.IsVisible) { OpenFind(replaceMode: false); return; }
        if (_findMatches.Count == 0) return;
        _findMatchIndex = (_findMatchIndex - 1 + _findMatches.Count) % _findMatches.Count;
        NavigateToCurrentMatch();
    }

    private void NavigateToCurrentMatch()
    {
        if (_findMatchIndex < 0 || _findMatchIndex >= _findMatches.Count) return;
        var (offset, length) = _findMatches[_findMatchIndex];

        Editor.Select(offset, length);
        _findHighlightColorizer.SetMatches(Editor.Document, _findMatches, _findMatchIndex);
        RevealCaretLine();
        RedrawMatches(_findMatches);
        FindBar.SetMatchCount(_findMatchIndex + 1, _findMatches.Count);
    }

    private void ExecuteReplace()
    {
        if (_findMatchIndex < 0 || _findMatchIndex >= _findMatches.Count)
        {
            UpdateFindMatches();
            if (_findMatches.Count == 0) return;
        }

        var (offset, length) = _findMatches[_findMatchIndex];
        Editor.Document.Replace(offset, length, ReplacementText());
        UpdateFindMatches();
        NavigateToCurrentMatch();
    }

    // Replacement text follows the same C64 case rule as typing: upper case for BASIC.
    private string ReplacementText() =>
        ViewModel.ActiveTab?.Language == EditorLanguage.Asm ? FindBar.ReplaceText : FindBar.ReplaceText.ToUpperInvariant();

    private void ExecuteReplaceAll()
    {
        if (_findMatches.Count == 0) UpdateFindMatches();
        if (_findMatches.Count == 0) return;

        using (Editor.Document.RunUpdate())
        {
            string replacement = ReplacementText();
            for (int i = _findMatches.Count - 1; i >= 0; i--)
                Editor.Document.Replace(_findMatches[i].Offset, _findMatches[i].Length, replacement);
        }
        UpdateFindMatches();
    }

    private void EditFind_Click(object? sender, RoutedEventArgs e) => OpenFind(replaceMode: false);
    private void EditReplace_Click(object? sender, RoutedEventArgs e) => OpenFind(replaceMode: true);
    private void EditFindNext_Click(object? sender, RoutedEventArgs e) => FindNext();
    private void EditFindPrevious_Click(object? sender, RoutedEventArgs e) => FindPrev();

    // ── Editor input ──────────────────────────────────────────────────────────

    // C64 BASIC is upper case by default - force typed text to match, the way an unshifted key
    // on a real C64 produces an upper-case letter. A shifted letter right after an unshifted
    // keyword prefix (e.g. g then shift-O for GOTO) is the C64's keyword abbreviation and is
    // stored as the PETSCII graphic character the machine would show. Assembly source case is
    // significant (labels, comments), so it's left alone.
    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm || string.IsNullOrEmpty(e.Text)) return;

        string insertText = TryGetKeywordAbbreviationGlyph(e.Text) ?? e.Text.ToUpperInvariant();
        if (insertText == e.Text) return;

        e.Handled = true;
        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, insertText);
        int caret = start + insertText.Length;
        Editor.CaretOffset = caret;
        Editor.Select(caret, 0);
    }

    private string? TryGetKeywordAbbreviationGlyph(string text)
    {
        if (Editor.SelectionLength > 0) return null;
        if (text.Length != 1 || !char.IsAsciiLetterUpper(text[0])) return null;

        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        int caretCol = Editor.CaretOffset - line.Offset;
        string lineText = document.GetText(line);

        // Never inside a string literal - a shifted keystroke there is raw PETSCII content.
        bool inString = false;
        for (int i = 0; i < caretCol; i++)
            if (lineText[i] == '"') inString = !inString;
        if (inString) return null;

        char shiftedLower = char.ToLowerInvariant(text[0]);
        int prefixAvailable = Math.Min(caretCol, BasicKeywordAbbreviations.MaxLength - 1);
        for (int prefixLen = prefixAvailable; prefixLen >= 0; prefixLen--)
        {
            string candidate = lineText.Substring(caretCol - prefixLen, prefixLen) + shiftedLower;
            if (BasicKeywordAbbreviations.ToKeyword.ContainsKey(candidate))
                return shiftedLower.ToString();
        }

        return null;
    }

    // Paste goes through here (menu, context menu, and the keyboard shortcut) so pasted BASIC
    // is upper-cased like typed BASIC; assembly keeps its case.
    private async Task PasteAsync()
    {
        if (Clipboard == null) return;
        string? text = await Clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        if (ViewModel.ActiveTab?.Language != EditorLanguage.Asm)
            text = text.ToUpperInvariant();

        int start = Editor.SelectionStart;
        Editor.Document.Replace(start, Editor.SelectionLength, text);
        int caret = start + text.Length;
        Editor.CaretOffset = caret;
        Editor.Select(caret, 0);
    }

    private async void Editor_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool primary = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        if ((e.Key == Key.V && primary) || (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            e.Handled = true;
            await PasteAsync();
            return;
        }

        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
            HandleEditingKey(e);
    }

    // Line-number zero-padding and auto-numbering for BASIC, auto-indent for assembly - the
    // behaviours behind the Formatting preferences, ported from the WPF app.
    private void HandleEditingKey(KeyEventArgs e)
    {
        bool isEnter = e.Key == Key.Enter;
        bool isSpaceOrTab = e.Key is Key.Space or Key.Tab;
        if (!isEnter && !isSpaceOrTab) return;
        if (Editor.SelectionLength > 0) return;

        var tab = ViewModel.ActiveTab;
        if (tab == null) return;

        if (tab.Language == EditorLanguage.Asm)
        {
            if (isEnter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && ViewModel.Settings.AsmAutoIndent)
            {
                e.Handled = true;
                InsertAsmNewlineWithIndent();
            }
            return;
        }

        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);
        Match match = _leadingLineNumberPattern.Match(lineText);

        // Zero-pad: fires on Space/Tab (at the end of the line number) and Enter (anywhere on the line).
        int padding = ViewModel.Settings.LineNumberPadding;
        if (padding > 0 && match.Success)
        {
            bool shouldPad = isEnter;
            if (isSpaceOrTab)
            {
                int caretCol = Editor.CaretOffset - line.Offset;
                int lineNumEndCol = match.Groups[2].Index + match.Groups[2].Length;
                shouldPad = caretCol == lineNumEndCol;
            }

            if (shouldPad)
            {
                string digits = match.Groups[2].Value;
                if (digits.Length < padding)
                {
                    string padded = digits.PadLeft(padding, '0');
                    int numberStart = line.Offset + match.Groups[2].Index;
                    int numberEnd = numberStart + digits.Length;
                    int delta = padded.Length - digits.Length;
                    int oldCaretOffset = Editor.CaretOffset;

                    document.Replace(numberStart, digits.Length, padded);

                    if (oldCaretOffset >= numberEnd)
                        Editor.CaretOffset = oldCaretOffset + delta;
                    else if (oldCaretOffset > numberStart)
                        Editor.CaretOffset = numberStart + padded.Length;

                    lineText = document.GetText(line);
                    match = _leadingLineNumberPattern.Match(lineText);
                }
            }
        }

        // Auto-number: on Enter when the line has code after its number. Shift+Enter gives a
        // plain newline.
        bool isShiftEnter = isEnter && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!isEnter || isShiftEnter || !ViewModel.Settings.AutoNumberLines || !match.Success) return;

        string afterNumber = lineText[(match.Groups[2].Index + match.Groups[2].Length)..];
        if (string.IsNullOrWhiteSpace(afterNumber)) return;
        if (!int.TryParse(match.Groups[2].Value, out int currentNumber)) return;

        int nextNumber = currentNumber + ViewModel.Settings.AutoNumberIncrement;

        // If the increment would land on or past the next existing line number, split the gap;
        // if there's no room at all, fall back to a plain newline.
        var nextDocLine = line.NextLine;
        if (nextDocLine != null)
        {
            Match nextMatch = _leadingLineNumberPattern.Match(document.GetText(nextDocLine));
            if (nextMatch.Success && int.TryParse(nextMatch.Groups[2].Value, out int nextExisting) && nextNumber >= nextExisting)
            {
                int midpoint = (currentNumber + nextExisting) / 2;
                if (midpoint <= currentNumber) return;
                nextNumber = midpoint;
            }
        }

        string nextLabel = padding > 0 ? nextNumber.ToString().PadLeft(padding, '0') : nextNumber.ToString();
        e.Handled = true;
        int insertOffset = Editor.CaretOffset;
        document.Insert(insertOffset, Environment.NewLine + nextLabel + " ");
        Editor.CaretOffset = insertOffset + Environment.NewLine.Length + nextLabel.Length + 1;
    }

    // Enter in an assembly tab with Auto-indent on: normalizes the line being left (re-indents a
    // bare mnemonic line to the mnemonic column and upper-cases the mnemonic; realigns an inline
    // comment to the comment column), then indents the new line to the mnemonic column if the
    // current line was an instruction.
    private void InsertAsmNewlineWithIndent()
    {
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        string lineText = document.GetText(line);
        int caretInLine = Editor.CaretOffset - line.Offset;
        bool caretAtEnd = caretInLine == lineText.Length;

        string workingLine = lineText;
        string trimmedStart = workingLine.TrimStart();
        int oldIndentLength = workingLine.Length - trimmedStart.Length;
        string trimmed = trimmedStart.TrimEnd();

        bool isMnemonicLine = AsmCodeFormatter.TryParseAsmMnemonicLine(trimmed, out string mnemonic, out string rest);
        string indent = isMnemonicLine ? new string(' ', Math.Max(0, ViewModel.Settings.AsmMnemonicIndentColumn - 1)) : "";

        if (isMnemonicLine)
        {
            string normalized = indent + mnemonic.ToUpperInvariant() + rest;
            if (normalized != workingLine)
            {
                caretInLine = Math.Clamp(caretInLine + (indent.Length - oldIndentLength), 0, normalized.Length);
                workingLine = normalized;
            }
        }

        int semicolonIndex = workingLine.IndexOf(';');
        if (semicolonIndex > 0)
        {
            string codePart = workingLine[..semicolonIndex];
            if (!string.IsNullOrWhiteSpace(codePart))
            {
                string commentPart = workingLine[semicolonIndex..];
                string trimmedCode = codePart.TrimEnd();
                int targetLength = Math.Max(0, ViewModel.Settings.AsmCommentAlignColumn - 1);
                string alignedCode = trimmedCode.Length < targetLength ? trimmedCode.PadRight(targetLength) : trimmedCode + "  ";
                string realigned = alignedCode + commentPart;

                if (realigned != workingLine)
                {
                    if (caretAtEnd)
                        caretInLine = realigned.Length;
                    else if (caretInLine >= semicolonIndex)
                        caretInLine = Math.Clamp(alignedCode.Length + (caretInLine - semicolonIndex), 0, realigned.Length);
                    else
                        caretInLine = Math.Clamp(caretInLine, 0, realigned.Length);

                    workingLine = realigned;
                }
            }
        }

        if (workingLine != lineText)
            document.Replace(line.Offset, line.Length, workingLine);

        int insertOffset = line.Offset + caretInLine;
        document.Insert(insertOffset, Environment.NewLine + indent);
        Editor.CaretOffset = insertOffset + Environment.NewLine.Length + indent.Length;
    }

    private void Editor_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var menu = new ContextMenu();
        var items = menu.Items;
        void Add(string header, Func<Task> action) => items.Add(new MenuItem { Header = header, Command = new AsyncCommand(action) });
        void AddSync(string header, Action action) => Add(header, () => { action(); return Task.CompletedTask; });

        Add("Run on VICE", ViewModel.RunOnViceAsync);
        Add("Load on VICE", ViewModel.TransferToViceAsync);
        items.Add(new Separator());
        AddSync("Undo", () => Editor.Undo());
        AddSync("Redo", () => Editor.Redo());
        items.Add(new Separator());
        AddSync("Cut", Editor.Cut);
        AddSync("Copy", Editor.Copy);
        Add("Paste", PasteAsync);
        AddSync("Delete", Editor.Delete);
        items.Add(new Separator());
        AddSync("Select All", Editor.SelectAll);

        menu.Open(Editor);
        e.Handled = true;
    }

    private void EditUndo_Click(object? sender, RoutedEventArgs e) => Editor.Undo();
    private void EditRedo_Click(object? sender, RoutedEventArgs e) => Editor.Redo();
    private void EditCut_Click(object? sender, RoutedEventArgs e) => Editor.Cut();
    private void EditCopy_Click(object? sender, RoutedEventArgs e) => Editor.Copy();
    private async void EditPaste_Click(object? sender, RoutedEventArgs e) => await PasteAsync();
    private void EditDelete_Click(object? sender, RoutedEventArgs e) => Editor.Delete();
    private void EditSelectAll_Click(object? sender, RoutedEventArgs e) => Editor.SelectAll();

    private async void ViceRun_Click(object? sender, RoutedEventArgs e)
    {
        await ViewModel.RunOnViceAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }

    private async void ViceTransfer_Click(object? sender, RoutedEventArgs e)
    {
        await ViewModel.TransferToViceAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }
    private async void ViceReset_Click(object? sender, RoutedEventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.ResetAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine reset.");
    private async void ViceReboot_Click(object? sender, RoutedEventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.RebootAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine rebooted.");
    private async void VicePause_Click(object? sender, RoutedEventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.PauseAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine paused.");
    private async void ViceResume_Click(object? sender, RoutedEventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.ResumeAsync(), "VICE machine resumed.");
    private async void VicePowerOff_Click(object? sender, RoutedEventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.PowerOffAsync(ViewModel.Settings.ViceEmulatorPath), "VICE emulator closed.");

    private async void Preferences_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(ViewModel.Settings);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;

        ViewModel.SaveSettings();
        ViewModel.NotifySettingsChanged();
        ApplyEditorSettings();
        if (_boundTab != null)
        {
            UninstallFolding();
            InstallFolding(_boundTab);
        }
        RunDiagnosticsNow();
        ViewModel.SetStatus("Preferences saved.");
    }

    #endregion
}
