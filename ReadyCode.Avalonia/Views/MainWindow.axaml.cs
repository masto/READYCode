// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Avalonia.Models;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Views;

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

    private FoldingManager? _foldingManager;
    private EditorTab? _boundTab;
    private bool _closeConfirmed;

    #endregion

    #region Constructors

    public MainWindow()
    {
        InitializeComponent();

        Editor.TextArea.TextView.ElementGenerators.Add(_petsciiGlyphGenerator);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateActiveLine();
        Editor.TextChanged += (_, _) => { _foldingTimer.Stop(); _foldingTimer.Start(); };
        _foldingTimer.Tick += (_, _) => { _foldingTimer.Stop(); UpdateFoldings(); };

        DataContextChanged += (_, _) => AttachViewModel();
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

        var dirty = vm.OpenTabs.Where(t => t.IsModified).ToList();
        if (dirty.Count == 0)
        {
            vm.SaveSettings();
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

        vm.SaveSettings();
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
        vm.ErrorRaised += (title, message) => Dispatcher.UIThread.Post(async () => await MessageDialog.ShowAsync(this, title, message));
        ApplyEditorSettings();
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
        Editor.Document = tab.Document;
        ApplyLanguageStyling(tab);
        InstallFolding(tab);
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

        // A .bas file is plain ASCII source; a detokenized .prg is styled to look like what ends
        // up on a real C64 screen, which needs the PETSCII font and glyph substitution.
        bool isAsciiStyled = isAsm || tab.Kind == C64UFileKind.Bas;
        Editor.FontFamily = isAsciiStyled ? _asciiFont : _petsciiFont;
        _petsciiGlyphGenerator.IsAsmMode = isAsciiStyled;
        Editor.TextArea.TextView.Redraw();
    }

    private void ApplyEditorSettings()
    {
        Editor.FontSize = ViewModel.Settings.EditorFontSize > 0 ? ViewModel.Settings.EditorFontSize + 4 : 16;
        Editor.WordWrap = ViewModel.Settings.WordWrap;
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
        string folder = ViewModel.Settings.LastFolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        return await StorageProvider.TryGetFolderFromPathAsync(folder);
    }

    // Saves a tab to its existing path, or prompts for one when it has none (or when forced).
    private async Task<bool> SaveTabAsync(EditorTab tab, bool forceDialog)
    {
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

        return ViewModel.SaveTab(tab, path);
    }

    #endregion

    #region Event Handlers

    private void FileNew_Click(object? sender, RoutedEventArgs e) => ViewModel.NewTab();

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
            if (file.TryGetLocalPath() is { } path)
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

    private async void ViceRun_Click(object? sender, RoutedEventArgs e) => await ViewModel.RunOnViceAsync();
    private async void ViceTransfer_Click(object? sender, RoutedEventArgs e) => await ViewModel.TransferToViceAsync();
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
        ApplyEditorSettings();
        ViewModel.SetStatus("Preferences saved.");
    }

    #endregion
}
