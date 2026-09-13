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

    [AvaloniaFact]
    public void MainWindow_UsesPetsciiFontForBasKindToo()
    {
        // Matches upstream v2.4.0's reverted font rule: .bas and .prg both always render PETSCII-
        // styled - only assembly is plain monospace. A .bas tab used to be treated as ASCII-only.
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.ActiveTab!.Kind = C64UFileKind.Bas;
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        Assert.Contains("Pet Me 64", editor.FontFamily.ToString());
    }

    [Fact]
    public void IsUpperCaseModeActive_WritesThroughToActiveTab_AndGatesByLanguage()
    {
        var vm = new MainViewModel();
        var basicTab = vm.ActiveTab!;

        Assert.True(vm.IsShiftModeApplicable);
        Assert.True(vm.IsUpperCaseModeActive);
        Assert.False(vm.IsLowerCaseModeActive);

        vm.IsLowerCaseModeActive = true;
        Assert.False(vm.IsUpperCaseModeActive);
        Assert.False(basicTab.IsUpperCaseModeActive);

        // An assembly tab never uses PETSCII rendering, so the mode has no effect there - it
        // keeps its own default and the setter is a no-op.
        var asmTab = vm.NewTab(EditorLanguage.Asm);
        vm.ActiveTab = asmTab;
        Assert.False(vm.IsShiftModeApplicable);
        vm.IsUpperCaseModeActive = false;
        Assert.True(asmTab.IsUpperCaseModeActive);

        // Switching back to the BASIC tab restores its own remembered mode.
        vm.ActiveTab = basicTab;
        Assert.True(vm.IsShiftModeApplicable);
        Assert.False(vm.IsUpperCaseModeActive);
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
