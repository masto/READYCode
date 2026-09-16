// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.C64U;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The explorers' Cut/Copy/Paste and drag-and-drop, as in WPF: within the local tree a drag
/// onto a folder moves and onto a disk image embeds; the C64U tree does the same on the device;
/// files dragged in from the OS are copied into a folder, embedded into an image, uploaded to the
/// C64U, or - dropped anywhere else - opened as tabs. Cut/Copy put the real file on the OS
/// clipboard, so it pastes into the Finder or Explorer too; the "move" intent of Cut, which
/// has no portable clipboard flag, is remembered in the app. The file operations themselves
/// are the view model's - see MainViewModel.FileOps.cs.
/// </summary>
public partial class MainWindow
{
    #region Private Fields

    private const double _dragThreshold = 4;
    internal static readonly DataFormat<FileTreeItem> LocalItemFormat = DataFormat.CreateInProcessFormat<FileTreeItem>("readycode.local-item");
    private static readonly DataFormat<C64UFileItem> _c64uItemFormat = DataFormat.CreateInProcessFormat<C64UFileItem>("readycode.c64u-item");

    private object? _dragCandidate;
    private PointerPressedEventArgs? _dragPress;
    private Point _dragStart;
    private object? _currentDropTarget;
    private bool _headerIsDropTarget;

    #endregion

    #region Private Methods - Setup

