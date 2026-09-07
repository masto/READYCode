// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ReadyCode.Assembler;
using ReadyCode.Avalonia.Models;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.ViewModels;

public enum StatusType { Info, Warning, Error }

/// <summary>
/// Application state and the file/VICE operations behind the main window's menus. Dialogs are
/// the view's job: methods here report failures through <see cref="ErrorRaised"/> and progress
/// through the status bar properties, and never block on UI.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);
    private static readonly TimeSpan _sysCommandDelay = TimeSpan.FromSeconds(0.5);

    private EditorTab? _activeTab;
    private string _statusText = "Ready.";
    private StatusType _statusType = StatusType.Info;

    #endregion

    #region Constructors

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        ApplyPlatformDefaults(Settings);
        OpenTabs.Add(new EditorTab());
        ActiveTab = OpenTabs[0];
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

    #endregion

    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when an operation fails in a way the user must see in a dialog (not just the status
    /// bar): (title, message).
    /// </summary>
    public event Action<string, string>? ErrorRaised;

    #endregion

    #region Public Methods

    /// <summary>Sets the status bar message.</summary>
    public void SetStatus(string text, StatusType type = StatusType.Info)
    {
        StatusText = text;
        StatusType = type;
    }

    /// <summary>Opens a new, empty BASIC tab and activates it.</summary>
    public EditorTab NewTab()
    {
        var tab = new EditorTab();
        OpenTabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    /// <summary>
    /// Opens <paramref name="path"/> in a tab (activating the existing tab if it's already open).
    /// Tokenized .prg files are detokenized to a BASIC listing; everything else is read as text.
    /// </summary>
    public bool OpenFile(string path)
    {
        var existing = OpenTabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            ActiveTab = existing;
            return true;
        }

        if (FileClassifier.Classify(path, isFolder: false).IsDiskImageKind())
        {
            SetStatus("Disk images can't be opened as text yet in this version.", StatusType.Warning);
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

            var tab = new EditorTab
            {
                FilePath = path,
                Language = LanguageClassifier.Classify(path),
                Kind = kind,
            };

            tab.Document.Text = prgData != null
                ? PadLineNumbers(new PrgConverter().ConvertFromPrg(prgData))
                : File.ReadAllText(path, Encoding.UTF8);
            tab.IsModified = false;

            // Replace a pristine, untitled tab rather than leaving it hanging around.
            if (OpenTabs.Count == 1 && OpenTabs[0].FilePath == null && !OpenTabs[0].IsModified && OpenTabs[0].Document.TextLength == 0)
                OpenTabs.Clear();

            OpenTabs.Add(tab);
            ActiveTab = tab;
            Settings.LastFolderPath = Path.GetDirectoryName(path) ?? Settings.LastFolderPath;
            SetStatus($"Opened {tab.FileName}.");
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

            tab.FilePath = filePath;
            tab.Kind = FileClassifier.Classify(filePath, isFolder: false);
            tab.Language = LanguageClassifier.Classify(filePath);
            tab.IsModified = false;
            Settings.LastFolderPath = Path.GetDirectoryName(filePath) ?? Settings.LastFolderPath;
            OnPropertyChanged(nameof(WindowTitle));
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Save File Error", $"Error saving file: {ex.Message}");
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
            OpenTabs.Add(new EditorTab());

        if (ReferenceEquals(ActiveTab, tab) || ActiveTab == null)
            ActiveTab = OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
    }

    /// <summary>Loads the active tab's program into VICE without running it.</summary>
    public Task TransferToViceAsync() => SendToViceAsync(run: false);

    /// <summary>Loads and runs the active tab's program in VICE.</summary>
    public Task RunOnViceAsync() => SendToViceAsync(run: true);

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

    /// <summary>Persists settings to disk.</summary>
    public void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) { SetStatus($"Couldn't save settings: {ex.Message}", StatusType.Warning); }
    }

    #endregion

    #region Private Methods

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
