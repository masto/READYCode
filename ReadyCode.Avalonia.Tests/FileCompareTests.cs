// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiffPlex.DiffBuilder.Model;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// File Compare: selecting two files in the explorer, the diff tab, and its split/unified
/// views, navigation, and whitespace toggle.
/// </summary>
public class FileCompareTests
{
    #region Public Methods

    [AvaloniaFact]
    public async Task SelectThenCompare_OpensADiffTab_AndClearsTheSelection()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.bas"), "10 PRINT \"A\"\n20 GOTO 10");
            File.WriteAllText(Path.Combine(dir, "b.bas"), "10 PRINT \"B\"\n15 REM NEW\n20 GOTO 10");
            vm.LoadFolder(dir);
            var a = vm.FolderItems.Single(i => i.Name == "a.bas");
            var b = vm.FolderItems.Single(i => i.Name == "b.bas");

            // Before anything is selected: only "Select file for comparison".
            var headers = window.ContextMenuHeadersFor(a);
            Assert.Contains("Select file for comparison", headers);
            Assert.DoesNotContain(headers, h => h.StartsWith("Compare with"));

            vm.SelectFileForCompare(ComparableFileRef.FromLocal(a));
            Assert.NotNull(vm.PendingCompareFile);
            Assert.Contains("Clear comparison selection", window.ContextMenuHeadersFor(a));
            Assert.Contains("Compare with a.bas", window.ContextMenuHeadersFor(b));

            var tab = await vm.CompareWithPendingAsync(ComparableFileRef.FromLocal(b));
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(tab);
            Assert.True(tab!.IsCompareMode);
            Assert.Equal("a.bas ↔ b.bas", tab.FileName);
            Assert.Same(tab, vm.ActiveTab);
            Assert.Null(vm.PendingCompareFile);

            var view = window.FindControl<FileCompareControl>("CompareView")!;
            Assert.True(view.IsVisible);
            Assert.False(window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.IsVisible);
            Assert.False(view.IsUnified);
            Assert.Equal(1, view.HunkCount); // the changed PRINT and the inserted REM are adjacent: one hunk
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "1 change");
            RenderCapture.Save(window, "compare-split.png");

            view.SetView(isUnified: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.CompareIsUnified);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "a.bas ↔ b.bas");
            RenderCapture.Save(window, "compare-unified.png");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task SelectingTheSameFileAgain_ClearsThePendingSelection()
    {
        var (_, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.bas"), "10 END");
            vm.LoadFolder(dir);
            var a = ComparableFileRef.FromLocal(vm.FolderItems.Single());
            vm.SelectFileForCompare(a);
            Assert.NotNull(vm.PendingCompareFile);
            vm.SelectFileForCompare(a);
            Assert.Null(vm.PendingCompareFile);
            Assert.Null(await vm.CompareWithPendingAsync(a));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task PrgAndBas_CompareAsDetokenizedText_AndIgnoreWhitespaceRecomputes()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "prog.prg"), new PrgConverter().ConvertToPrg("10 PRINT \"HI\"\n20 END"));
            File.WriteAllText(Path.Combine(dir, "prog.bas"), "10 PRINT  \"HI\"\n20 END");
            vm.LoadFolder(dir);
            var prg = ComparableFileRef.FromLocal(vm.FolderItems.Single(i => i.Name == "prog.prg"));
            var bas = ComparableFileRef.FromLocal(vm.FolderItems.Single(i => i.Name == "prog.bas"));

            var tab = await vm.OpenCompareTabAsync(prg, bas);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(tab);
            var view = window.FindControl<FileCompareControl>("CompareView")!;

            // Only the doubled space differs; ignoring whitespace makes the files identical.
            Assert.Equal(1, view.HunkCount);
            view.SetIgnoreWhitespace(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, view.HunkCount);
            Assert.True(tab!.CompareIgnoreWhitespace);
            Assert.All(tab.CompareResult!.SideBySide.OldText.Lines, l => Assert.Equal(ChangeType.Unchanged, l.Type));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task NextChange_ScrollsThePanesTogether()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            string same = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"{i * 10} REM LINE {i}"));
            File.WriteAllText(Path.Combine(dir, "a.bas"), same);
            File.WriteAllText(Path.Combine(dir, "b.bas"), same.Replace("REM LINE 150", "REM CHANGED"));
            vm.LoadFolder(dir);
            var a = ComparableFileRef.FromLocal(vm.FolderItems.Single(i => i.Name == "a.bas"));
            var b = ComparableFileRef.FromLocal(vm.FolderItems.Single(i => i.Name == "b.bas"));

            Assert.NotNull(await vm.OpenCompareTabAsync(a, b));
            Dispatcher.UIThread.RunJobs();
            var view = window.FindControl<FileCompareControl>("CompareView")!;
            var left = view.FindControl<AvaloniaEdit.TextEditor>("LeftEditor")!;
            var right = view.FindControl<AvaloniaEdit.TextEditor>("RightEditor")!;

            // Folded, the whole thing fits on screen (149 unchanged lines collapse to one row);
            // expanded, the change at line 150 is well below the fold.
            Assert.Equal(1, view.HunkCount);
            Assert.True(left.ExtentHeight < left.ViewportHeight);
            view.ExpandAll();
            Dispatcher.UIThread.RunJobs();
            Assert.True(left.ExtentHeight > left.ViewportHeight);

            view.NavigateHunk(1);
            Dispatcher.UIThread.RunJobs();
            Assert.True(left.VerticalOffset > 0, "left pane did not scroll to the change");
            Assert.Equal(left.VerticalOffset, right.VerticalOffset, 1.0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void CompareTab_IsNeitherBasicNorAssembly_AndCannotBeSaved()
    {
        var (_, vm, _) = ShowWithFolder();
        var tab = new Models.EditorTab
        {
            DisplayName = "x ↔ y",
            CompareResult = ReadyCode.Diff.FileCompareEngine.Compute("x", "1", false, null, "y", "2", false, null, false),
        };
        vm.OpenTabs.Add(tab);
        vm.ActiveTab = tab;
        Assert.False(vm.IsBasicTabActive);
        Assert.False(vm.IsAsmTabActive);
        Assert.False(vm.IsShiftModeApplicable);
        Assert.False(vm.SaveTab(tab, Path.Combine(Path.GetTempPath(), "never-written.bas")));
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, MainViewModel Vm, string Dir) ShowWithFolder()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        string dir = Path.Combine(Path.GetTempPath(), $"readycode-compare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (window, vm, dir);
    }

    #endregion
}
