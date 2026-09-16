// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Assembler;
using ReadyCode.C64U;
using ReadyCode.Diff;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The explorer's file operations behind Cut/Copy/Paste and drag-and-drop: moving and copying
/// files and folders within the open folder or in from outside it, and embedding a file into a
/// disk image (tokenized or assembled on the way in). The window decides what a gesture means;
/// this does it and keeps the tree and any open tabs right. Matches WPF's rules: a name clash
/// skips that item with a message rather than overwriting.
/// </summary>
public partial class MainViewModel
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the path put on the clipboard by Cut, so a Paste of it moves rather than
    /// copies. The OS clipboard carries only the file, not the intent - Windows' own "move"
    /// flag has no portable equivalent - so the intent lives here, for this app's own Paste.
    /// </summary>
    public string? PendingCutPath { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Whether <paramref name="source"/> can be dropped on <paramref name="target"/>: onto a
    /// folder that isn't its own parent or inside itself (a move), or a single non-image file
    /// onto a disk image (an embed).
    /// </summary>
    public static bool IsValidDrop(FileTreeItem source, FileTreeItem target)
    {
        if (target.Kind.IsDiskImageKind()) return !source.IsFolder && !source.Kind.IsDiskImageKind() && !source.IsVirtual;
        if (!target.IsFolder || source.IsVirtual) return false;
        return IsValidMove(source.FullPath, source.IsFolder, target.FullPath);
    }

    /// <summary>Whether a file or folder can be moved into <paramref name="targetFolderPath"/>: not where it already is, and not into itself.</summary>
    public static bool IsValidMove(string sourcePath, bool sourceIsFolder, string targetFolderPath)
    {
        if (string.Equals(Path.GetDirectoryName(sourcePath), targetFolderPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return false;
        if (sourceIsFolder)
        {
            string srcPrefix = sourcePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string tgtPrefix = targetFolderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (tgtPrefix.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Moves or copies each of <paramref name="sourcePaths"/> into <paramref name="targetFolderPath"/>
    /// - Paste and an outside drop. An item whose name is already taken there is skipped with a
    /// message. Open tabs on a moved file follow it.
    /// </summary>
    /// <returns>The number of items moved or copied.</returns>
    public int TransferIntoFolder(IEnumerable<string> sourcePaths, string targetFolderPath, bool move)
    {
        int done = 0;
        var touchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetFolderPath };
        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            bool isFolder = Directory.Exists(sourcePath);
            if (!isFolder && !File.Exists(sourcePath)) continue;

            string itemName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
            string destination = Path.Combine(targetFolderPath, itemName);
            string verb = move ? "Move" : "Paste";

            if (move && !IsValidMove(sourcePath, isFolder, targetFolderPath))
            {
                ErrorRaised?.Invoke($"{verb} Failed", $"\"{itemName}\" can't be moved there.");
                continue;
            }
            if ((isFolder && Directory.Exists(destination)) || (!isFolder && File.Exists(destination)))
            {
                ErrorRaised?.Invoke($"{verb} Failed", $"A {(isFolder ? "folder" : "file")} named \"{itemName}\" already exists in \"{Path.GetFileName(targetFolderPath)}\".");
                continue;
            }

            try
            {
                if (move)
                {
                    if (isFolder) Directory.Move(sourcePath, destination);
                    else File.Move(sourcePath, destination);
                    UpdateOpenTabPathsAfterMove(sourcePath, destination);
                    if (Path.GetDirectoryName(sourcePath) is { } sourceParent) touchedFolders.Add(sourceParent);
                }
                else
                {
                    if (isFolder) CopyDirectoryRecursive(sourcePath, destination);
                    else File.Copy(sourcePath, destination);
                }
                done++;
            }
            catch (Exception ex)
            {
                ErrorRaised?.Invoke($"{verb} Failed", $"Could not {verb.ToLowerInvariant()} \"{itemName}\": {ex.Message}");
            }
        }

        if (done > 0)
        {
            foreach (string folder in touchedFolders) RefreshFolder(folder);
            if (FindItemByPath(targetFolderPath) is { } target) target.IsExpanded = true;
            SetStatus(move ? $"Moved {done} item{(done == 1 ? "" : "s")}." : $"Copied {done} item{(done == 1 ? "" : "s")}.");
        }
        return done;
    }

    /// <summary>
    /// Turns a file's bytes into the PRG bytes a disk-image entry holds: tokenizing BASIC
    /// source, assembling assembly source, passing a program through as is. Assembly errors
    /// are reported and fail it.
    /// </summary>
    public bool TryBuildDiskEntryPrgData(byte[] sourceBytes, C64UFileKind sourceKind, out byte[]? prgData)
    {
        if (sourceKind == C64UFileKind.Asm)
        {
            var result = new Asm6502Assembler().Assemble(
                CompareFileResolver.DecodeSourceText(sourceBytes), Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);
            if (!result.Success)
            {
                ErrorRaised?.Invoke("Assembly Errors", string.Join(Environment.NewLine, result.Errors.Select(e => $"Line {e.LineNumber}: {e.Message}")));
                prgData = null;
                return false;
            }
            prgData = result.PrgBytes;
            return true;
        }

        prgData = sourceKind == C64UFileKind.Bas
            ? new PrgConverter().ConvertToPrg(CompareFileResolver.DecodeSourceText(sourceBytes))
            : sourceBytes;
        return true;
    }

    /// <summary>Embeds a local file into a local disk image as a new entry named after it.</summary>
    public bool AddFileToLocalDiskImage(string sourcePath, FileTreeItem diskItem)
    {
        if (Directory.Exists(sourcePath))
        {
            ErrorRaised?.Invoke("Add File Failed", $"\"{Path.GetFileName(sourcePath)}\" is a folder and can't be added to a disk image.");
            return false;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            var kind = FileClassifier.Classify(sourcePath, isFolder: false, () => bytes);
            return AddBytesToLocalDiskImage(bytes, kind, Path.GetFileNameWithoutExtension(sourcePath), diskItem);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Add File Failed", $"Could not add \"{Path.GetFileName(sourcePath)}\" to {diskItem.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Embeds a file's bytes, converted per <paramref name="kind"/>, into a local disk image.</summary>
    public bool AddBytesToLocalDiskImage(byte[] sourceBytes, C64UFileKind kind, string entryName, FileTreeItem diskItem)
    {
        if (!TryBuildDiskEntryPrgData(sourceBytes, kind, out byte[]? prgData)) return false;

        try
        {
            var entryKind = FileClassifier.Classify(entryName + ".prg", isFolder: false, () => prgData!);
            byte[] diskBytes = File.ReadAllBytes(diskItem.FullPath);
            byte[] updated = DiskImage.ForKind(diskItem.Kind).AddEntry(diskBytes, entryName, entryKind, prgData!);
            File.WriteAllBytes(diskItem.FullPath, updated);
            diskItem.RefreshChildren();
            diskItem.IsExpanded = true;
            SetStatus($"Added {entryName} to {diskItem.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Add File Failed", $"Could not add \"{entryName}\" to {diskItem.Name}: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Private Methods

    private void RefreshFolder(string folderPath)
    {
        if (!IsFolderOpen) return;
        string root = RootFolderPath.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(folderPath.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            RefreshRootItems();
        else
            FindItemByPath(folderPath)?.RefreshChildren();
    }

    private void UpdateOpenTabPathsAfterMove(string movedFrom, string destination)
    {
        string prefix = movedFrom.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var tab in OpenTabs)
        {
            if (string.IsNullOrEmpty(tab.FilePath)) continue;
            if (tab.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination + tab.FilePath[movedFrom.Length..];
            else if (string.Equals(tab.FilePath, movedFrom, StringComparison.OrdinalIgnoreCase))
                tab.FilePath = destination;
        }
        OnPropertyChanged(nameof(WindowTitle));
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (string dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    #endregion
}
