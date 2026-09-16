// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.C64U;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The local explorer's file operations: moving and copying via paste and drag-and-drop,
/// embedding into disk images, and files dropped in from outside.
/// </summary>
public class ExplorerFileOpsTests
{
    #region Public Methods

    [AvaloniaFact]
    public void Move_CarriesOpenTabsAlong_AndRefusesItsOwnFolderOrSelf()
    {
        var (_, vm, dir) = ShowWithFolder();
        try
        {
            string file = Path.Combine(dir, "prog.bas");
            string sub = Path.Combine(dir, "sub");
            File.WriteAllText(file, "10 END");
            Directory.CreateDirectory(sub);
            vm.LoadFolder(dir);
            Assert.True(vm.OpenFile(file));

            Assert.False(MainViewModel.IsValidMove(file, false, dir));      // already there
            Assert.False(MainViewModel.IsValidMove(sub, true, sub));         // into itself
            Assert.True(MainViewModel.IsValidMove(file, false, sub));

            Assert.Equal(1, vm.TransferIntoFolder([file], sub, move: true));
            Assert.False(File.Exists(file));
            Assert.True(File.Exists(Path.Combine(sub, "prog.bas")));
            Assert.Equal(Path.Combine(sub, "prog.bas"), vm.ActiveTab!.FilePath);
            Assert.True(vm.FolderItems.Single(i => i.Name == "sub").IsExpanded);
            Assert.Contains(vm.FolderItems.Single(i => i.Name == "sub").Children, c => c.Name == "prog.bas");
            Assert.DoesNotContain(vm.FolderItems, i => i.Name == "prog.bas");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void Copy_SkipsANameClashWithAMessage_AndCopiesFoldersRecursively()
    {
        var (_, vm, dir) = ShowWithFolder();
        var errors = new List<string>();
        vm.ErrorRaised += (title, _) => errors.Add(title);
        try
        {
            string src = Path.Combine(dir, "src");
            Directory.CreateDirectory(Path.Combine(src, "nested"));
            File.WriteAllText(Path.Combine(src, "nested", "a.bas"), "10 END");
            File.WriteAllText(Path.Combine(dir, "clash.bas"), "original");
            Directory.CreateDirectory(Path.Combine(dir, "dest"));
            File.WriteAllText(Path.Combine(dir, "dest", "clash.bas"), "existing");
            vm.LoadFolder(dir);

            int done = vm.TransferIntoFolder([src, Path.Combine(dir, "clash.bas")], Path.Combine(dir, "dest"), move: false);

            Assert.Equal(1, done);
            Assert.Single(errors);
            Assert.Equal("existing", File.ReadAllText(Path.Combine(dir, "dest", "clash.bas")));
            Assert.Equal("10 END", File.ReadAllText(Path.Combine(dir, "dest", "src", "nested", "a.bas")));
            Assert.True(Directory.Exists(src)); // a copy, not a move
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void AddingABasFileToADiskImage_TokenizesIt_AndAnAsmFileIsAssembled()
    {
        var (_, vm, dir) = ShowWithFolder();
        try
        {
            string image = Path.Combine(dir, "disk.d64");
            File.WriteAllBytes(image, DiskImage.ForKind(C64UFileKind.D64).CreateBlankImage("TEST"));
            File.WriteAllText(Path.Combine(dir, "hello.bas"), "10 PRINT \"HI\"");
            File.WriteAllText(Path.Combine(dir, "code.asm"), "        * = $C000\n        RTS");
            vm.LoadFolder(dir);
            var diskItem = vm.FolderItems.Single(i => i.Name == "disk.d64");

            Assert.True(vm.AddFileToLocalDiskImage(Path.Combine(dir, "hello.bas"), diskItem));
            Assert.True(vm.AddFileToLocalDiskImage(Path.Combine(dir, "code.asm"), diskItem));
            Dispatcher.UIThread.RunJobs(); // disk-image entries load on a deferred job

            var entries = diskItem.Children.Where(c => c.IsVirtual).ToList();
            Assert.Contains(entries, e => e.Name.Equals("hello", StringComparison.OrdinalIgnoreCase) && e.Kind == C64UFileKind.Prg);
            Assert.Contains(entries, e => e.Name.Equals("code", StringComparison.OrdinalIgnoreCase) && e.Kind == C64UFileKind.Ml);
            var hello = entries.Single(e => e.Name.Equals("hello", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("10 PRINT \"HI\"", new PrgConverter().ConvertFromPrg(hello.Content!).TrimEnd('\n', '\r'));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void DraggingAFileOntoAFolder_MovesIt_AndOntoADiskImageEmbedsIt()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "prog.bas"), "10 END");
            File.WriteAllText(Path.Combine(dir, "other.bas"), "20 END");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllBytes(Path.Combine(dir, "disk.d64"), DiskImage.ForKind(C64UFileKind.D64).CreateBlankImage("TEST"));
            vm.LoadFolder(dir);
            Dispatcher.UIThread.RunJobs();

            var prog = vm.FolderItems.Single(i => i.Name == "prog.bas");
            var other = vm.FolderItems.Single(i => i.Name == "other.bas");
            var sub = vm.FolderItems.Single(i => i.Name == "sub");
            var disk = vm.FolderItems.Single(i => i.Name == "disk.d64");

            // Onto a folder: a move.
            window.SimulateTreeDrag(prog, RowCenter(window, sub));
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(Path.Combine(dir, "sub", "prog.bas")));
            Assert.False(prog.IsDropTarget);
            Assert.False(sub.IsDropTarget);

            // Onto a disk image: an embed, the original staying put. (The move refreshed the
            // root, so the items are looked up again.)
            other = vm.FolderItems.Single(i => i.Name == "other.bas");
            disk = vm.FolderItems.Single(i => i.Name == "disk.d64");
            window.SimulateTreeDrag(other, RowCenter(window, disk));
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(Path.Combine(dir, "other.bas")));
            Assert.Contains(disk.Children, c => c.IsVirtual && c.Name.Equals("other", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task DroppingOsFiles_CopiesIntoAFolder_OrOpensThemOverTheEditor()
    {
        var (window, vm, dir) = ShowWithFolder();
        string outside = Path.Combine(Path.GetTempPath(), $"readycode-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            string external = Path.Combine(outside, "ext.bas");
            File.WriteAllText(external, "10 PRINT \"EXT\"");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            vm.LoadFolder(dir);
            Dispatcher.UIThread.RunJobs();
            var sub = vm.FolderItems.Single(i => i.Name == "sub");

            var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(external));
            Assert.NotNull(file);
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file!));

            window.SimulateExternalDrop(data, RowCenter(window, sub));
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(Path.Combine(dir, "sub", "ext.bas")));
            Assert.True(File.Exists(external));

            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            window.SimulateExternalDrop(data, editor.TranslatePoint(new Point(100, 100), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(external, vm.ActiveTab!.FilePath);
            Assert.Equal("10 PRINT \"EXT\"", vm.ActiveTab.Document.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task CutThenPaste_Moves_AndCopyThenPaste_Copies()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "prog.bas"), "10 END");
            Directory.CreateDirectory(Path.Combine(dir, "a"));
            Directory.CreateDirectory(Path.Combine(dir, "b"));
            vm.LoadFolder(dir);

            await window.CutOrCopyItemAsync(vm.FolderItems.Single(i => i.Name == "prog.bas"), cut: true);
            Assert.NotNull(vm.PendingCutPath);
            await window.PasteIntoFolderAsync(Path.Combine(dir, "a"));
            Assert.False(File.Exists(Path.Combine(dir, "prog.bas")));
            Assert.True(File.Exists(Path.Combine(dir, "a", "prog.bas")));
            Assert.Null(vm.PendingCutPath); // a cut pastes once

            var moved = vm.FolderItems.Single(i => i.Name == "a").Children.Single(c => c.Name == "prog.bas");
            await window.CutOrCopyItemAsync(moved, cut: false);
            await window.PasteIntoFolderAsync(Path.Combine(dir, "b"));
            Assert.True(File.Exists(Path.Combine(dir, "a", "prog.bas")));
            Assert.True(File.Exists(Path.Combine(dir, "b", "prog.bas")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void ContextMenus_OfferCutCopyPaste_AndAddFileOnADiskImage()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "prog.bas"), "10 END");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllBytes(Path.Combine(dir, "disk.d64"), DiskImage.ForKind(C64UFileKind.D64).CreateBlankImage("TEST"));
            vm.LoadFolder(dir);

            var file = window.ContextMenuHeadersFor(vm.FolderItems.Single(i => i.Name == "prog.bas"));
            Assert.Contains("Cut", file);
            Assert.Contains("Copy", file);
            Assert.Contains("Paste", file);

            var folder = window.ContextMenuHeadersFor(vm.FolderItems.Single(i => i.Name == "sub"));
            Assert.Contains("Paste", folder);
            Assert.Contains("Cut", folder);

            var disk = window.ContextMenuHeadersFor(vm.FolderItems.Single(i => i.Name == "disk.d64"));
            Assert.Contains("Add File…", disk);
            Assert.Contains("Copy", disk);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    #endregion

    #region Private Methods

    private static Point RowCenter(MainWindow window, FileTreeItem item)
    {
        Dispatcher.UIThread.RunJobs();
        var tree = window.FindControl<TreeView>("FileTree")!;
        var row = tree.GetVisualDescendants().OfType<TreeViewItem>().First(r => ReferenceEquals(r.DataContext, item));
        var header = row.GetVisualDescendants().OfType<TextBlock>().First();
        return header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
    }

    private static (MainWindow Window, MainViewModel Vm, string Dir) ShowWithFolder()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 600 };
        window.Show();
        // The tests share one settings folder: make sure the explorer tab is the one showing.
        vm.ActiveLeftPanelTab = "Explorer";
        vm.IsExplorerOpen = true;
        Dispatcher.UIThread.RunJobs();
        string dir = Path.Combine(Path.GetTempPath(), $"readycode-fileops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (window, vm, dir);
    }

    #endregion
}

/// <summary>Drives the window's drag-and-drop handlers the way the platform would.</summary>
internal static class DragSimulation
{
    /// <summary>Drops a tree item at a point, with the in-process data the tree's own drag carries.</summary>
    public static void SimulateTreeDrag(this MainWindow window, FileTreeItem item, Point dropAt)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(MainWindow.LocalItemFormat, item));
        window.DragDrop(dropAt, RawDragEventType.DragEnter, data, DragDropEffects.Move | DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(dropAt, RawDragEventType.DragOver, data, DragDropEffects.Move | DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(dropAt, RawDragEventType.Drop, data, DragDropEffects.Move | DragDropEffects.Copy, RawInputModifiers.None);
    }

    /// <summary>Drops the given data (files from outside) at a point.</summary>
    public static void SimulateExternalDrop(this MainWindow window, IDataTransfer data, Point dropAt)
    {
        window.DragDrop(dropAt, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(dropAt, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(dropAt, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
    }
}
