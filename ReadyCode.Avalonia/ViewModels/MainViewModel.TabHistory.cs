// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using ReadyCode.Avalonia.Models;
using ReadyCode.C64U;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// File > Open Recent and Reopen Closed Tab, plus Import: the recent-files list is the one the
/// WPF app keeps in settings (<see cref="Settings.AppSettings.RecentFiles"/>), so the two apps
/// share it; the closed-tab history is in-memory for the session, as in WPF.
/// </summary>
public partial class MainViewModel
{
    // Everything needed to put a closed tab back exactly as it was - including unsaved text,
    // since the tab may never have matched disk, and the disk-image identity of a virtual tab.
    private sealed record ClosedTabSnapshot(
        string? FilePath, string? DisplayName, string? VirtualSourceId, bool IsC64UVirtual,
        C64UFileKind Kind, EditorLanguage Language, string Text, byte[]? RawBytes, bool WasModified, int CaretOffset,
        bool IsDisassemblyMode, DisassemblySource DisassemblySource, IReadOnlyDictionary<int, ushort>? DisassemblyLineAddresses);

    private const int _maxClosedTabHistory = 20;

    #region Private Fields

    private readonly List<ClosedTabSnapshot> _closedTabHistory = new();

    #endregion

    #region Public Events

    /// <summary>Raised after the recent-files list changes, so the menu can be rebuilt.</summary>
    public event Action? RecentFilesChanged;

    #endregion

    #region Public Properties

    /// <summary>Gets the recently opened or saved files, most recent first.</summary>
    public IReadOnlyList<string> RecentFiles => Settings.RecentFiles;

    /// <summary>Gets whether a tab has been closed this session and can be reopened.</summary>
    public bool HasClosedTabHistory => _closedTabHistory.Count > 0;

    #endregion

    #region Public Methods

    /// <summary>
    /// Restores the most recently closed tab, including any unsaved content it had at close
    /// time. Returns the reopened tab, or null if nothing has been closed yet this session.
    /// </summary>
    public EditorTab? ReopenClosedTab()
    {
        if (_closedTabHistory.Count == 0) return null;

        var snapshot = _closedTabHistory[^1];
        _closedTabHistory.RemoveAt(_closedTabHistory.Count - 1);
        OnPropertyChanged(nameof(HasClosedTabHistory));

        var tab = new EditorTab
        {
            FilePath = snapshot.FilePath,
            DisplayName = snapshot.DisplayName,
            VirtualSourceId = snapshot.VirtualSourceId,
            IsC64UVirtual = snapshot.IsC64UVirtual,
            Kind = snapshot.Kind,
            Language = snapshot.Language,
            RawBytes = snapshot.RawBytes,
            IsDisassemblyMode = snapshot.IsDisassemblyMode,
            DisassemblySource = snapshot.DisassemblySource,
            DisassemblyLineAddresses = snapshot.DisassemblyLineAddresses,
        };
        tab.Document.Text = snapshot.Text;
        tab.IsModified = snapshot.WasModified; // reset any spurious change event from document setup
        tab.CaretOffset = Math.Min(snapshot.CaretOffset, tab.Document.TextLength);
        AddTab(tab);
        ActiveTab = tab;
        return tab;
    }

    /// <summary>
    /// Opens a plain text file in a new, untitled BASIC tab, bypassing PETSCII and
    /// tokenization - the inverse of exporting.
    /// </summary>
    public bool ImportText(string path)
    {
        try
        {
            var tab = EditorTab.CreateNew(EditorLanguage.Basic);
            tab.Document.Text = File.ReadAllText(path, Encoding.UTF8);
            tab.IsModified = false;
            AddTab(tab);
            ActiveTab = tab;
            LastDialogFolder = Path.GetDirectoryName(path) ?? LastDialogFolder;
            SetStatus($"Imported {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Import Error", $"Error importing file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes the active tab's text as-is to a plain text file - no PETSCII mapping, no
    /// tokenization - so it can be read by anything.
    /// </summary>
    public bool ExportText(string path)
    {
        if (ActiveTab == null) return false;
        try
        {
            File.WriteAllText(path, ActiveTab.Document.Text, Encoding.UTF8);
            LastDialogFolder = Path.GetDirectoryName(path) ?? LastDialogFolder;
            SetStatus($"Exported {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Export Error", $"Error exporting file: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Private Methods

    private void TrackRecentFile(string path)
    {
        Settings.AddRecentFile(path);
        SaveSettings();
        RecentFilesChanged?.Invoke();
    }

    private void RememberClosedTab(EditorTab tab)
    {
        _closedTabHistory.Add(new ClosedTabSnapshot(
            tab.FilePath, tab.DisplayName, tab.VirtualSourceId, tab.IsC64UVirtual,
            tab.Kind, tab.Language, tab.Document.Text, tab.RawBytes, tab.IsModified, tab.CaretOffset,
            tab.IsDisassemblyMode, tab.DisassemblySource, tab.DisassemblyLineAddresses));
        if (_closedTabHistory.Count > _maxClosedTabHistory)
            _closedTabHistory.RemoveAt(0);
        OnPropertyChanged(nameof(HasClosedTabHistory));
    }

    #endregion
}
