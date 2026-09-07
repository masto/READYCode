// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ReadyCode.Assembler;
using ReadyCode.Avalonia.Models;
using ReadyCode.C64U;
using ReadyCode.Diagnostics;
using ReadyCode.Diff;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// Severity of a status-bar message, which decides the bar's colors.
/// </summary>
public enum StatusType
{
    /// <summary>An ordinary progress or completion message.</summary>
    Info,

    /// <summary>Something the user should notice but that didn't fail.</summary>
    Warning,

    /// <summary>An operation failed.</summary>
    Error,
}

/// <summary>
/// Application state and the file, folder-explorer, and VICE operations behind the main
/// window's menus. Dialogs are the view's job: methods here report failures through
/// <see cref="ErrorRaised"/> and progress through the status bar properties, and never block
/// on UI.
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);
    private static readonly TimeSpan _sysCommandDelay = TimeSpan.FromSeconds(0.5);

    private EditorTab? _activeTab;
    private string _statusText = "Ready.";
    private StatusType _statusType = StatusType.Info;
    private string _explorerTitle = "";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class, loading the persisted
    /// settings, the last open folder, and the previous session's tabs.
    /// </summary>
    public MainViewModel()
    {
        Settings = AppSettings.Load();
        ApplyPlatformDefaults(Settings);
        LastDialogFolder = Directory.Exists(Settings.LastFolderPath) ? Settings.LastFolderPath : "";

        OpenTabs.Add(EditorTab.CreateNew(EditorLanguage.Basic));
        ActiveTab = OpenTabs[0];

        if (Directory.Exists(Settings.LastFolderPath))
            LoadFolder(Settings.LastFolderPath);

        RestoreOpenTabs();
    }

    #endregion

    #region Public Properties

    /// <summary>Gets the persisted application settings (shared with the WPF app's format).</summary>
    public AppSettings Settings { get; }

    /// <summary>Gets the open documents, in tab order.</summary>
    public ObservableCollection<EditorTab> OpenTabs { get; } = new();

    /// <summary>Gets or sets the tab currently shown in the editor.</summary>
    public EditorTab? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (ReferenceEquals(_activeTab, value)) return;
            _activeTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>Gets the text shown in the status bar.</summary>
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the severity of the current status message.</summary>
    public StatusType StatusType
    {
        get => _statusType;
        private set { if (_statusType == value) return; _statusType = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the main window title.</summary>
    public string WindowTitle => ActiveTab == null ? "READYCode" : $"{ActiveTab.FileName} - READYCode";

    /// <summary>
    /// Gets or sets the folder the file pickers start in. Deliberately separate from the
    /// explorer's root folder, which is what <see cref="AppSettings.LastFolderPath"/> holds.
    /// </summary>
    public string LastDialogFolder { get; set; }

    // ── Folder explorer ───────────────────────────────────────────────────────

    /// <summary>Gets the root items of the folder explorer tree.</summary>
    public ObservableCollection<FileTreeItem> FolderItems { get; } = new();

    /// <summary>Gets the folder currently open in the explorer, or "" if none.</summary>
    public string RootFolderPath => Settings.LastFolderPath;

    /// <summary>Gets whether a folder is open in the explorer.</summary>
    public bool IsFolderOpen => !string.IsNullOrEmpty(RootFolderPath);

    /// <summary>Gets the title shown above the explorer tree (the open folder's name).</summary>
    public string ExplorerTitle
    {
        get => _explorerTitle;
        private set { if (_explorerTitle == value) return; _explorerTitle = value; OnPropertyChanged(); }
    }

    // ── Problems panel ────────────────────────────────────────────────────────

    /// <summary>Gets the diagnostics of every open tab, for the Problems panel.</summary>
    public ObservableCollection<ErrorListRow> ErrorListRows { get; } = new();

    /// <summary>Gets or sets whether the bottom panel (Problems / Debug) is shown. Persisted in settings.</summary>
    public bool IsBottomPanelOpen
    {
        get => Settings.IsBottomPanelOpen;
        set
        {
            if (Settings.IsBottomPanelOpen == value) return;
            Settings.IsBottomPanelOpen = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Gets or sets the selected bottom-panel tab index (0 Problems, 1 Variables, 2 Breakpoints, 3 Call Stack).</summary>
    public int BottomPanelTabIndex
    {
        get => Settings.ActiveBottomPanelTab switch { "Variables" => 1, "Breakpoints" => 2, "CallStack" => 3, _ => 0 };
        set
        {
            string name = value switch { 1 => "Variables", 2 => "Breakpoints", 3 => "CallStack", _ => "Problems" };
            if (Settings.ActiveBottomPanelTab == name) return;
            Settings.ActiveBottomPanelTab = name;
            OnPropertyChanged();
        }
    }

    /// <summary>Gets the Problems panel header, including the issue count.</summary>
    public string ProblemsTitle => ErrorListRows.Count == 0 ? "PROBLEMS" : $"PROBLEMS ({ErrorListRows.Count})";

    /// <summary>Gets the text shown when the Problems panel has no rows.</summary>
    public string ProblemsEmptyText => Settings.EnableLinting
        ? "No issues found."
        : "Linting is disabled - enable it in Preferences to see errors here.";

    /// <summary>Gets or sets whether the column guide is drawn. Persisted in settings.</summary>
    public bool ShowColumnGuide
    {
        get => Settings.ShowColumnGuide;
        set { if (Settings.ShowColumnGuide == value) return; Settings.ShowColumnGuide = value; OnPropertyChanged(); }
    }

    /// <summary>Gets or sets whether the editor wraps long lines. Persisted in settings.</summary>
    public bool WordWrap
    {
        get => Settings.WordWrap;
        set { if (Settings.WordWrap == value) return; Settings.WordWrap = value; OnPropertyChanged(); }
    }

    /// <summary>Gets or sets whether the status bar is shown. Persisted in settings.</summary>
    public bool ShowStatusBar
    {
        get => Settings.ShowStatusBar;
        set { if (Settings.ShowStatusBar == value) return; Settings.ShowStatusBar = value; OnPropertyChanged(); }
    }

    /// <summary>Re-raises change notifications for every settings-backed property (after Preferences).</summary>
    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(ShowColumnGuide));
        OnPropertyChanged(nameof(WordWrap));
        OnPropertyChanged(nameof(ShowStatusBar));
        OnPropertyChanged(nameof(ProblemsEmptyText));
    }

    /// <summary>Gets or sets whether the explorer panel is shown. Persisted in settings.</summary>
    public bool IsExplorerOpen
    {
        get => Settings.IsLeftPanelOpen;
        set
        {
            if (Settings.IsLeftPanelOpen == value) return;
            Settings.IsLeftPanelOpen = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when a bindable property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when an operation fails in a way the user must see in a dialog (not just the status
    /// bar): (title, message).
    /// </summary>
    public event Action<string, string>? ErrorRaised;

    #endregion

    #region Public Methods - Status and Settings

    /// <summary>Sets the status bar message.</summary>
    public void SetStatus(string text, StatusType type = StatusType.Info)
    {
        StatusText = text;
        StatusType = type;
    }

    /// <summary>
    /// Records which files are open (for <see cref="AppSettings.RestoreOpenTabsOnStartup"/>) and
    /// persists settings to disk.
    /// </summary>
    public void SaveSettingsAndSession()
    {
        Settings.OpenTabPaths = Settings.RestoreOpenTabsOnStartup
            ? OpenTabs.Where(t => t.FilePath != null).Select(t => t.FilePath!).ToList()
            : new List<string>();
        SaveSettings();
    }

    /// <summary>Persists settings to disk.</summary>
    public void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) { SetStatus($"Couldn't save settings: {ex.Message}", StatusType.Warning); }
    }

    #endregion

    #region Public Methods - Tabs and Files

    /// <summary>Opens a new, empty tab for the given language and activates it.</summary>
    public EditorTab NewTab(EditorLanguage language = EditorLanguage.Basic)
    {
        var tab = EditorTab.CreateNew(language);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    /// <summary>
    /// Gets the already-open tab for <paramref name="path"/>, if any.
    /// </summary>
    public EditorTab? FindOpenTab(string path) =>
        OpenTabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens <paramref name="path"/> in a tab. If the file is already open, its tab is reloaded
    /// from disk (discarding any unsaved edits - the caller is expected to have confirmed that)
    /// and activated, the way most editors treat re-opening a file as "revert to saved".
    /// Tokenized .prg files are detokenized to a BASIC listing; everything else is read as text.
    /// </summary>
    public bool OpenFile(string path)
    {
        var existing = FindOpenTab(path);

        if (FileClassifier.Classify(path, isFolder: false).IsDiskImageKind())
        {
            SetStatus("Disk images can't be opened as text - expand them in the explorer to see the programs inside.", StatusType.Warning);
            return false;
        }

        try
        {
            byte[]? prgData = null;
            C64UFileKind kind;
            if (path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
            {
                prgData = File.ReadAllBytes(path);
                kind = FileClassifier.Classify(path, isFolder: false, () => prgData);
            }
            else
            {
                kind = FileClassifier.Classify(path, isFolder: false);
            }

            if (kind == C64UFileKind.Ml)
            {
                SetStatus("Machine-language files need the hex editor, which isn't available yet in this version.", StatusType.Warning);
                return false;
            }

            var tab = existing ?? new EditorTab
            {
                FilePath = path,
                Language = LanguageClassifier.Classify(path),
            };
            tab.Kind = kind;

            tab.Document.Text = prgData != null
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(prgData))
                : File.ReadAllText(path, Encoding.UTF8);
            tab.IsModified = false;

            if (existing == null)
                AddTab(tab);

            ActiveTab = tab;
            LastDialogFolder = Path.GetDirectoryName(path) ?? LastDialogFolder;
            SetStatus(existing == null ? $"Opened {tab.FileName}." : $"Reloaded {tab.FileName} from disk.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Open File Error", $"Error opening file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a program stored inside a disk image (an explorer entry with in-memory content) in
    /// a tab, re-activating the existing tab if it's already open.
    /// </summary>
    public bool OpenVirtualEntry(FileTreeItem item)
    {
        if (item.Content == null || item.SourcePath == null) return false;

        if (!item.IsOpenable)
        {
            SetStatus($"{item.Name} isn't a text-editable program type.", StatusType.Warning);
            return false;
        }

        if (item.Kind == C64UFileKind.Ml)
        {
            SetStatus("Machine-language files need the hex editor, which isn't available yet in this version.", StatusType.Warning);
            return false;
        }

        string sourceId = $"{item.SourcePath}!{item.Name}";
        var existing = OpenTabs.FirstOrDefault(t => t.VirtualSourceId == sourceId);
        if (existing != null)
        {
            ActiveTab = existing;
            return true;
        }

        try
        {
            var tab = new EditorTab
            {
                DisplayName = item.Name,
                VirtualSourceId = sourceId,
                Kind = item.Kind,
                Language = item.Kind == C64UFileKind.Asm ? EditorLanguage.Asm : EditorLanguage.Basic,
            };
            tab.Document.Text = item.Kind == C64UFileKind.Prg
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(item.Content))
                : CompareFileResolver.DecodeSourceText(item.Content);
            tab.IsModified = false;

            AddTab(tab);
            ActiveTab = tab;
            SetStatus($"Opened {item.Name} from {Path.GetFileName(item.SourcePath)}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Open File Error", $"Error opening file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="tab"/> to <paramref name="filePath"/>: plain text for assembly
    /// source or .bas listings, BASIC-tokenized PRG bytes otherwise.
    /// </summary>
    public bool SaveTab(EditorTab tab, string filePath)
    {
        try
        {
            if (!PrgConverter.ShouldTokenizeOnSave(tab.Language, filePath))
            {
                File.WriteAllText(filePath, tab.Document.Text, Encoding.UTF8);
                SetStatus("File saved.");
            }
            else
            {
                byte[] prgData = new PrgConverter().ConvertToPrg(tab.Document.Text);
                File.WriteAllBytes(filePath, prgData);
                SetStatus($"File saved: {prgData.Length:N0} tokenized bytes.");
            }

            bool isNewPath = !string.Equals(tab.FilePath, filePath, StringComparison.OrdinalIgnoreCase);
            tab.VirtualSourceId = null;
            tab.DisplayName = null;
            tab.FilePath = filePath;
            tab.Kind = FileClassifier.Classify(filePath, isFolder: false);
            tab.Language = LanguageClassifier.Classify(filePath);
            tab.IsModified = false;
            LastDialogFolder = Path.GetDirectoryName(filePath) ?? LastDialogFolder;
            OnPropertyChanged(nameof(WindowTitle));

            if (isNewPath)
                RefreshFolderContaining(filePath);
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Save File Error", $"Error saving file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes a disk-image entry tab back into its source image (plain text for assembly,
    /// tokenized PRG bytes otherwise) and refreshes the image's node in the explorer.
    /// </summary>
    public bool SaveVirtualTab(EditorTab tab)
    {
        if (tab.VirtualSourceId == null) return false;

        var parts = tab.VirtualSourceId.Split('!', 2);
        if (parts.Length != 2) return false;
        string sourcePath = parts[0];
        string entryName = parts[1];

        try
        {
            byte[] newContent = tab.Language == EditorLanguage.Asm
                ? Encoding.UTF8.GetBytes(tab.Document.Text)
                : new PrgConverter().ConvertToPrg(tab.Document.Text);

            byte[] diskBytes = File.ReadAllBytes(sourcePath);
            var kind = FileClassifier.Classify(sourcePath, isFolder: false);
            byte[] updated = DiskImage.ForKind(kind).ReplaceEntry(diskBytes, entryName, newContent);
            File.WriteAllBytes(sourcePath, updated);

            tab.IsModified = false;
            FindItemByPath(sourcePath)?.RefreshChildren();
            SetStatus($"{entryName} saved into {Path.GetFileName(sourcePath)}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Save File Error", $"Error saving to disk image: {ex.Message}");
            return false;
        }
    }

    /// <summary>Closes <paramref name="tab"/>, always leaving at least one tab open.</summary>
    public void CloseTab(EditorTab tab)
    {
        int index = OpenTabs.IndexOf(tab);
        if (index < 0) return;

        OpenTabs.RemoveAt(index);
        if (OpenTabs.Count == 0)
            OpenTabs.Add(EditorTab.CreateNew(EditorLanguage.Basic));

        if (ReferenceEquals(ActiveTab, tab) || ActiveTab == null)
            ActiveTab = OpenTabs[Math.Min(index, OpenTabs.Count - 1)];

        RefreshErrorList();
    }

    /// <summary>
    /// Analyzes <paramref name="tab"/>'s text (BASIC or assembly) and caches the result on it.
    /// Returns no diagnostics when linting is disabled.
    /// </summary>
    public IReadOnlyList<EditorDiagnostic> AnalyzeTab(EditorTab tab)
    {
        string text = tab.Document.Text;
        tab.Diagnostics = !Settings.EnableLinting
            ? Array.Empty<EditorDiagnostic>()
            : tab.Language == EditorLanguage.Asm
                ? AsmDiagnostics.Analyze(text, new Asm6502Assembler().Assemble(text, Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress))
                : BasicDiagnostics.Analyze(text);
        RefreshErrorList();
        return tab.Diagnostics;
    }

    /// <summary>Rebuilds the Problems rows from every open tab's cached diagnostics.</summary>
    public void RefreshErrorList()
    {
        ErrorListRows.Clear();

        if (Settings.EnableLinting)
        {
            foreach (var tab in OpenTabs)
            {
                foreach (var diag in tab.Diagnostics)
                {
                    var documentLine = tab.Document.GetLineByOffset(Math.Min(diag.Offset, tab.Document.TextLength));
                    int? basicLineNumber = null;
                    if (tab.Language == EditorLanguage.Basic &&
                        BasicDiagnostics.TryParseLineNumber(tab.Document.GetText(documentLine), out int n, out _, out _, out _))
                        basicLineNumber = n;

                    ErrorListRows.Add(new ErrorListRow
                    {
                        Tab = tab,
                        Message = diag.Message,
                        Offset = diag.Offset,
                        Line = documentLine.LineNumber,
                        BasicLineNumber = basicLineNumber,
                    });
                }
            }
        }

        OnPropertyChanged(nameof(ProblemsTitle));
        OnPropertyChanged(nameof(ProblemsEmptyText));
    }

    #endregion

    #region Public Methods - Folder Explorer

    /// <summary>
    /// Loads the folder explorer tree from <paramref name="folderPath"/> and remembers it as
    /// the open folder.
    /// </summary>
    public void LoadFolder(string folderPath)
    {
        Settings.LastFolderPath = folderPath;
        string name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
        ExplorerTitle = string.IsNullOrEmpty(name) ? folderPath.ToUpperInvariant() : name.ToUpperInvariant();

        FolderItems.Clear();
        try
        {
            foreach (string dir in Directory.GetDirectories(folderPath)
                                            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                FolderItems.Add(new FileTreeItem(dir, true));

            foreach (string file in Directory.GetFiles(folderPath)
                                             .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                FolderItems.Add(new FileTreeItem(file, false));
        }
        catch { /* Access denied, etc. */ }

        LoadBreakpointsForProject(folderPath);
        OnPropertyChanged(nameof(RootFolderPath));
        OnPropertyChanged(nameof(IsFolderOpen));
    }

    /// <summary>Closes the open folder and empties the explorer.</summary>
    public void CloseFolder()
    {
        Settings.LastFolderPath = "";
        ExplorerTitle = "";
        FolderItems.Clear();
        OnPropertyChanged(nameof(RootFolderPath));
        OnPropertyChanged(nameof(IsFolderOpen));
    }

    /// <summary>Reloads the explorer tree, preserving which root folders are expanded.</summary>
    public void RefreshRootItems()
    {
        if (!IsFolderOpen) return;

        var expandedPaths = FolderItems
            .Where(i => i.IsFolder && i.IsExpanded)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        LoadFolder(RootFolderPath);

        foreach (var item in FolderItems.Where(i => i.IsFolder && expandedPaths.Contains(i.FullPath)))
            item.IsExpanded = true;
    }

    /// <summary>Collapses every folder in the explorer.</summary>
    public void CollapseAllFolders()
    {
        foreach (var item in FolderItems)
            item.CollapseAll();
    }

    /// <summary>Finds the loaded explorer item for <paramref name="path"/>, if it's been loaded.</summary>
    public FileTreeItem? FindItemByPath(string path) => FindItemByPath(FolderItems, path);

    /// <summary>Returns the folder item containing <paramref name="target"/>, or null at the root.</summary>
    public FileTreeItem? FindParentFolder(FileTreeItem target) =>
        FolderItems.Contains(target) ? null : FindParentFolderRecursive(FolderItems, target);

    /// <summary>
    /// Refreshes the explorer node for the folder containing <paramref name="path"/> (or the
    /// whole tree if that folder is the root or isn't loaded), so a newly created or renamed
    /// file shows up without collapsing everything else.
    /// </summary>
    public void RefreshFolderContaining(string path)
    {
        if (!IsFolderOpen) return;

        string? dir = Path.GetDirectoryName(path);
        if (dir == null) return;

        string root = RootFolderPath.TrimEnd(Path.DirectorySeparatorChar);
        bool isRoot = string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase);
        bool isNested = dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isRoot && !isNested) return;

        if (isRoot)
            RefreshRootItems();
        else
            FindItemByPath(dir)?.RefreshChildren();
    }

    /// <summary>
    /// Creates an empty file named <paramref name="fileName"/> in <paramref name="parentDirectory"/>
    /// and opens it in a new tab.
    /// </summary>
    public bool CreateFile(string parentDirectory, string fileName)
    {
        string path = Path.Combine(parentDirectory, fileName);
        if (File.Exists(path) || Directory.Exists(path))
        {
            ErrorRaised?.Invoke("New File", $"\"{fileName}\" already exists.");
            return false;
        }

        try
        {
            File.WriteAllText(path, string.Empty);
            RefreshFolderContaining(path);

            // Open a blank tab directly rather than re-reading the (empty) file, which for a .prg
            // would go through the PRG parser and produce nothing useful.
            var tab = new EditorTab
            {
                FilePath = path,
                Language = LanguageClassifier.Classify(path),
                Kind = FileClassifier.Classify(path, isFolder: false),
            };
            tab.IsModified = false;
            AddTab(tab);
            ActiveTab = tab;
            SetStatus($"Created {fileName}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("New File", $"Could not create file: {ex.Message}");
            return false;
        }
    }

    /// <summary>Creates a folder named <paramref name="folderName"/> in <paramref name="parentDirectory"/>.</summary>
    public bool CreateFolder(string parentDirectory, string folderName)
    {
        string path = Path.Combine(parentDirectory, folderName);
        if (File.Exists(path) || Directory.Exists(path))
        {
            ErrorRaised?.Invoke("New Folder", $"\"{folderName}\" already exists.");
            return false;
        }

        try
        {
            Directory.CreateDirectory(path);
            RefreshFolderContaining(path);
            SetStatus($"Created folder {folderName}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("New Folder", $"Could not create folder: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Renames a file, folder, or disk-image entry, updating any open tab for it.
    /// </summary>
    public bool RenameItem(FileTreeItem item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return false;

        try
        {
            if (item.IsVirtual)
            {
                if (item.SourcePath == null) return false;
                byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
                var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
                File.WriteAllBytes(item.SourcePath, DiskImage.ForKind(kind).RenameEntry(diskBytes, item.Name, newName));
                FindItemByPath(item.SourcePath)?.RefreshChildren();

                string oldId = $"{item.SourcePath}!{item.Name}";
                foreach (var tab in OpenTabs.Where(t => t.VirtualSourceId == oldId))
                {
                    tab.VirtualSourceId = $"{item.SourcePath}!{newName}";
                    tab.DisplayName = newName;
                }
                return true;
            }

            string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);
            if (item.IsFolder)
            {
                Directory.Move(item.FullPath, newPath);
                foreach (var tab in OpenTabs.Where(t => t.FilePath != null && t.FilePath.StartsWith(item.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    tab.FilePath = newPath + tab.FilePath![item.FullPath.Length..];
            }
            else
            {
                File.Move(item.FullPath, newPath);
                foreach (var tab in OpenTabs.Where(t => string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    tab.FilePath = newPath;
                    tab.Kind = FileClassifier.Classify(newPath, isFolder: false);
                    tab.Language = LanguageClassifier.Classify(newPath);
                }
            }

            RefreshFolderContaining(item.FullPath);
            OnPropertyChanged(nameof(WindowTitle));
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Rename", $"Could not rename \"{item.Name}\": {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Permanently deletes a file, folder, or disk-image entry (the caller confirms first),
    /// closing any tab that was showing it.
    /// </summary>
    public bool DeleteItem(FileTreeItem item)
    {
        try
        {
            if (item.IsVirtual)
            {
                if (item.SourcePath == null) return false;
                byte[] diskBytes = File.ReadAllBytes(item.SourcePath);
                var kind = FileClassifier.Classify(item.SourcePath, isFolder: false);
                File.WriteAllBytes(item.SourcePath, DiskImage.ForKind(kind).DeleteEntry(diskBytes, item.Name));

                string id = $"{item.SourcePath}!{item.Name}";
                foreach (var tab in OpenTabs.Where(t => t.VirtualSourceId == id).ToList())
                {
                    tab.IsModified = false;
                    CloseTab(tab);
                }

                FindItemByPath(item.SourcePath)?.RefreshChildren();
                return true;
            }

            if (item.IsFolder)
            {
                Directory.Delete(item.FullPath, recursive: true);
                foreach (var tab in OpenTabs.Where(t => t.FilePath != null && t.FilePath.StartsWith(item.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    tab.IsModified = false;
                    CloseTab(tab);
                }
            }
            else
            {
                File.Delete(item.FullPath);
                foreach (var tab in OpenTabs.Where(t => string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    tab.IsModified = false;
                    CloseTab(tab);
                }
            }

            // Remove the node directly so other expanded folders stay expanded.
            var parent = FindParentFolder(item);
            if (parent != null) parent.Children.Remove(item);
            else FolderItems.Remove(item);
            SetStatus($"Deleted {item.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Delete", $"Could not delete \"{item.Name}\": {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Public Methods - VICE

    /// <summary>Loads the active tab's program into VICE without running it.</summary>
    public Task TransferToViceAsync() => SendToViceAsync(run: false);

    /// <summary>Loads and runs the active tab's program in VICE.</summary>
    public Task RunOnViceAsync() => SendToViceAsync(run: true);

    /// <summary>
    /// Loads (or loads and runs) an explorer item in VICE without opening it: an .asm file is
    /// assembled, a .bas listing is tokenized, and a .prg/.ml file's bytes are sent as-is.
    /// </summary>
    public async Task SendFileToViceAsync(FileTreeItem item, bool run)
    {
        if (!EnsureVicePathConfigured()) return;

        byte[] raw;
        try
        {
            raw = item.Content ?? File.ReadAllBytes(item.FullPath);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Load/Run File", $"Error reading file: {ex.Message}");
            return;
        }

        byte[] prgBytes;
        if (item.Kind == C64UFileKind.Asm)
        {
            var result = new Asm6502Assembler().Assemble(
                CompareFileResolver.DecodeSourceText(raw), Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);
            if (!result.Success)
            {
                ErrorRaised?.Invoke("Assembly Errors",
                    string.Join(Environment.NewLine, result.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}")));
                SetStatus($"Assembly failed with {result.Errors.Count} error(s).", StatusType.Error);
                return;
            }
            prgBytes = result.PrgBytes!;
        }
        else if (item.Kind == C64UFileKind.Bas)
        {
            prgBytes = new PrgConverter().ConvertToPrg(CompareFileResolver.DecodeSourceText(raw));
        }
        else
        {
            prgBytes = raw;
        }

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            SetStatus($"Transferring '{item.Name}' to VICE…");

            if (!run)
            {
                await client.TransferAsync(Settings.ViceEmulatorPath, prgBytes, item.Name, Settings.ViceBringToForeground);
                SetStatus($"'{item.Name}' transferred to VICE. Type RUN in the emulator to start it.");
            }
            else if (new PrgConverter().NeedsSysToRun(prgBytes, out ushort origin))
            {
                await client.TransferAsync(Settings.ViceEmulatorPath, prgBytes, item.Name, Settings.ViceBringToForeground);
                await Task.Delay(_sysCommandDelay);
                await client.TypeAsync($"SYS{origin}\r");
                SetStatus($"'{item.Name}' transferred and running on VICE (SYS{origin}).");
            }
            else
            {
                await client.RunAsync(Settings.ViceEmulatorPath, prgBytes, item.Name, Settings.ViceBringToForeground);
                SetStatus($"'{item.Name}' transferred and running on VICE.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"VICE transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>Performs a machine action (reset, reboot, pause, resume, power off) on VICE.</summary>
    public async Task ViceMachineActionAsync(Func<ViceClient, Task> action, string successMessage)
    {
        if (!EnsureVicePathConfigured()) return;

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            await action(client);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"VICE action failed: {ex.Message}", StatusType.Error);
        }
    }

    #endregion

    #region Private Methods

    // Reopens the files that were open when the app last closed, skipping any that have since
    // moved or been deleted, and activates the first of them as it was.
    private void RestoreOpenTabs()
    {
        if (!Settings.RestoreOpenTabsOnStartup) return;

        EditorTab? first = null;
        foreach (string path in Settings.OpenTabPaths)
        {
            if (!File.Exists(path)) continue;
            if (OpenFile(path)) first ??= ActiveTab;
        }

        if (first != null) ActiveTab = first;
        SetStatus("Ready.");
    }

    // Sensible first-run defaults for the current OS - the settings file itself is shared in
    // format with the WPF app, but a Windows-style empty emulator path helps nobody on a Mac.
    private static void ApplyPlatformDefaults(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ViceEmulatorPath)) return;

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/opt/homebrew/bin/x64sc", "/usr/local/bin/x64sc", "/Applications/VICE/x64sc.app/Contents/MacOS/x64sc"]
            : OperatingSystem.IsLinux()
                ? ["/usr/bin/x64sc", "/usr/local/bin/x64sc", "/snap/bin/vice.x64sc"]
                : [];

        settings.ViceEmulatorPath = candidates.FirstOrDefault(File.Exists) ?? "";
    }

    // Adds a tab, replacing a pristine untitled tab rather than leaving it hanging around.
    private void AddTab(EditorTab tab)
    {
        if (OpenTabs.Count == 1 && OpenTabs[0].FilePath == null && !OpenTabs[0].IsVirtual
            && !OpenTabs[0].IsModified && OpenTabs[0].Document.TextLength == 0)
            OpenTabs.Clear();

        OpenTabs.Add(tab);
    }

    private static FileTreeItem? FindItemByPath(IEnumerable<FileTreeItem> items, string path)
    {
        foreach (var item in items)
        {
            if (!item.IsVirtual && string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return item;
            if (item.IsFolder && path.StartsWith(item.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var found = FindItemByPath(item.Children, path);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static FileTreeItem? FindParentFolderRecursive(IEnumerable<FileTreeItem> items, FileTreeItem target)
    {
        foreach (var container in items.Where(i => i.IsFolder || i.IsDiskImage))
        {
            if (container.Children.Contains(target)) return container;
            var found = FindParentFolderRecursive(container.Children, target);
            if (found != null) return found;
        }
        return null;
    }

    private bool EnsureVicePathConfigured()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ViceEmulatorPath)) return true;
        SetStatus("Please set the VICE emulator path in Settings first.", StatusType.Error);
        return false;
    }

    private async Task SendToViceAsync(bool run)
    {
        string? text = ActiveTab?.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to run. Please write some code first.", StatusType.Error);
            return;
        }

        if (!EnsureVicePathConfigured()) return;

        if (!TryBuildPrgData(text, out byte[]? prgData, out AssemblyResult? asmResult))
            return;

        try
        {
            var client = new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort);
            SetStatus("Transferring program to VICE…");

            if (run && NeedsSysCommand(asmResult))
            {
                // Standalone machine code (an explicit ".org", or "Standalone" output mode): VICE's
                // autostart RUN does nothing useful with no BASIC program in memory. Load without
                // running, then type "SYS <origin>" once the machine is back at READY.
                await client.TransferAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);
                await Task.Delay(_sysCommandDelay);
                await client.TypeAsync($"SYS{asmResult!.Origin}\r");
                SetStatus($"Program transferred and running on VICE (SYS{asmResult.Origin}).");
            }
            else if (run)
            {
                await client.RunAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);
                SetStatus("Program transferred and running on VICE.");
            }
            else
            {
                await client.TransferAsync(Settings.ViceEmulatorPath, prgData!, ActiveTab!.FileName, Settings.ViceBringToForeground);
                SetStatus("Program transferred to VICE.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"VICE transfer failed: {ex.Message}", StatusType.Error);
        }
    }

    private bool NeedsSysCommand(AssemblyResult? asmResult) =>
        asmResult != null && (asmResult.HasExplicitOrigin || Settings.AsmOutputMode == "Standalone");

    private bool TryBuildPrgData(string text, out byte[]? prgData, out AssemblyResult? asmResult)
    {
        asmResult = null;

        if (ActiveTab!.Language == EditorLanguage.Asm)
        {
            asmResult = new Asm6502Assembler().Assemble(
                text, Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);
            if (!asmResult.Success)
            {
                ErrorRaised?.Invoke("Assembly Errors",
                    string.Join(Environment.NewLine, asmResult.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}")));
                SetStatus($"Assembly failed with {asmResult.Errors.Count} error(s).", StatusType.Error);
                prgData = null;
                return false;
            }

            prgData = asmResult.PrgBytes;
            return true;
        }

        prgData = new PrgConverter().ConvertToPrg(PrepareCodeForTransfer(text));
        return true;
    }

    private string PrepareCodeForTransfer(string text)
    {
        if (!Settings.MinifyOnTransfer) return text;
        return CodeMinifier.Minify(text,
            removeWhitespace:       Settings.MinifyRemoveWhitespace,
            replace0WithPeriod:     Settings.MinifyReplaceZeroWithDot,
            useScientificNotation:  Settings.MinifyUseScientificNotation,
            removeComments:         Settings.MinifyRemoveComments,
            simplifyNextStatements: Settings.MinifySimplifyNext,
            renumberLines:          Settings.MinifyRenumberLines);
    }

    private string PadLineNumbers(string sourceCode)
    {
        int padding = Settings.LineNumberPadding;
        if (padding <= 0) return sourceCode;

        string[] lines = sourceCode.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            Match match = _leadingLineNumberPattern.Match(lines[i]);
            if (!match.Success) continue;

            string digits = match.Groups[2].Value;
            if (digits.Length >= padding) continue;

            string padded = digits.PadLeft(padding, '0');
            lines[i] = lines[i].Remove(match.Groups[2].Index, digits.Length).Insert(match.Groups[2].Index, padded);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
