// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Tests for <see cref="MainViewModel"/>'s tab and file handling.
/// </summary>
public class MainViewModelTests
{
    #region Public Methods

    [Fact]
    public void NewBasicTab_DefaultsToPrgKind_SoItRendersAsPetsciiFromTheStart()
    {
        var vm = new MainViewModel();

        Assert.Equal(C64UFileKind.Prg, vm.ActiveTab!.Kind);
        Assert.Equal("Untitled.prg", vm.ActiveTab.FileName);

        var asm = vm.NewTab(EditorLanguage.Asm);
        Assert.Equal(C64UFileKind.Asm, asm.Kind);
        Assert.Equal("Untitled.asm", asm.FileName);
    }

    [AvaloniaFact]
    public void MainWindow_UsesPetsciiFontForNewTab_AndMonospaceForAsm()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        Assert.Contains("Pet Me 64", editor.FontFamily.ToString());

        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Pet Me 64", editor.FontFamily.ToString());
    }

    [Fact]
    public void OpenFile_AlreadyOpen_ReloadsFromDiskIntoTheSameTab()
    {
        string path = Path.Combine(Path.GetTempPath(), $"readycode-test-{Guid.NewGuid():N}.bas");
        File.WriteAllText(path, "10 PRINT \"SAVED\"\n");
        try
        {
            var vm = new MainViewModel();
            Assert.True(vm.OpenFile(path));
            var tab = vm.ActiveTab!;
            Assert.Single(vm.OpenTabs); // replaced the pristine untitled tab

            tab.Document.Text = "10 PRINT \"EDITED\"\n";
            Assert.True(tab.IsModified);

            Assert.True(vm.OpenFile(path));

            Assert.Same(tab, vm.ActiveTab);
            Assert.Single(vm.OpenTabs);
            Assert.Equal("10 PRINT \"SAVED\"\n", tab.Document.Text);
            Assert.False(tab.IsModified);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion
}
