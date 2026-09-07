// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.C64U;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Folder explorer behaviour, against a throwaway folder tree on disk.
/// </summary>
public class ExplorerTests : IDisposable
{
    private static readonly string? _renderDir = Environment.GetEnvironmentVariable("READYCODE_RENDER_DIR");
    private readonly string _root;

    public ExplorerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"readycode-explorer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "games"));
        File.WriteAllText(Path.Combine(_root, "hello.bas"), "10 PRINT \"HELLO\"\n");
        File.WriteAllBytes(Path.Combine(_root, "hello.prg"), new PrgConverter().ConvertToPrg("10 PRINT \"HELLO\"\n"));
        File.WriteAllText(Path.Combine(_root, "games", "border.asm"), "; demo\n LDA #0\n STA $D020\n RTS\n");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "plain text");

        var disk = DiskImage.ForKind(C64UFileKind.D64);
        byte[] image = disk.CreateBlankImage("TESTDISK");
        image = disk.AddEntry(image, "GAME", C64UFileKind.Prg, new PrgConverter().ConvertToPrg("10 PRINT \"ON DISK\"\n"));
        File.WriteAllBytes(Path.Combine(_root, "games", "test.d64"), image);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void LoadFolder_ListsFoldersFirstThenFiles_WithKinds()
    {
        var vm = new MainViewModel();
        vm.LoadFolder(_root);

        Assert.Equal(Path.GetFileName(_root).ToUpperInvariant(), vm.ExplorerTitle);
        Assert.True(vm.IsFolderOpen);
        Assert.Equal(["games", "hello.bas", "hello.prg", "notes.txt"], vm.FolderItems.Select(i => i.Name));
        Assert.True(vm.FolderItems[0].IsFolder);
        Assert.Equal(C64UFileKind.Bas, vm.FolderItems[1].Kind);
        Assert.Equal(C64UFileKind.Prg, vm.FolderItems[2].Kind);
        Assert.Equal("PRG", vm.FolderItems[2].Badge);
    }

    [Fact]
    public void DiskImage_ExpandsToItsEntries_AndEntriesOpenAsVirtualTabs()
    {
        var vm = new MainViewModel();
        vm.LoadFolder(_root);

        var games = vm.FolderItems[0];
        games.IsExpanded = true;
        var d64 = games.Children.Single(c => c.Name == "test.d64");
        Assert.True(d64.IsDiskImage);

        d64.IsExpanded = true; // DeferToUiThread is synchronous in tests
        var entry = Assert.Single(d64.Children);
        Assert.Equal("GAME", entry.Name);
        Assert.True(entry.IsVirtual);

        Assert.True(vm.OpenVirtualEntry(entry));
        var tab = vm.ActiveTab!;
        Assert.True(tab.IsVirtual);
        Assert.Equal("GAME", tab.FileName);
        Assert.Contains("ON DISK", tab.Document.Text);

        // Saving writes the entry back into the image.
        tab.Document.Text = "10 PRINT \"CHANGED\"\n";
        Assert.True(vm.SaveVirtualTab(tab));
        Assert.False(tab.IsModified);
        var reread = DiskImage.ForKind(C64UFileKind.D64).ReadDirectory(File.ReadAllBytes(d64.FullPath)).Single();
        Assert.Contains("CHANGED", new PrgConverter().ConvertFromPrg(reread.Content));
    }

    [Fact]
    public void CreateRenameDelete_UpdateDiskAndTree()
    {
        var vm = new MainViewModel();
        vm.LoadFolder(_root);

        Assert.True(vm.CreateFolder(_root, "new folder"));
        Assert.Contains(vm.FolderItems, i => i.Name == "new folder" && i.IsFolder);

        Assert.True(vm.CreateFile(Path.Combine(_root, "new folder"), "fresh.prg"));
        Assert.Equal("fresh.prg", vm.ActiveTab!.FileName);
        Assert.True(File.Exists(Path.Combine(_root, "new folder", "fresh.prg")));

        var hello = vm.FolderItems.Single(i => i.Name == "hello.bas");
        vm.OpenFile(hello.FullPath);
        var helloTab = vm.ActiveTab!;
        Assert.True(vm.RenameItem(hello, "renamed.bas"));
        Assert.True(File.Exists(Path.Combine(_root, "renamed.bas")));
        Assert.Equal("renamed.bas", helloTab.FileName);

        var renamed = vm.FolderItems.Single(i => i.Name == "renamed.bas");
        Assert.True(vm.DeleteItem(renamed));
        Assert.False(File.Exists(renamed.FullPath));
        Assert.DoesNotContain(vm.OpenTabs, t => t.FileName == "renamed.bas");
        Assert.DoesNotContain(vm.FolderItems, i => i.Name == "renamed.bas");
    }

    [AvaloniaFact]
    public void MainWindow_RendersExplorer()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 450 };
        window.Show();

        vm.LoadFolder(_root);
        vm.IsExplorerOpen = true;
        vm.FolderItems[0].IsExpanded = true;
        vm.OpenFile(Path.Combine(_root, "hello.prg"));

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        if (!string.IsNullOrEmpty(_renderDir))
        {
            Directory.CreateDirectory(_renderDir);
            frame!.Save(Path.Combine(_renderDir, "main-window-explorer.png"));
        }
    }
}