    private void InstallExplorerDragDrop()
    {
        foreach (var tree in new TreeView[] { FileTree, C64UFileTree })
        {
            tree.AddHandler(PointerPressedEvent, Tree_PointerPressed, RoutingStrategies.Tunnel);
            tree.AddHandler(PointerMovedEvent, Tree_PointerMoved, RoutingStrategies.Tunnel);
            tree.AddHandler(PointerReleasedEvent, (_, _) => { _dragCandidate = null; _dragPress = null; }, RoutingStrategies.Tunnel);
        }

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearDropHighlight());
        AddHandler(DragDrop.DropEvent, Window_Drop);
    }

    #endregion

    #region Private Methods - Drag source

    private void Tree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;
        _dragPress = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A disk-image entry has no file of its own to move, so it can't be dragged.
        object? item = (e.Source as Control)?.DataContext;
        bool draggable = item switch
        {
            FileTreeItem local => !local.IsVirtual && local.FullPath.Length > 0,
            C64UFileItem remote => !remote.IsVirtual,
            _ => false,
        };
        if (!draggable) return;

        _dragCandidate = item;
        _dragPress = e;
        _dragStart = e.GetPosition(this);
    }

    private async void Tree_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate == null || _dragPress == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragCandidate = null; return; }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < _dragThreshold && Math.Abs(delta.Y) < _dragThreshold) return;

        object item = _dragCandidate;
        var press = _dragPress;
        _dragCandidate = null;
        _dragPress = null;

        var data = new DataTransfer();
        switch (item)
        {
            case FileTreeItem local:
                data.Add(DataTransferItem.Create(LocalItemFormat, local));
                // The real file too, so the same drag can land in the Finder / Explorer.
                if (await StorageItemForPathAsync(local.FullPath) is { } storageItem)
                    data.Add(DataTransferItem.CreateFile(storageItem));
                break;
            case C64UFileItem remote:
                data.Add(DataTransferItem.Create(_c64uItemFormat, remote));
                break;
            default:
                return;
        }

        // Both effects are offered: DragOver decides Move (folder) or Copy (disk image).
        await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move | DragDropEffects.Copy);
        ClearDropHighlight();
    }

    private async Task<IStorageItem?> StorageItemForPathAsync(string path)
    {
        try
        {
            var uri = new Uri(path);
            return Directory.Exists(path)
                ? await StorageProvider.TryGetFolderFromPathAsync(uri)
                : await StorageProvider.TryGetFileFromPathAsync(uri);
        }
        catch { return null; }
    }

    #endregion

    #region Private Methods - Drop target

    // What a drag is over: a local tree item, a C64U tree item, the local explorer's header
    // (the root folder), or nothing in particular.
    private object? FindDropTarget(DragEventArgs e)
    {
        for (var control = e.Source as Control; control != null; control = control.Parent as Control)
        {
            if (ReferenceEquals(control, ExplorerHeader)) return ExplorerHeader;
            if (control is TreeViewItem { DataContext: FileTreeItem or C64UFileItem } row) return row.DataContext;
            if (control is TreeView) break;
        }
        return null;
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        var dragged = DraggedItem(e);
        var target = FindDropTarget(e);
        var osFiles = dragged == null && e.DataTransfer.Contains(DataFormat.File);

        DragDropEffects effect = (dragged, target) switch
        {
            (FileTreeItem local, FileTreeItem t) when MainViewModel.IsValidDrop(local, t) => t.Kind.IsDiskImageKind() ? DragDropEffects.Copy : DragDropEffects.Move,
            (FileTreeItem local, Border) when ViewModel.IsFolderOpen && MainViewModel.IsValidMove(local.FullPath, local.IsFolder, ViewModel.RootFolderPath) => DragDropEffects.Move,
            (C64UFileItem remote, C64UFileItem t) when MainViewModel.IsValidC64UDrop(remote, t) => t.Kind.IsDiskImageKind() ? DragDropEffects.Copy : DragDropEffects.Move,
            (null, FileTreeItem t) when osFiles && (t.IsFolder || t.Kind.IsDiskImageKind()) && !t.IsVirtual => DragDropEffects.Copy,
            (null, Border) when osFiles && ViewModel.IsFolderOpen => DragDropEffects.Copy,
            (null, C64UFileItem t) when osFiles && (t.IsFolder || t.Kind.IsDiskImageKind()) && !t.IsVirtual => DragDropEffects.Copy,
            (null, _) when osFiles => DragDropEffects.Copy, // opened as tabs
            _ => DragDropEffects.None,
        };

        e.DragEffects = effect;
        e.Handled = true;
        SetDropHighlight(effect == DragDropEffects.None ? null : target);
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        var dragged = DraggedItem(e);
        var target = FindDropTarget(e);
        ClearDropHighlight();
        e.Handled = true;

        switch (dragged, target)
        {
            case (FileTreeItem local, FileTreeItem t) when MainViewModel.IsValidDrop(local, t):
                if (t.Kind.IsDiskImageKind()) ViewModel.AddFileToLocalDiskImage(local.FullPath, t);
                else ViewModel.TransferIntoFolder([local.FullPath], t.FullPath, move: true);
                return;
            case (FileTreeItem local, Border) when ViewModel.IsFolderOpen:
                ViewModel.TransferIntoFolder([local.FullPath], ViewModel.RootFolderPath, move: true);
                return;
            case (C64UFileItem remote, C64UFileItem t) when MainViewModel.IsValidC64UDrop(remote, t):
                if (t.Kind.IsDiskImageKind()) await ViewModel.AddC64UItemToC64UDiskImageAsync(remote, t);
                else await ViewModel.MoveC64UItemAsync(remote, t);
                return;
            case (not null, _):
                return;
        }

        var paths = DroppedPaths(e);
        if (paths.Count == 0) return;

        switch (target)
        {
            case FileTreeItem { IsFolder: true } folder:
                ViewModel.TransferIntoFolder(paths, folder.FullPath, move: false);
                return;
            case FileTreeItem image when image.Kind.IsDiskImageKind() && !image.IsVirtual:
                foreach (string path in paths) ViewModel.AddFileToLocalDiskImage(path, image);
                return;
            case Border when ViewModel.IsFolderOpen:
                ViewModel.TransferIntoFolder(paths, ViewModel.RootFolderPath, move: false);
                return;
            case C64UFileItem { IsFolder: true, IsVirtual: false } remoteFolder:
                foreach (string path in paths.Where(File.Exists))
                    await ViewModel.UploadFileToC64UAsync(remoteFolder.FullPath, Path.GetFileName(path), await File.ReadAllBytesAsync(path));
                return;
            case C64UFileItem remoteImage when remoteImage.Kind.IsDiskImageKind() && !remoteImage.IsVirtual:
                foreach (string path in paths.Where(File.Exists))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path);
                    await ViewModel.AddFileToC64UDiskImageAsync(remoteImage, Path.GetFileName(path), bytes, FileClassifier.Classify(path, isFolder: false, () => bytes));
                }
                return;
        }

        // Anywhere else: open the files, as File > Open would.
        foreach (string path in paths.Where(File.Exists))
        {
            var kind = FileClassifier.Classify(path, isFolder: false);
            if (kind is C64UFileKind.Bas or C64UFileKind.Prg or C64UFileKind.Asm or C64UFileKind.Ml)
                ViewModel.OpenFile(path);
        }
    }

    private static object? DraggedItem(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(LocalItemFormat) as object ?? e.DataTransfer.TryGetValue(_c64uItemFormat);

    private static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    private void SetDropHighlight(object? target)
    {
        if (ReferenceEquals(target, _currentDropTarget)) return;
        ClearDropHighlight();
        _currentDropTarget = target;
        switch (target)
        {
            case FileTreeItem local: local.IsDropTarget = true; break;
            case C64UFileItem remote: remote.IsDropTarget = true; break;
            case Border:
                _headerIsDropTarget = true;
                ExplorerHeader.Background = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC1, 0x07));
                break;
        }
    }

    private void ClearDropHighlight()
    {
        switch (_currentDropTarget)
        {
            case FileTreeItem local: local.IsDropTarget = false; break;
            case C64UFileItem remote: remote.IsDropTarget = false; break;
        }
        _currentDropTarget = null;
        if (_headerIsDropTarget)
        {
            _headerIsDropTarget = false;
            ExplorerHeader.Bind(BackgroundProperty, ExplorerHeader.GetResourceObservable("ThemeFolderExplorerHeaderBg"));
        }
    }

    #endregion

    #region Private Methods - Clipboard

    internal async Task CutOrCopyItemAsync(FileTreeItem item, bool cut)
    {
        if (item.IsVirtual || Clipboard == null) return;
        if (await StorageItemForPathAsync(item.FullPath) is not { } storageItem) return;

        await Clipboard.SetFilesAsync([storageItem]);
        ViewModel.PendingCutPath = cut ? item.FullPath : null;
        ViewModel.SetStatus(cut ? $"Cut {item.Name} - paste it into a folder to move it." : $"Copied {item.Name}.");
    }

    // Pastes the clipboard's files into a folder: a move if they are what Cut put there, a copy
    // otherwise (including files copied in the Finder / Explorer).
    internal async Task PasteIntoFolderAsync(string folderPath)
    {
        var paths = await ClipboardFilePathsAsync();
        if (paths.Count == 0)
        {
            ViewModel.SetStatus("There are no files on the clipboard to paste.", StatusType.Warning);
            return;
        }

        bool move = ViewModel.PendingCutPath != null && paths.Any(p => string.Equals(p, ViewModel.PendingCutPath, StringComparison.OrdinalIgnoreCase));
        int done = ViewModel.TransferIntoFolder(paths, folderPath, move);
        if (move && done > 0)
        {
            ViewModel.PendingCutPath = null;
            if (Clipboard != null) await Clipboard.ClearAsync();
        }
    }

    private async Task<List<string>> ClipboardFilePathsAsync()
    {
        if (Clipboard == null) return [];
        try
        {
            return (await Clipboard.TryGetFilesAsync())?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        }
        catch { return []; }
    }

    // The Paste item is added disabled and enabled once the clipboard has been asked, since
    // the menu is built synchronously and the clipboard is only readable asynchronously.
    private MenuItem MakePasteItem(string header, string folderPath)
    {
        var item = new MenuItem { Header = header, IsEnabled = false, Command = new AsyncCommand(() => PasteIntoFolderAsync(folderPath)) };
        _ = EnableIfClipboardHasFilesAsync(item);
        return item;
    }

    private async Task EnableIfClipboardHasFilesAsync(MenuItem item)
    {
        item.IsEnabled = (await ClipboardFilePathsAsync()).Count > 0;
    }

    #endregion
}
