// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Avalonia;
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
using ReadyCode.Core.Interop;
using ReadyCode.Avalonia.Models;
using ReadyCode.Avalonia.Themes;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Models;
using ReadyCode.Search;
using ReadyCode.Diagnostics;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;
using ReadyCode.Formatting;
using ReadyCode.Debugger;
using ReadyCode.Diff;
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

    private static readonly FontFamily _petsciiFont = EditorFonts.Petscii;
    private static readonly FontFamily _asciiFont = EditorFonts.Ascii;

    private static readonly FilePickerFileType _c64Files = new("C64 programs")
    {
        Patterns = ["*.prg", "*.bas", "*.asm", "*.s"],
    };

    private readonly PetsciiGlyphGenerator _petsciiGlyphGenerator = new();
    // Brushes for all of these come from the active theme - see ApplyThemeBrushes.
    private readonly LineNumberColorizer _lineNumberColorizer = new();
    private readonly BasicKeywordColorizer _keywordColorizer = new();
    private readonly NumberLiteralColorizer _numberLiteralColorizer = new();
    private readonly StringLiteralColorizer _stringLiteralColorizer = new();
    private readonly RemCommentColorizer _remCommentColorizer = new();
    private readonly AsmMnemonicColorizer _asmMnemonicColorizer = new();
    private readonly AsmNumberLiteralColorizer _asmNumberLiteralColorizer = new();
    private readonly AsmLabelColorizer _asmLabelColorizer = new();
    private readonly AsmCommentColorizer _asmCommentColorizer = new();
    private readonly BasicFoldingStrategy _basicFoldingStrategy = new();
    private readonly AsmFoldingStrategy _asmFoldingStrategy = new();
    private readonly DispatcherTimer _foldingTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly FindHighlightColorizer _findHighlightColorizer = new();
    private readonly DispatcherTimer _findUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private ErrorSquiggleRenderer _errorSquiggleRenderer = null!;
    private readonly ColumnGuideRenderer _columnGuideRenderer = new();
    private CurrentLineBorderRenderer _currentLineBorderRenderer = null!;
    private readonly DebugCurrentLineRenderer _debugCurrentLineRenderer = new();
    private readonly BreakpointMargin _breakpointMargin = new();
    private readonly AsmLineNumberMargin _asmLineNumberMargin = new();
    private readonly TabStopElementGenerator _tabStopGenerator = new();
    private BasicLineAddressTable? _activeTabLineAddressTable;
    private IReadOnlyList<EditorDiagnostic> _currentDiagnostics = Array.Empty<EditorDiagnostic>();
    private double _problemsHeight = 160;
    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);
    private readonly List<(int Offset, int Length)> _findMatches = new();
    private int _findMatchIndex = -1;

    private FoldingManager? _foldingManager;
    private double _explorerWidth = 230;
    private double _rightPanelWidth = 230;
    private EditorTab? _boundTab;
    private bool _closeConfirmed;

    // Avalonia's TextInputEventArgs carries no modifier state of its own (unlike WPF's
    // TextCompositionEventArgs, which reads the live global Keyboard.Modifiers) - cached from the
    // tunnel-routed KeyDown that always precedes the TextInput for the same keystroke.
    private KeyModifiers _lastKeyModifiers;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // AvaloniaEdit's own element generator would otherwise claim the C0/C1 control codes
        // first and draw them as boxed ANSI names (CHR$(147) as "STS"), instead of leaving them
        // to the PETSCII glyph generator - same setting the WPF editor turns off.
        Editor.Options.ShowBoxForControlCharacters = false;
        Editor.TextArea.TextView.ElementGenerators.Add(_petsciiGlyphGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_tabStopGenerator);
        _errorSquiggleRenderer = new ErrorSquiggleRenderer(Editor);
        _currentLineBorderRenderer = new CurrentLineBorderRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentLineBorderRenderer);
        ApplyThemeBrushes();
        AppTheme.Changed += OnThemeChanged;
        Closed += (_, _) => AppTheme.Changed -= OnThemeChanged;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_errorSquiggleRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_columnGuideRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugCurrentLineRenderer);
        InstallCompletion();
        HexEditor.ByteEdited += (_, _) => { if (ViewModel.ActiveTab is { IsHexMode: true } tab) tab.IsModified = true; };
        HexEditor.ContextRequested += HexEditor_ContextRequested;
        CompareView.ViewStateChanged += (isUnified, ignoreWhitespace) =>
        {
            if (ViewModel.ActiveTab is { IsCompareMode: true } tab)
            {
                tab.CompareIsUnified = isUnified;
                tab.CompareIgnoreWhitespace = ignoreWhitespace;
                tab.CompareResult = CompareView.Result;
            }
        };
        DisassemblyToolbar.DisassembleRequested += async (_, _) => await DisassembleRequestedAsync();
        InstallExplorerDragDrop();
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

        AddQuickKeyBindings();
        ConfigureMacOSMenus();
        Opened += (_, _) => AddMenuShortcutBindings();

        // No change-notification event exists for Caps Lock - Activated catches a toggle made
        // while some other window had focus; Editor_PreviewKeyDown catches one made via the
        // physical key while typing here.
        Activated += (_, _) => ViewModel.RefreshKeyboardLockStatus();

        DataContextChanged += (_, _) => AttachViewModel();
    }

    // On macOS, Quit is supplied by the system and About and Preferences belong in the
    // application menu (which App sets up), so the menu bar should not carry them as well.
    private void ConfigureMacOSMenus()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var menu = NativeMenu.GetMenu(this);
        if (menu == null) return;

        RemoveItem(menu, "Preferences");
        if (FindItem(menu, "File")?.Menu is { } fileMenu)
        {
            RemoveItem(fileMenu, "Exit");
            RemoveTrailingSeparator(fileMenu);
        }
        if (FindItem(menu, "Help")?.Menu is { } helpMenu)
        {
            RemoveItem(helpMenu, "About READYCode");
            RemoveTrailingSeparator(helpMenu);
        }
    }

    private static void RemoveTrailingSeparator(NativeMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is NativeMenuItemSeparator trailing)
            menu.Items.Remove(trailing);
    }

    // Where the menu is exported to the system (macOS), each item's Gesture becomes a real menu
    // key equivalent and the shortcuts work with nothing further. Where it is drawn inside the
    // window instead (Windows, Linux), that does not register accelerators, so the same gestures
    // are bound on the window. Driven off the menu itself, so the shortcuts cannot drift from
    // what the menu advertises. Deferred to Opened, because whether the menu was exported is not
    // known until the window has a platform handle.
    internal void AddMenuShortcutBindings()
    {
        if (NativeMenu.GetIsNativeMenuExported(this)) return;
        if (NativeMenu.GetMenu(this) is not { } menu) return;

        foreach (var top in menu.Items.OfType<NativeMenuItem>())
        {
            foreach (var item in top.Menu?.Items.OfType<NativeMenuItem>() ?? [])
            {
                if (item is NativeMenuItemSeparator || item.Gesture is not { } gesture) continue;

                var target = item;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = gesture,
                    Command = new AsyncCommand(() =>
                    {
                        // The same entry point the platform menu exporters use to fire an item.
                        if (target.IsEnabled)
                            ((INativeMenuItemExporterEventsImplBridge)target).RaiseClicked();
                        return Task.CompletedTask;
                    }),
                });
            }
        }
    }

    private static NativeMenuItem? FindItem(NativeMenu menu, string header) =>
        menu.Items.OfType<NativeMenuItem>().FirstOrDefault(item => item.Header == header);

    private static void RemoveItem(NativeMenu menu, string header)
    {
        if (FindItem(menu, header) is { } item)
            menu.Items.Remove(item);
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

        // An active debug session is cleaned up (VICE detached, the C64U's poll loop joined)
        // before the window goes, so closing is cancelled and re-run once that has finished.
        if (vm.IsDebugging)
        {
            e.Cancel = true;
            await vm.DebugStopAsync();
            Close(); // runs OnClosing again, now past this branch, for the unsaved-changes prompt
            return;
        }

        var dirty = vm.OpenTabs.Where(t => t.IsModified).ToList();
        if (dirty.Count == 0)
        {
            vm.SaveSettingsAndSession();
            vm.DisconnectC64U();
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
        vm.DisconnectC64U();
        _closeConfirmed = true;
        Close();
    }

    #endregion

    #region Private Methods


    private void AttachViewModel()
    {
        if (DataContext is not MainViewModel vm) return;

        vm.PropertyChanged += ViewModel_PropertyChanged;
        vm.DebugVariables.CollectionChanged += DebugVariables_CollectionChanged;
        vm.BreakpointStore.Breakpoints.CollectionChanged += BreakpointStore_CollectionChanged;
        foreach (var breakpoint in vm.BreakpointStore.Breakpoints)
            breakpoint.PropertyChanged += Breakpoint_PropertyChanged;
        vm.ErrorRaised += (title, message) => Dispatcher.UIThread.Post(async () => await MessageDialog.ShowAsync(this, title, message));
        vm.RefreshKeyboardLockStatus();
        vm.RecentFilesChanged += RefreshRecentFilesMenu;
        RefreshRecentFilesMenu();
        ApplyEditorSettings();
        _explorerWidth = vm.Settings.LeftPanelWidth > 60 ? vm.Settings.LeftPanelWidth : 230;
        ApplyExplorerLayout();
        ApplyVariableExplorerLayout();
        _rightPanelWidth = vm.Settings.RightPanelWidth > 60 ? vm.Settings.RightPanelWidth : 230;
        ApplyRightPanelLayout();
        BuildReferencePanels();
        vm.ApplyLanguageToRightPanel();
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
            case nameof(MainViewModel.ShowVariableExplorer):
                ApplyVariableExplorerLayout();
                break;
            case nameof(MainViewModel.IsRightPanelOpen):
                ApplyRightPanelLayout();
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
            case nameof(MainViewModel.IsUpperCaseModeActive):
                ApplyUpperCaseMode();
                if (ReferenceEquals(ViewModel.ActiveTab, ViewModel.DebugTab)) ViewModel.RefreshDebugVariablesDisplayMode();
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
        {
            _boundTab.PropertyChanged -= BoundTab_PropertyChanged;
            _boundTab.CaretOffset = _boundTab.IsHexMode ? HexEditor.SelectedOffset : Editor.CaretOffset;
        }

        _boundTab = tab;
        if (tab == null) return;

        tab.PropertyChanged += BoundTab_PropertyChanged;

        UninstallFolding();
        _findHighlightColorizer.Clear();
        Editor.Document = tab.Document;
        Editor.CaretOffset = Math.Min(tab.CaretOffset, tab.Document.TextLength);
        ApplyLanguageStyling(tab);
        InstallFolding(tab);
        _diagnosticsTimer.Stop();
        RunDiagnostics();
        if (FindBar.IsVisible) UpdateFindMatches();

        // A hex tab shows the hex editor where the text editor would be; its (empty) document
        // stays bound above so nothing else has to special-case it.
        bool hex = tab.IsHexMode;
        bool compare = tab.IsCompareMode;
        HexEditor.IsVisible = hex;
        CompareView.IsVisible = compare;
        Editor.IsVisible = !hex && !compare;
        ApplyDisassemblyMode(tab);
        if (hex)
        {
            if (FindBar.IsVisible) CloseFind();
            HexEditor.LoadBytes(tab.RawBytes!, tab.CaretOffset, tab.UndoStack);
        }
        else if (compare)
        {
            if (FindBar.IsVisible) CloseFind();
            CompareView.LoadResult(tab.CompareResult!, tab.CompareIsUnified, tab.CompareIgnoreWhitespace);
        }
        else if (tab.IsDisassemblyMode && tab.DisassemblyLineAddresses == null)
        {
            // A fresh disassembly tab: the address range is the first thing to fill in.
            Dispatcher.UIThread.Post(DisassemblyToolbar.FocusStartAddress, DispatcherPriority.Background);
        }
        else
        {
            Editor.Focus();
        }
    }

    private void BoundTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundTab == null) return;
        switch (e.PropertyName)
        {
            case nameof(EditorTab.Language) or nameof(EditorTab.Kind):
                ApplyLanguageStyling(_boundTab);
                break;
            case nameof(EditorTab.IsDisassemblyMode) or nameof(EditorTab.DisassemblyLineAddresses):
                ApplyDisassemblyMode(_boundTab);
                RefreshAsmGutter(_boundTab);
                break;
        }
    }

    // The gutter of an assembly tab shows the disassembly's addresses, else the assembled
    // addresses when the source has a fixed origin, else line numbers.
    private void RefreshAsmGutter(EditorTab tab) =>
        _asmLineNumberMargin.LineAddresses = tab.DisassemblyLineAddresses ?? tab.AssembledLineAddresses;

    private void ApplyDisassemblyMode(EditorTab tab)
    {
        Editor.IsReadOnly = tab.IsDisassemblyMode;
        DisassemblyToolbar.IsVisible = tab.IsDisassemblyMode;
    }

    private void ApplyLanguageStyling(EditorTab tab)
    {
        var transformers = Editor.TextArea.TextView.LineTransformers;
        transformers.Clear();

        bool isAsm = tab.Language == EditorLanguage.Asm;
        var margins = Editor.TextArea.LeftMargins;
        if (!isAsm && !margins.Contains(_breakpointMargin)) margins.Insert(0, _breakpointMargin);
        if (isAsm) margins.Remove(_breakpointMargin);
        if (isAsm && !margins.Contains(_asmLineNumberMargin)) margins.Insert(0, _asmLineNumberMargin);
        if (!isAsm) margins.Remove(_asmLineNumberMargin);
        RefreshAsmGutter(tab);
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

        // BASIC (.bas and .prg alike) always uses the PETSCII font with glyph substitution on,
        // since either can contain real PETSCII control/graphics characters; assembly always
        // uses the plain monospace font with substitution off, since assembly/plain source must
        // never be reinterpreted as PETSCII bytes.
        Editor.FontFamily = isAsm ? _asciiFont : _petsciiFont;
        _petsciiGlyphGenerator.IsAsmMode = isAsm;
        _tabStopGenerator.IsEnabled = isAsm;
        ApplyEditorSettings();
        Editor.TextArea.TextView.Redraw();
        ViewModel.ApplyLanguageToRightPanel();
    }

    // Applies the active tab's Upper Active/Inactive mode to the shared glyph generator and
    // redraws - a pure re-render, since the mode only changes which glyph a byte displays as,
    // never the byte itself (see PetsciiGlyphGenerator.IsUpperCaseModeActive). Reached via
    // ViewModel.IsUpperCaseModeActive's PropertyChanged, which fires whenever the active tab
    // changes or the active tab's own mode is toggled - see ShiftModeIndicator_Click.
    private void ApplyUpperCaseMode()
    {
        _petsciiGlyphGenerator.IsUpperCaseModeActive = ViewModel.IsUpperCaseModeActive;
        Editor.TextArea.TextView.Redraw();
    }

    // Hands the editor's colorizers and renderers the active theme's brushes, under the same
    // keys the WPF app's ApplyEditorAppearance reads. The brushes are the theme's own shared
    // instances (see AppTheme.Apply), so a later theme switch recolors the colorizers in place;
    // the two renderers keep a pen built from the color instead, which is why this is also
    // re-run from OnThemeChanged.
    private void ApplyThemeBrushes()
    {
        _lineNumberColorizer.LineNumberBrush = ThemeBrush("ThemeEditorLineNumberFg");
        _asmLineNumberMargin.TextBrush = ThemeBrush("ThemeEditorLineNumberFg");
        _lineNumberColorizer.ActiveLineNumberBrush = ThemeBrush("ThemeEditorFg");
        _keywordColorizer.KeywordBrush = ThemeBrush("ThemeEditorKeywordFg");
        _numberLiteralColorizer.NumberBrush = ThemeBrush("ThemeEditorNumberLiteralFg");
        _stringLiteralColorizer.StringBrush = ThemeBrush("ThemeEditorStringFg");
        _remCommentColorizer.CommentBrush = ThemeBrush("ThemeEditorCommentFg");
        _asmMnemonicColorizer.MnemonicBrush = ThemeBrush("ThemeEditorKeywordFg");
        _asmNumberLiteralColorizer.NumberBrush = ThemeBrush("ThemeEditorNumberLiteralFg");
        _asmLabelColorizer.LabelBrush = ThemeBrush("ThemeEditorStringFg");
        _asmCommentColorizer.CommentBrush = ThemeBrush("ThemeEditorCommentFg");
        _findHighlightColorizer.MatchBrush = ThemeBrush("ThemeFindMatchBg");
        _findHighlightColorizer.MatchFgBrush = ThemeBrush("ThemeFindMatchFg");
        _findHighlightColorizer.CurrentMatchBrush = ThemeBrush("ThemeFindCurrentBg");
        _findHighlightColorizer.CurrentMatchFgBrush = ThemeBrush("ThemeFindCurrentFg");

        _errorSquiggleRenderer.SetColor(ThemeColor("ThemeEditorErrorSquiggle"));
        _currentLineBorderRenderer.SetColor(ThemeColor("ThemeEditorCurrentLineBorder"));
        // The guide is a full-height line beside the text, so it is drawn at a quarter opacity
        // rather than the theme's solid color - which is what the WPF ruler amounts to as well.
        var guide = ThemeColor("ThemeEditorGuideLineFg");
        _columnGuideRenderer.SetColor(new Color(0x40, guide.R, guide.G, guide.B));
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyThemeBrushes();
        UpdateStatusColors();
        Editor.TextArea.TextView.Redraw();
    }

    private IBrush ThemeBrush(string key) => (IBrush)this.FindResource(key)!;
    private Color ThemeColor(string key) => ((ISolidColorBrush)ThemeBrush(key)).Color;

    // Applies every setting that affects the editor control itself; safe to call repeatedly.
    private void ApplyEditorSettings()
    {
        var settings = ViewModel.Settings;
        Editor.FontSize = Math.Clamp(settings.EditorFontSize, 6, 72);
        HexEditor.HexFontSize = Editor.FontSize;
        CompareView.EditorFontSize = Editor.FontSize;
        _asmLineNumberMargin.FontSize = Editor.FontSize;
        _asmLineNumberMargin.ZeroPadWidth = settings.LineNumberPadding;
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
        if (DataContext is not MainViewModel vm) return;
        (StatusBar.Background, StatusTextBlock.Foreground) = vm.StatusType switch
        {
            StatusType.Error => (ThemeBrush("ThemeStatusErrorBg"), ThemeBrush("ThemeStatusErrorFg")),
            StatusType.Warning => (ThemeBrush("ThemeStatusWarningBg"), ThemeBrush("ThemeStatusWarningFg")),
            _ => (ThemeBrush("ThemeStatusBarBg"), ThemeBrush("ThemeStatusBarFg")),
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
    // Column 0 is the activity bar, which never collapses; 1 is the panel and 2 its splitter.
    private void ApplyExplorerLayout()
    {
        var column = MainGrid.ColumnDefinitions[1];
        if (ViewModel.IsExplorerOpen)
        {
            column.Width = new GridLength(_explorerWidth);
            column.MinWidth = 120;
            MainGrid.ColumnDefinitions[2].Width = new GridLength(4);
        }
        else
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0) _explorerWidth = column.Width.Value;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
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
        if (tab != null) RefreshAsmGutter(tab);
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

    private async void DebugPause_Click(object? sender, EventArgs e) => await ViewModel.DebugPauseAsync();
    private async void DebugRestart_Click(object? sender, EventArgs e) => await ViewModel.DebugRestartAsync();
    private async void DebugStop_Click(object? sender, EventArgs e) => await ViewModel.DebugStopAsync();
    private async void DebugStepOver_Click(object? sender, EventArgs e) => await ViewModel.DebugStepOverAsync();
    private async void DebugStepInto_Click(object? sender, EventArgs e) => await ViewModel.DebugStepIntoAsync();
    private async void DebugStepOut_Click(object? sender, EventArgs e) => await ViewModel.DebugStepOutAsync();

    private async void DebugRunToCursor_Click(object? sender, EventArgs e)
    {
        if (!TryGetBasicLineAtDocumentLine(Editor.TextArea.Caret.Line, out ushort basicLine))
        {
            ViewModel.SetStatus("Run to Cursor requires the caret to be on a line with code.", StatusType.Error);
            return;
        }
        await ViewModel.RunToLineAsync(basicLine);
    }

    private async void DebugToggleBreakpoint_Click(object? sender, EventArgs e) => await ToggleBreakpointAtDocumentLineAsync(Editor.TextArea.Caret.Line);

    private void DebugToggleBreakpointEnabled_Click(object? sender, EventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab && TryGetBasicLineAtDocumentLine(Editor.TextArea.Caret.Line, out ushort basicLine))
            ViewModel.ToggleBreakpointEnabled(tab, basicLine);
    }

    private async void DebugDeleteAllBreakpoints_Click(object? sender, EventArgs e)
    {
        await ViewModel.DeleteAllBreakpointsAsync();
        RefreshBreakpointMargin();
    }

    private void ViewDebugPanel_Click(object? sender, EventArgs e)
    {
        if (ViewModel.IsBottomPanelOpen && ViewModel.BottomPanelTabIndex == 1) { ViewModel.IsBottomPanelOpen = false; return; }
        ViewModel.IsBottomPanelOpen = true;
        ViewModel.BottomPanelTabIndex = 1;
    }

    private void ViewBottomPanelClose_Click(object? sender, RoutedEventArgs e) => ViewModel.IsBottomPanelOpen = false;

    private async void VariablesList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double-click on an array toggles it (the tree does that); on a value, edits it.
        if (VariablesList.SelectedItem is DebugVariableNode { IsArray: false })
            await EditSelectedVariableAsync();
    }

    // An array's elements are read from the machine the first time it is expanded.
    private void DebugVariables_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (DebugVariableNode node in e.NewItems) node.PropertyChanged += DebugVariableNode_PropertyChanged;
        if (e.OldItems != null)
            foreach (DebugVariableNode node in e.OldItems) node.PropertyChanged -= DebugVariableNode_PropertyChanged;
    }

    private async void DebugVariableNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebugVariableNode.IsExpanded) && sender is DebugVariableNode { IsArray: true, IsExpanded: true } node)
            await ViewModel.LoadArrayChildrenAsync(node);
    }

    private async void VariablesList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await EditSelectedVariableAsync(); }
    }

    // The value as the C64 would show it in the debugged tab's Upper/Lower Case Mode, edited
    // the same way: what's typed is taken as display text, so in Lower Case Mode the ASCII
    // case is swapped back to the bytes the C64 stores.
    private async Task EditSelectedVariableAsync()
    {
        if (VariablesList.SelectedItem is not DebugVariableNode { IsArray: false, Variable: { } variable }) return;
        if (!ViewModel.IsDebugStopped)
        {
            ViewModel.SetStatus($"Can't update {variable.Name} while running - pause first.", StatusType.Error);
            return;
        }

        bool isUpperCaseModeActive = ViewModel.DebugTab?.IsUpperCaseModeActive ?? true;
        string current = variable is { Type: BasicVariableType.String, Value: ResolvedStringValue resolved }
            ? PetsciiScreenCodeMap.ToDisplayText(resolved.Text, isUpperCaseModeActive)
            : MainViewModel.FormatVariableValue(variable);
        string? text = await TextPromptDialog.ShowAsync(this, "Set Variable", $"New value for {variable.Name}:", current);
        if (text == null) return;

        if (variable.Type == BasicVariableType.String && !isUpperCaseModeActive)
            text = SwapAsciiLetterCase(text);
        await ViewModel.SetVariableAsync(variable, text);
    }

    private static string SwapAsciiLetterCase(string text)
    {
        char[] chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsAsciiLetter(chars[i]))
                chars[i] = char.IsAsciiLetterUpper(chars[i]) ? char.ToLowerInvariant(chars[i]) : char.ToUpperInvariant(chars[i]);
        return new string(chars);
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

    // Selects the exact text the diagnostic's squiggle underlines, not just the start of the
    // line, and scrolls it into view - as WPF since 2.5.0.
    internal void JumpToProblem(ErrorListRow row)
    {
        ViewModel.ActiveTab = row.Tab;
        int offset = Math.Min(row.Offset, Editor.Document.TextLength);
        int length = Math.Clamp(row.Length, 0, Editor.Document.TextLength - offset);
        Editor.Select(offset, length);
        RevealCaretLine();
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
        // Select() moved the caret; a span that is a whole keyword would otherwise show a suggestion.
        ClearGhostText();
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

    private void ViewProblems_Click(object? sender, EventArgs e)
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

        var column = MainGrid.ColumnDefinitions[1];
        if (vm.IsExplorerOpen && column.Width.IsAbsolute && column.Width.Value > 0)
            _explorerWidth = column.Width.Value;
        vm.Settings.LeftPanelWidth = _explorerWidth;
        PersistFolderTreeHeight();

        var rightColumn = MainGrid.ColumnDefinitions[5];
        if (vm.IsRightPanelOpen && rightColumn.Width.IsAbsolute && rightColumn.Width.Value > 0)
            _rightPanelWidth = rightColumn.Width.Value;
        vm.Settings.RightPanelWidth = _rightPanelWidth;
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

    private void OpenTreeItem(FileTreeItem item, bool forceHex = false)
    {
        if (item.IsFolder || (item.IsDiskImage && !forceHex))
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        if (item.IsVirtual)
            ViewModel.OpenVirtualEntry(item, forceHex);
        else if (item.FullPath.Length > 0)
            ViewModel.OpenFile(item.FullPath, forceHex);
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

    // "Select file for comparison" on any comparable file; "Compare file" once one is pending,
    // disabled (with the reason) when the clicked file can't be compared with it.
    private void AddCompareItems(ItemCollection items, ComparableFileRef file)
    {
        if (!CompareFileResolver.IsComparableKind(file.Kind)) return;

        items.Add(new Separator());
        var pending = ViewModel.PendingCompareFile;
        items.Add(new MenuItem
        {
            Header = file.IsSameFile(pending) ? "Clear comparison selection" : "Select file for comparison",
            Command = new AsyncCommand(() => { ViewModel.SelectFileForCompare(file); return Task.CompletedTask; }),
        });
        if (pending != null && !file.IsSameFile(pending))
        {
            bool canCompare = ViewModel.CanCompareWithPending(file);
            items.Add(new MenuItem
            {
                Header = $"Compare with {pending.Name}",
                IsEnabled = canCompare,
                Command = new AsyncCommand(async () => await ViewModel.CompareWithPendingAsync(file)),
            });
        }
    }

    /// <summary>The headers of the explorer context menu an item would get, for tests.</summary>
    internal IReadOnlyList<string> ContextMenuHeadersFor(FileTreeItem item) =>
        BuildTreeContextMenu(item).Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToList();

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
            Add("Cut", () => CutOrCopyItemAsync(item, cut: true));
            Add("Copy", () => CutOrCopyItemAsync(item, cut: false));
            items.Add(MakePasteItem("Paste", item.FullPath));
            Sep();
            AddSync("Refresh", item.RefreshChildren);
            Sep();
        }
        else if (item.IsDiskImage)
        {
            Add("Add File…", () => AddFileToLocalDiskImageAsync(item));
            AddSync("Open in Hex editor", () => OpenTreeItem(item, forceHex: true));
            Sep();
            Add("Cut", () => CutOrCopyItemAsync(item, cut: true));
            Add("Copy", () => CutOrCopyItemAsync(item, cut: false));
            Sep();
            AddSync("Refresh", item.RefreshChildren);
            Sep();
        }
        else
        {
            if (item.IsOpenable)
                AddSync(item.Kind == C64UFileKind.Asm ? "Open in Assembly editor" : "Open in BASIC editor", () => OpenTreeItem(item));
            AddSync("Open in Hex editor", () => OpenTreeItem(item, forceHex: true));
            if (item.Kind == C64UFileKind.Ml)
                AddSync("Disassemble file", () => ViewModel.DisassembleFile(item));
            AddCompareItems(items, ComparableFileRef.FromLocal(item));
            if (item.Kind is C64UFileKind.Prg or C64UFileKind.Ml or C64UFileKind.Asm or C64UFileKind.Bas)
            {
                Add("Run on VICE", () => ViewModel.SendFileToViceAsync(item, run: true));
                Add("Load on VICE", () => ViewModel.SendFileToViceAsync(item, run: false));
                Add("Run on C64U", () => ViewModel.SendFileToC64UAsync(item, run: true));
                Add("Load on C64U", () => ViewModel.SendFileToC64UAsync(item, run: false));
            }
            Sep();
        }

        if (!item.IsVirtual && !item.IsFolder && !item.IsDiskImage)
        {
            Add("Cut", () => CutOrCopyItemAsync(item, cut: true));
            Add("Copy", () => CutOrCopyItemAsync(item, cut: false));
            if (Path.GetDirectoryName(item.FullPath) is { } parent)
                items.Add(MakePasteItem("Paste", parent));
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

    // ── C64U explorer ─────────────────────────────────────────────────────────

    private C64UFileItem? SelectedC64UTreeItem => C64UFileTree.SelectedItem as C64UFileItem;

    // The folder a C64U explorer "new" action should target: the selected folder, the selected
    // file's folder, or the root when nothing is selected. Mirrors ExplorerTargetFolder above,
    // but works in remote path strings rather than FileTreeItem parent lookups.
    private (string ParentPath, C64UFileItem? Folder) C64UExplorerTargetFolder()
    {
        var selected = SelectedC64UTreeItem;
        if (selected == null || selected.IsVirtual) return ("/", null);
        if (selected.IsFolder) return (selected.FullPath, selected);

        string trimmed = selected.FullPath.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string parentPath = slash <= 0 ? "/" : trimmed[..slash];
        return (parentPath, ViewModel.FindC64UItemByPath(parentPath));
    }

    private async Task OpenC64UTreeItemAsync(C64UFileItem item)
    {
        if (item.IsFolder || item.IsDiskImage)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        await ViewModel.OpenC64UItemAsync(item);
    }

    private async Task NewC64UFolderAsync()
    {
        if (!ViewModel.IsC64UConnected) return;
        var (parentPath, folder) = C64UExplorerTargetFolder();
        string? name = await TextPromptDialog.ShowAsync(this, "New Folder", "Folder name:");
        if (name == null) return;
        if (folder != null) folder.IsExpanded = true;
        await ViewModel.CreateC64UFolderAsync(parentPath, name);
    }

    private async Task NewC64UDiskImageAsync(C64UFileKind kind)
    {
        if (!ViewModel.IsC64UConnected) return;
        var (parentPath, folder) = C64UExplorerTargetFolder();
        string extension = kind == C64UFileKind.D64 ? ".d64" : ".d81";
        string? name = await TextPromptDialog.ShowAsync(this, kind == C64UFileKind.D64 ? "New .d64 Disk Image" : "New .d81 Disk Image", "Disk name:", "disk" + extension);
        if (name == null) return;
        if (folder != null) folder.IsExpanded = true;
        await ViewModel.CreateC64UDiskImageAsync(parentPath, name, kind);
    }

    private async Task RenameC64UItemAsync(C64UFileItem item)
    {
        string? name = await TextPromptDialog.ShowAsync(this, "Rename", $"New name for \"{item.Name}\":", item.Name);
        if (name == null || name == item.Name) return;
        await ViewModel.RenameC64UItemAsync(item, name);
    }

    private async Task DeleteC64UItemAsync(C64UFileItem item)
    {
        string what = item.IsFolder ? $"folder \"{item.Name}\" and all its contents" : $"\"{item.Name}\"";
        string? choice = await MessageDialog.ShowAsync(this, "Delete", $"Permanently delete {what} from the C64 Ultimate?", "Delete", "Cancel");
        if (choice != "Delete") return;
        await ViewModel.DeleteC64UItemAsync(item);
    }

    private async Task DownloadC64UFileAsync(C64UFileItem item)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download File",
            SuggestedFileName = item.Name,
            SuggestedStartLocation = await SuggestedFolderAsync(),
        });

        string? path = file?.TryGetLocalPath();
        if (path == null) return;

        try
        {
            byte[] bytes = item.Content ?? (ViewModel.C64UFtp != null
                ? await ViewModel.C64UFtp.DownloadBytesAsync(item.FullPath)
                : throw new InvalidOperationException("Not connected to the C64 Ultimate."));
            await File.WriteAllBytesAsync(path, bytes);
            ViewModel.SetStatus($"Downloaded {item.Name}.");
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Download", $"Could not download \"{item.Name}\": {ex.Message}");
        }
    }

    private async Task AddFileToLocalDiskImageAsync(FileTreeItem diskItem)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add File to Disk Image",
            AllowMultiple = true,
            SuggestedStartLocation = await SuggestedFolderAsync(),
            FileTypeFilter = [_c64Files, FilePickerFileTypes.All],
        });

        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path)
                ViewModel.AddFileToLocalDiskImage(path, diskItem);
    }

    private async Task AddFileToC64UDiskImageAsync(C64UFileItem diskItem)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add File to Disk Image",
            AllowMultiple = false,
            SuggestedStartLocation = await SuggestedFolderAsync(),
            FileTypeFilter = [_c64Files, FilePickerFileTypes.All],
        });

        if (files.Count != 1 || files[0].TryGetLocalPath() is not { } path) return;

        byte[] raw = await File.ReadAllBytesAsync(path);
        var kind = FileClassifier.Classify(path, isFolder: false, () => raw);
        await ViewModel.AddFileToC64UDiskImageAsync(diskItem, Path.GetFileName(path), raw, kind);
    }

    private ContextMenu BuildC64UTreeContextMenu(C64UFileItem item)
    {
        var menu = new ContextMenu();
        var items = menu.Items;

        void Add(string header, Func<Task> action) =>
            items.Add(new MenuItem { Header = header, Command = new AsyncCommand(action) });
        void AddSync(string header, Action action) => Add(header, () => { action(); return Task.CompletedTask; });
        void Sep() => items.Add(new Separator());

        if (item.IsFolder)
        {
            Add("New Folder…", async () => { C64UFileTree.SelectedItem = item; await NewC64UFolderAsync(); });
            Add("New .d64 Disk Image…", async () => { C64UFileTree.SelectedItem = item; await NewC64UDiskImageAsync(C64UFileKind.D64); });
            Add("New .d81 Disk Image…", async () => { C64UFileTree.SelectedItem = item; await NewC64UDiskImageAsync(C64UFileKind.D81); });
            Sep();
            Add("Refresh", item.RefreshChildrenAsync);
            Sep();
        }
        else if (item.IsDiskImage)
        {
            Add("Add File…", () => AddFileToC64UDiskImageAsync(item));
            Add("Mount to Drive A", () => ViewModel.MountC64UDriveAsync("a", item.FullPath));
            Add("Mount to Drive B", () => ViewModel.MountC64UDriveAsync("b", item.FullPath));
            Sep();
            Add("Download…", () => DownloadC64UFileAsync(item));
            Add("Refresh", item.RefreshChildrenAsync);
            Sep();
        }
        else
        {
            if (item.IsOpenable)
                AddSync(item.Kind == C64UFileKind.Asm ? "Open in Assembly editor" : "Open in BASIC editor", () => OpenC64UTreeItem(item));
            Add("Open in Hex editor", () => ViewModel.OpenC64UItemAsync(item, forceHex: true));
            if (item.Kind == C64UFileKind.Ml)
                Add("Disassemble file", () => ViewModel.DisassembleC64UFileAsync(item));
            AddCompareItems(items, ComparableFileRef.FromC64U(item));
            if (item.Kind is C64UFileKind.Prg or C64UFileKind.Ml or C64UFileKind.Asm or C64UFileKind.Bas)
            {
                Add("Run on C64U", () => ViewModel.SendC64UItemAsync(item, run: true));
                Add("Load on C64U", () => ViewModel.SendC64UItemAsync(item, run: false));
                Add("Run on VICE", () => SendC64UTreeItemToViceAsync(item, run: true));
                Add("Load on VICE", () => SendC64UTreeItemToViceAsync(item, run: false));
            }
            if (!item.IsVirtual)
            {
                Sep();
                Add("Download…", () => DownloadC64UFileAsync(item));
            }
            Sep();
        }

        Add("Rename…", () => RenameC64UItemAsync(item));
        Add("Delete", () => DeleteC64UItemAsync(item));
        return menu;
    }

    private void OpenC64UTreeItem(C64UFileItem item) => _ = OpenC64UTreeItemAsync(item);

    // Sends an item from the C64U tree to VICE instead: downloads it first (if not already in
    // memory as a disk-image entry), same as SendC64UItemAsync does for its own target.
    private async Task SendC64UTreeItemToViceAsync(C64UFileItem item, bool run)
    {
        if (item.Content == null && ViewModel.C64UFtp == null) return;

        try
        {
            byte[] bytes = item.Content ?? await ViewModel.C64UFtp!.DownloadBytesAsync(item.FullPath);
            var localItem = new FileTreeItem(item.Name, bytes, item.Kind, item.FullPath);
            await ViewModel.SendFileToViceAsync(localItem, run);
        }
        catch (Exception ex)
        {
            ViewModel.SetStatus($"Error downloading file: {ex.Message}", StatusType.Error);
        }
    }

    // Activity bar: clicking a tab's icon shows that tab, opening the panel if it was collapsed;
    // clicking the icon of the tab already showing collapses the panel - the WPF app's behavior.
    private void ActivityExplorer_Click(object? sender, RoutedEventArgs e) => ActivateLeftPanel("Explorer");

    // Deliberately does not auto-connect: matches the WPF app, which leaves the "Not connected"
    // state until the user clicks Connect, even with a URL already configured.
    private void ActivityC64U_Click(object? sender, RoutedEventArgs e) => ActivateLeftPanel("C64U");

    private void ActivitySearch_Click(object? sender, RoutedEventArgs e) => ActivateLeftPanel("Search");

    private void ActivateLeftPanel(string tab)
    {
        if (ViewModel.IsExplorerOpen && ViewModel.ActiveLeftPanelTab == tab)
        {
            ViewModel.IsExplorerOpen = false;
            return;
        }
        ViewModel.ActiveLeftPanelTab = tab;
        ViewModel.IsExplorerOpen = true;
    }

    private async void C64UConnect_Click(object? sender, RoutedEventArgs e) => await ViewModel.ConnectToC64UAsync();
    private async void C64URefresh_Click(object? sender, RoutedEventArgs e) => await ViewModel.RefreshC64UFolderAsync();
    private async void C64UNewFolder_Click(object? sender, RoutedEventArgs e) => await NewC64UFolderAsync();
    private async void C64UEjectA_Click(object? sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("a");
    private async void C64UEjectB_Click(object? sender, RoutedEventArgs e) => await ViewModel.EjectC64UDriveAsync("b");

    private async void C64UUpload_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsC64UConnected) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Upload to C64 Ultimate",
            AllowMultiple = true,
            SuggestedStartLocation = await SuggestedFolderAsync(),
        });

        var (parentPath, _) = C64UExplorerTargetFolder();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            byte[] data = await File.ReadAllBytesAsync(path);
            await ViewModel.UploadFileToC64UAsync(parentPath, Path.GetFileName(path), data);
        }
    }

    private void C64UFileTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not C64UFileItem item) return;
        if (item.IsFolder || item.IsDiskImage) return; // TreeView already toggles expansion
        _ = OpenC64UTreeItemAsync(item);
        e.Handled = true;
    }

    private async void C64UFileTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (SelectedC64UTreeItem is not { } item) return;

        switch (e.Key)
        {
            case Key.Enter:
                await OpenC64UTreeItemAsync(item);
                e.Handled = true;
                break;
            case Key.F2:
                e.Handled = true;
                await RenameC64UItemAsync(item);
                break;
            case Key.Delete:
            case Key.Back when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                e.Handled = true;
                await DeleteC64UItemAsync(item);
                break;
        }
    }

    private void C64UFileTree_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not C64UFileItem item || item.Name.Length == 0) return;

        C64UFileTree.SelectedItem = item;
        var container = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        BuildC64UTreeContextMenu(item).Open(container ?? (Control)C64UFileTree);
        e.Handled = true;
    }

    // Saves a tab to its existing path, or prompts for one when it has none (or when forced).
    private async Task<bool> SaveTabAsync(EditorTab tab, bool forceDialog)
    {
        if (tab.IsCompareMode)
        {
            ViewModel.SetStatus("A comparison can't be saved.", StatusType.Warning);
            return false;
        }

        if (tab.IsVirtual && !forceDialog)
        {
            bool savedVirtual = tab.IsC64UVirtual ? await ViewModel.SaveC64UVirtualTabAsync(tab) : ViewModel.SaveVirtualTab(tab);
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

    private void FileNew_Click(object? sender, EventArgs e) => ViewModel.NewTab(EditorLanguage.Basic);

    private void FileNewBas_Click(object? sender, EventArgs e) => ViewModel.NewTab(EditorLanguage.Basic, C64UFileKind.Bas);

    private void FileNewAsm_Click(object? sender, EventArgs e) => ViewModel.NewTab(EditorLanguage.Asm);

    private async void FileOpen_Click(object? sender, EventArgs e)
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

    private async void FileSave_Click(object? sender, EventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab)
            await SaveTabAsync(tab, forceDialog: false);
    }

    private async void FileSaveAs_Click(object? sender, EventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab)
            await SaveTabAsync(tab, forceDialog: true);
    }

    private async void FileClose_Click(object? sender, EventArgs e)
    {
        if (ViewModel.ActiveTab is { } tab) await CloseTabWithPromptAsync(tab);
    }

    // The tab strip's per-tab close button; its DataContext is the tab, active or not.
    private async void TabClose_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is EditorTab tab) await CloseTabWithPromptAsync(tab);
    }

    // Closes a tab, offering to save it first if it has unsaved changes. Returns false if the
    // user cancelled.
    private async Task<bool> CloseTabWithPromptAsync(EditorTab tab)
    {
        if (tab.IsModified)
        {
            ViewModel.ActiveTab = tab; // so the user can see what they're being asked about
            string? choice = await MessageDialog.ShowAsync(this, "Unsaved Changes",
                $"Save changes to {tab.FileName}?", "Save", "Don't Save", "Cancel");
            if (choice == "Cancel" || choice == null) return false;
            if (choice == "Save" && !await SaveTabAsync(tab, forceDialog: false)) return false;
        }

        if (ReferenceEquals(tab, ViewModel.ActiveTab))
            tab.CaretOffset = tab.IsHexMode ? HexEditor.SelectedOffset : Editor.CaretOffset;
        ViewModel.CloseTab(tab, rememberForReopen: true);
        return true;
    }

    private void FileReopenClosedTab_Click(object? sender, EventArgs e) => ViewModel.ReopenClosedTab();

    private async void FileExport_Click(object? sender, EventArgs e) => await ExportTextAsync();

    private async void FileImport_Click(object? sender, EventArgs e) => await ImportTextAsync();

    private void FileExit_Click(object? sender, EventArgs e) => Close();

    private async void FileOpenFolder_Click(object? sender, EventArgs e) => await OpenFolderAsync();

    // The explorer's empty state offers the same action as a button, whose Click carries
    // RoutedEventArgs rather than the native menu's plain EventArgs.
    private async void ExplorerOpenFolder_Click(object? sender, RoutedEventArgs e) => await OpenFolderAsync();

    private async Task OpenFolderAsync()
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

    private void FileCloseFolder_Click(object? sender, EventArgs e) => ViewModel.CloseFolder();

    private void ViewExplorer_Click(object? sender, EventArgs e) => ViewModel.IsExplorerOpen = !ViewModel.IsExplorerOpen;
    private void ViewSecondarySideBar_Click(object? sender, EventArgs e) => ViewModel.ToggleSecondarySideBar();
    private void ViewColumnGuide_Click(object? sender, EventArgs e) => ViewModel.ShowColumnGuide = !ViewModel.ShowColumnGuide;
    private void ViewWordWrap_Click(object? sender, EventArgs e) => ViewModel.WordWrap = !ViewModel.WordWrap;
    private void ViewStatusBar_Click(object? sender, EventArgs e) => ViewModel.ShowStatusBar = !ViewModel.ShowStatusBar;

    // A no-op on macOS/Linux (see KeyboardLockKeys.ToggleCapsLock) - the click still refreshes the
    // indicator so a real toggle made via the physical key registers immediately.
    private void ToggleCapsLock()
    {
        KeyboardLockKeys.ToggleCapsLock();
        ViewModel.RefreshKeyboardLockStatus();
    }

    private void CapsLockIndicator_Click(object? sender, RoutedEventArgs e) => ToggleCapsLock();

    // Setting ViewModel.IsUpperCaseModeActive here is the only work needed - it writes through to
    // ActiveTab.IsUpperCaseModeActive and calls RefreshShiftModeStatus itself, which updates
    // ViewModel.IsUpperCaseModeActive's own backing field and raises its PropertyChanged, which
    // ViewModel_PropertyChanged turns into ApplyUpperCaseMode(). The Edit menu's "Lower Case
    // Mode" checkbox drives the exact same property (inverted) via IsLowerCaseModeActive. Two
    // thin overloads because Avalonia's compiled bindings need an exact delegate match: Button's
    // Click wants EventHandler&lt;RoutedEventArgs&gt;, NativeMenuItem's wants plain EventHandler.
    private void ToggleUpperCaseMode() => ViewModel.IsUpperCaseModeActive = !ViewModel.IsUpperCaseModeActive;
    private void ShiftModeIndicator_Click(object? sender, RoutedEventArgs e) => ToggleUpperCaseMode();
    private void EditLowerCaseMode_Click(object? sender, EventArgs e) => ToggleUpperCaseMode();

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
        // from the event source so the menu always matches the item under the pointer. Off any
        // row, the menu is the root folder's.
        if ((e.Source as Control)?.DataContext is not FileTreeItem item || item.Name.Length == 0)
        {
            if (!ViewModel.IsFolderOpen) return;
            var rootMenu = new ContextMenu();
            rootMenu.Items.Add(new MenuItem { Header = "New File…", Command = new AsyncCommand(async () => { FileTree.SelectedItem = null; await NewFileAsync(); }) });
            rootMenu.Items.Add(new MenuItem { Header = "New Folder…", Command = new AsyncCommand(async () => { FileTree.SelectedItem = null; await NewFolderAsync(); }) });
            rootMenu.Items.Add(new Separator());
            rootMenu.Items.Add(MakePasteItem("Paste", ViewModel.RootFolderPath));
            rootMenu.Open(FileTree);
            e.Handled = true;
            return;
        }

        FileTree.SelectedItem = item;
        var container = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        BuildTreeContextMenu(item).Open(container ?? (Control)FileTree);
        e.Handled = true;
    }

    // ── Find / replace ────────────────────────────────────────────────────────

    private void OpenFind(bool replaceMode)
    {
        if (IsHexTabActive || IsCompareTabActive) return;
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

        // Selecting the match moved the caret, and a match that happens to be a whole keyword
        // ("SIN") would otherwise show a suggestion ("()") - noise when the user is finding,
        // not typing.
        ClearGhostText();
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

    private void EditFind_Click(object? sender, EventArgs e) => OpenFind(replaceMode: false);
    private void EditReplace_Click(object? sender, EventArgs e) => OpenFind(replaceMode: true);
    private void EditFindNext_Click(object? sender, EventArgs e) => FindNext();
    private void EditFindPrevious_Click(object? sender, EventArgs e) => FindPrev();

    // ── Editor input ──────────────────────────────────────────────────────────

    // C64 BASIC is upper case by default - force typed text to match, the way an unshifted key
    // on a real C64 produces an upper-case letter. A shifted letter right after an unshifted
    // keyword prefix (e.g. g then shift-O for GOTO) is the C64's keyword abbreviation and is
    // stored as the PETSCII graphic character the machine would show. Assembly source case is
    // significant (labels, comments), so it's left alone.
    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm || string.IsNullOrEmpty(e.Text)) return;

        string insertText = TryGetKeywordAbbreviationGlyph(e.Text) ?? ApplyC64Shift(e.Text);
        if (insertText == e.Text) return;

        e.Handled = true;
        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, insertText);
        int caret = start + insertText.Length;
        Editor.CaretOffset = caret;
        Editor.Select(caret, 0);

        // e.Handled = true above suppresses AvaloniaEdit's own TextInput handling entirely, which
        // is what normally scrolls the caret into view after inserting typed text - without this,
        // typing past the right edge with word wrap off never auto-scrolls horizontally.
        Editor.TextArea.Caret.BringCaretToView();
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

    // Real C64 keyboard behavior for a letter key: unshifted (no Shift, Caps Lock off) produces
    // the normal uppercase-looking glyph (this app's default); shifted (Shift held, or Caps
    // Lock on - a real C64's Shift Lock physically latches Shift down, unlike a PC's Caps Lock,
    // so either one alone is enough, and there's no cancel-out when both are active at once)
    // produces the C64 graphic character occupying that key's shifted position. Internally that
    // graphic glyph is just the letter's lower case ASCII byte - PetsciiGlyphGenerator already
    // renders it as the correct C64 ROM glyph, so no separate PETSCII mapping is needed here.
    // Non-letters (digits, punctuation) are unaffected either way.
    private string ApplyC64Shift(string text)
    {
        if (text.Length != 1 || !char.IsAsciiLetter(text[0])) return text.ToUpperInvariant();

        bool shifted = _lastKeyModifiers.HasFlag(KeyModifiers.Shift) || ViewModel.IsCapsLockOn;
        return shifted ? char.ToLowerInvariant(text[0]).ToString() : char.ToUpperInvariant(text[0]).ToString();
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
        // Cached for Editor_TextEntering, which fires right after for the same keystroke but
        // carries no modifier state of its own - see _lastKeyModifiers. Also catches a Caps Lock
        // press made via the physical key while typing here (the Activated handler in the
        // constructor covers a toggle made while some other window had focus).
        _lastKeyModifiers = e.KeyModifiers;
        ViewModel.RefreshKeyboardLockStatus();

        if (HandleCompletionKey(e))
        {
            // Enter/Tab with the popup open are left unhandled here so the popup's own handler,
            // later in the tunnel, accepts the selection; only the line-numbering below is skipped.
            if (!IsCompletionPopupOpen) e.Handled = true;
            return;
        }

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

        // Enter at or before the line's own number means "a new line above this one": split the
        // gap between the previous line's number (or 0) and this one, rather than continuing
        // forward from it and colliding with this line's content. (Upstream 306492e.)
        int enterCol = Editor.CaretOffset - line.Offset;
        if (enterCol <= match.Groups[2].Index)
        {
            int previousNumber = 0;
            bool hasPreviousNumber = false;
            if (line.PreviousLine is { } prevDocLine)
            {
                Match prevMatch = _leadingLineNumberPattern.Match(document.GetText(prevDocLine));
                if (prevMatch.Success && int.TryParse(prevMatch.Groups[2].Value, out int prevNumber))
                {
                    previousNumber = prevNumber;
                    hasPreviousNumber = true;
                }
            }

            // 0 is a valid line number, so with no real previous line the only floor is this
            // line's own number.
            int aboveMidpoint = (previousNumber + currentNumber) / 2;
            bool noRoom = aboveMidpoint >= currentNumber || (hasPreviousNumber && aboveMidpoint <= previousNumber);
            if (noRoom) return;

            string aboveLabel = padding > 0 ? aboveMidpoint.ToString().PadLeft(padding, '0') : aboveMidpoint.ToString();
            e.Handled = true;
            document.Insert(line.Offset, aboveLabel + " " + Environment.NewLine);
            Editor.CaretOffset = line.Offset + aboveLabel.Length + 1;
            return;
        }

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
        Add("Run on C64U", ViewModel.RunOnC64UAsync);
        Add("Load on C64U", ViewModel.TransferToC64UAsync);
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

    private void HexEditor_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var menu = new MenuFlyout();
        void Add(string header, Func<Task> action, bool enabled = true) =>
            menu.Items.Add(new MenuItem { Header = header, Command = new AsyncCommand(action), IsEnabled = enabled });
        bool hasSelection = HexEditor.HasSelection;
        Add("Cut", HexEditor.CutAsync, hasSelection);
        Add("Copy", HexEditor.CopyAsync, hasSelection);
        Add("Paste", HexEditor.PasteAsync);
        Add("Delete", () => { HexEditor.Delete(); return Task.CompletedTask; }, hasSelection);
        menu.Items.Add(new Separator());
        Add("Select All", () => { HexEditor.SelectAll(); return Task.CompletedTask; });
        menu.ShowAt(HexEditor, showAtPointer: true);
        e.Handled = true;
    }

    // The Edit menu's basics go to whichever editor the active tab is in.
    private bool IsHexTabActive => ViewModel.ActiveTab?.IsHexMode == true;
    private bool IsCompareTabActive => ViewModel.ActiveTab?.IsCompareMode == true;
    private void EditUndo_Click(object? sender, EventArgs e) { if (IsHexTabActive) HexEditor.Undo(); else Editor.Undo(); }
    private void EditRedo_Click(object? sender, EventArgs e) { if (IsHexTabActive) HexEditor.Redo(); else Editor.Redo(); }
    private async void EditCut_Click(object? sender, EventArgs e) { if (IsHexTabActive) await HexEditor.CutAsync(); else Editor.Cut(); }
    private async void EditCopy_Click(object? sender, EventArgs e) { if (IsHexTabActive) await HexEditor.CopyAsync(); else Editor.Copy(); }
    private async void EditPaste_Click(object? sender, EventArgs e) { if (IsHexTabActive) await HexEditor.PasteAsync(); else await PasteAsync(); }
    private void EditDelete_Click(object? sender, EventArgs e) { if (IsHexTabActive) HexEditor.Delete(); else Editor.Delete(); }
    private void EditSelectAll_Click(object? sender, EventArgs e) { if (IsHexTabActive) HexEditor.SelectAll(); else Editor.SelectAll(); }
    private async void EditGoToLine_Click(object? sender, EventArgs e) => await ExecuteGoToLineAsync();
    private void EditComment_Click(object? sender, EventArgs e) => ExecuteCommentSelection();
    private void EditUncomment_Click(object? sender, EventArgs e) => ExecuteUncommentSelection();
    private void EditMakeUppercase_Click(object? sender, EventArgs e) => ExecuteChangeSelectionCase(upper: true);
    private void EditMakeLowercase_Click(object? sender, EventArgs e) => ExecuteChangeSelectionCase(upper: false);
    private async void ViewCodeStatistics_Click(object? sender, EventArgs e) => await ShowCodeStatisticsAsync();

    private async void ViceRun_Click(object? sender, EventArgs e)
    {
        await ViewModel.RunOnViceAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }

    private async void ViceTransfer_Click(object? sender, EventArgs e)
    {
        await ViewModel.TransferToViceAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }
    private async void ViceDebugStart_Click(object? sender, EventArgs e) => await ViewModel.DebugStartOrContinueAsync();
    private async void ViceReset_Click(object? sender, EventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.ResetAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine reset.");
    private async void ViceReboot_Click(object? sender, EventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.RebootAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine rebooted.");
    private async void VicePause_Click(object? sender, EventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.PauseAsync(ViewModel.Settings.ViceEmulatorPath), "VICE machine paused.");
    private async void ViceResume_Click(object? sender, EventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.ResumeAsync(), "VICE machine resumed.");
    private async void VicePowerOff_Click(object? sender, EventArgs e) => await ViewModel.ViceMachineActionAsync(c => c.PowerOffAsync(ViewModel.Settings.ViceEmulatorPath), "VICE emulator closed.");

    private async void C64URun_Click(object? sender, EventArgs e)
    {
        await ViewModel.RunOnC64UAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }

    private async void C64UTransfer_Click(object? sender, EventArgs e)
    {
        await ViewModel.TransferToC64UAsync();
        if (ViewModel.ActiveTab is { } tab) ShowProblemsIfAny(tab);
    }

    private async void C64UDebugStart_Click(object? sender, EventArgs e) => await ViewModel.DebugStartOrContinueOnC64UAsync();
    private async void C64UReset_Click(object? sender, EventArgs e) => await ViewModel.C64UMachineActionAsync("reset", "C64 Ultimate machine reset.");
    private async void C64UReboot_Click(object? sender, EventArgs e) => await ViewModel.C64UMachineActionAsync("reboot", "C64 Ultimate machine rebooted.");
    private async void C64UPause_Click(object? sender, EventArgs e) => await ViewModel.C64UMachineActionAsync("pause", "C64 Ultimate machine paused.");
    private async void C64UResume_Click(object? sender, EventArgs e) => await ViewModel.C64UMachineActionAsync("resume", "C64 Ultimate machine resumed.");
    private async void C64UPowerOff_Click(object? sender, EventArgs e) => await ViewModel.C64UMachineActionAsync("poweroff", "C64 Ultimate powered off.");

    private void ViceDisassemble_Click(object? sender, EventArgs e) => ViewModel.OpenDisassemblyTab(DisassemblySource.Vice);
    private void C64UDisassemble_Click(object? sender, EventArgs e) => ViewModel.OpenDisassemblyTab(DisassemblySource.C64U);

    private async Task DisassembleRequestedAsync()
    {
        if (ViewModel.ActiveTab is not { IsDisassemblyMode: true } tab) return;
        if (!DisassemblyToolbar.TryGetAddressRange(out ushort start, out ushort end, out string? error))
        {
            ViewModel.SetStatus(error!, StatusType.Error);
            return;
        }
        await ViewModel.DisassembleMemoryAsync(tab, start, end);
    }

    private async void ViceAbout_Click(object? sender, EventArgs e) => await ShowAboutViceAsync();

    private async Task ShowAboutViceAsync()
    {
        var info = await ViewModel.FetchViceInfoAsync();
        if (info != null) await new AboutViceWindow(info, ViewModel.Settings.ViceEmulatorPath).ShowDialog(this);
    }

    private async void C64UAbout_Click(object? sender, EventArgs e) => await ShowAboutC64UAsync();

    private async Task ShowAboutC64UAsync()
    {
        var info = await ViewModel.FetchC64UInfoAsync();
        if (info != null) await new AboutC64UWindow(info).ShowDialog(this);
    }

    /// <summary>
    /// Shows the About box. Public so the macOS application menu, which App owns, can invoke it.
    /// </summary>
    public async Task ShowAboutAsync() => await new AboutWindow().ShowDialog(this);

    private async void About_Click(object? sender, EventArgs e) => await ShowAboutAsync();
    private async void HelpGitHub_Click(object? sender, EventArgs e) => await OpenUrlAsync(ViewModel.Settings.GitHubUrl);
    private async void HelpDocs_Click(object? sender, EventArgs e) => await OpenUrlAsync(ViewModel.Settings.DocsUrl);

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception)
        {
            // No browser, or the launch was refused - nothing useful to recover with.
        }
    }

    /// <summary>
    /// Shows the Preferences dialog and applies whatever changed. Public for the same reason as
    /// <see cref="ShowAboutAsync"/>.
    /// </summary>
    public async Task ShowPreferencesAsync() => await ShowPreferencesCoreAsync();

    private async void Preferences_Click(object? sender, EventArgs e) => await ShowPreferencesCoreAsync();

    private async Task ShowPreferencesCoreAsync()
    {
        var dialog = new SettingsWindow(ViewModel.Settings);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;

        ViewModel.SaveSettings();
        ViewModel.NotifySettingsChanged();
        if (AppTheme.Normalize(ViewModel.Settings.Theme) != AppTheme.Current)
            AppTheme.Apply(Application.Current!, ViewModel.Settings.Theme);
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
