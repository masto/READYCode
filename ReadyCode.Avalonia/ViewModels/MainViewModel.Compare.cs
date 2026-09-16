// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Avalonia.Models;
using ReadyCode.Diff;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// File Compare: "Select file for comparison" on one explorer item, then "Compare file" on
/// another, opens a read-only diff tab. Each side is resolved to text on its own (a .prg is
/// detokenized, a machine-language program disassembled, as opening it would), so any two
/// comparable kinds can be compared. The resolving and diffing is the shared
/// <see cref="CompareFileResolver"/> and <see cref="FileCompareEngine"/>.
/// </summary>
public partial class MainViewModel
{
    #region Private Fields

    private ComparableFileRef? _pendingCompareFile;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the file chosen with "Select file for comparison", waiting for the other side, or
    /// null. Selecting the same file again clears it.
    /// </summary>
    public ComparableFileRef? PendingCompareFile
    {
        get => _pendingCompareFile;
        private set
        {
            if (ReferenceEquals(_pendingCompareFile, value)) return;
            _pendingCompareFile = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Makes <paramref name="file"/> the pending left side of a comparison - or, if it already
    /// is, clears the selection, which is how a pending comparison is cancelled.
    /// </summary>
    public void SelectFileForCompare(ComparableFileRef file)
    {
        if (file.IsSameFile(PendingCompareFile))
        {
            PendingCompareFile = null;
            SetStatus("Comparison selection cleared.");
        }
        else
        {
            PendingCompareFile = file;
            SetStatus($"Selected {file.Name} for comparison - right-click another file and choose Compare file.");
        }
    }

    /// <summary>
    /// Whether <paramref name="file"/> can be compared with the pending one - both must be a
    /// kind File Compare can resolve to text.
    /// </summary>
    public bool CanCompareWithPending(ComparableFileRef file) =>
        PendingCompareFile != null && CompareFileResolver.CanCompare(PendingCompareFile, file);

    /// <summary>Compares the pending file with <paramref name="right"/> in a new tab, and clears the pending selection.</summary>
    public async Task<EditorTab?> CompareWithPendingAsync(ComparableFileRef right)
    {
        if (PendingCompareFile is not { } left) return null;
        var tab = await OpenCompareTabAsync(left, right);
        if (tab != null) PendingCompareFile = null;
        return tab;
    }

    /// <summary>
    /// Resolves both files to text, diffs them, and opens the result in a new read-only tab.
    /// </summary>
    public async Task<EditorTab?> OpenCompareTabAsync(ComparableFileRef left, ComparableFileRef right)
    {
        if (!CompareFileResolver.CanCompare(left, right))
        {
            ErrorRaised?.Invoke("Compare Files", "These two files can't be compared - one or both are an unsupported file type.");
            return null;
        }

        try
        {
            byte[] leftBytes = await ReadComparableFileBytesAsync(left);
            byte[] rightBytes = await ReadComparableFileBytesAsync(right);

            var leftResolved = CompareFileResolver.Resolve(left.Name, leftBytes, left.Kind);
            var rightResolved = CompareFileResolver.Resolve(right.Name, rightBytes, right.Kind);

            if (leftResolved.Warning != null && rightResolved.Warning != null)
            {
                ErrorRaised?.Invoke("Compare Files", $"Could not compare these files:{Environment.NewLine}{leftResolved.Warning}{Environment.NewLine}{rightResolved.Warning}");
                return null;
            }

            var result = FileCompareEngine.Compute(
                leftResolved.DisplayName, leftResolved.Text, leftResolved.IsAsciiStyled, leftResolved.Warning,
                rightResolved.DisplayName, rightResolved.Text, rightResolved.IsAsciiStyled, rightResolved.Warning,
                ignoreWhitespace: false);

            var tab = new EditorTab
            {
                DisplayName = $"{left.Name} ↔ {right.Name}",
                CompareResult = result,
            };
            tab.IsModified = false;
            AddTab(tab);
            ActiveTab = tab;
            SetStatus($"Compared {left.Name} with {right.Name}.");
            return tab;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Compare Files", $"Error comparing files: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Private Methods

    private async Task<byte[]> ReadComparableFileBytesAsync(ComparableFileRef fileRef)
    {
        if (fileRef.VirtualContent != null) return fileRef.VirtualContent;
        if (fileRef.Source == ComparableFileSource.Local) return File.ReadAllBytes(fileRef.FullPath);
        if (C64UFtp == null) throw new InvalidOperationException("Not connected to a C64 Ultimate.");
        return await C64UFtp.DownloadBytesAsync(fileRef.FullPath);
    }

    #endregion
}
