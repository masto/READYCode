// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The Search panel: find and replace across the open folder's files.
/// </summary>
public class ProjectSearchTests
{
    #region Public Methods

    [AvaloniaFact]
    public void Search_FindsMatchesAcrossBasPrgAndAsm_ByLineAndColumn()
    {
        var (_, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "one.bas"), "10 PRINT \"SCORE\"\n20 SC=SC+1\n30 PRINT SC");
            File.WriteAllBytes(Path.Combine(dir, "two.prg"), new PrgConverter().ConvertToPrg("10 SC=0\n20 END"));
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "three.asm"), "sc = $c000\n        lda sc");
            File.WriteAllText(Path.Combine(dir, "ignored.d64"), "sc sc sc");
            vm.LoadFolder(dir);

            Assert.Equal(6, vm.RunProjectSearch("sc", matchCase: false, wholeWord: true, useRegex: false));
            Assert.Equal("6 results in 3 files.", vm.SearchStatusText);

            var one = vm.SearchResults.Single(f => f.RelativeDisplayPath == "one.bas");
            Assert.Equal([2, 2, 3], one.Matches.Select(m => m.LineNumber));
            Assert.Equal([3, 6, 9], one.Matches.Select(m => m.ColumnOffset));
            Assert.Equal("Line 2: 20 SC=SC+1", one.Matches[0].DisplayText);

            // The .prg is searched as it opens - with its line numbers padded - so the column
            // lines up with the tab.
            vm.Settings.LineNumberPadding = 4;
            vm.RunProjectSearch("sc", false, true, false);
            var two = vm.SearchResults.Single(f => f.RelativeDisplayPath == "two.prg");
            Assert.Equal("Line 1: 0010 SC=0", two.Matches.Single().DisplayText);
            Assert.Equal(5, two.Matches.Single().ColumnOffset);

            Assert.Equal(4, vm.RunProjectSearch("SC", matchCase: true, wholeWord: true, useRegex: false)); // not the .asm's lower case
            Assert.Equal(1, vm.RunProjectSearch("^sc", matchCase: false, wholeWord: false, useRegex: true)); // only the .asm's constant line
            Assert.Equal(0, vm.RunProjectSearch("nothing here", false, false, false));
            Assert.Equal("No results found.", vm.SearchStatusText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void ReplaceAll_EditsOpenTabsThroughTheirDocument_AndRewritesClosedFiles()
    {
        var (_, vm, dir) = ShowWithFolder();
        try
        {
            string openPath = Path.Combine(dir, "open.bas");
            string closedPath = Path.Combine(dir, "closed.prg");
            File.WriteAllText(openPath, "10 SC=1\n20 PRINT SC");
            File.WriteAllBytes(closedPath, new PrgConverter().ConvertToPrg("10 SC=2"));
            vm.LoadFolder(dir);
            Assert.True(vm.OpenFile(openPath));
            var openTab = vm.ActiveTab!;

            Assert.Equal(3, vm.RunProjectSearch("SC", true, true, false));
            Assert.Equal(2, vm.ReplaceAllInProject("TOTAL"));

            Assert.Equal("10 TOTAL=1\n20 PRINT TOTAL", openTab.Document.Text);
            Assert.True(openTab.IsModified);
            Assert.Equal("10 SC=1\n20 PRINT SC", File.ReadAllText(openPath)); // on disk only after a save
            Assert.Equal("10 TOTAL=2", new PrgConverter().ConvertFromPrg(File.ReadAllBytes(closedPath)).TrimEnd('\n', '\r'));

            // The search reads files as they are on disk (as WPF), so the open tab's unsaved
            // replacements aren't seen until it is saved - its two matches are still found.
            Assert.Equal(2, vm.RunProjectSearch("SC", true, true, false));
            Assert.True(vm.SaveTab(openTab, openPath));
            Assert.Equal(0, vm.RunProjectSearch("SC", true, true, false));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void FindInFiles_OpensTheSearchTab_AndReplaceInFilesShowsTheReplaceRow()
    {
        var (window, vm, dir) = ShowWithFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.bas"), "10 PRINT \"HELLO\"\n20 GOTO 10");
            vm.LoadFolder(dir);
            vm.IsExplorerOpen = false;

            var edit = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Edit").Menu!;
            NativeMenuItem Item(string h) => edit.Items.OfType<NativeMenuItem>().Single(i => i.Header == h);
            Assert.NotNull(Item("Find in Files").Gesture);
            Assert.NotNull(Item("Replace in Files").Gesture);

            ((INativeMenuItemExporterEventsImplBridge)Item("Find in Files")).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsExplorerOpen);
            Assert.True(vm.IsSearchTabActive);
            Assert.True(window.FindControl<DockPanel>("SearchPanel")!.IsVisible);
            Assert.False(window.FindControl<DockPanel>("SearchReplaceRow")!.IsVisible);

            ((INativeMenuItemExporterEventsImplBridge)Item("Replace in Files")).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<DockPanel>("SearchReplaceRow")!.IsVisible);
            Assert.True(window.FindControl<ToggleButton>("SearchReplaceExpandBtn")!.IsChecked);

            window.FindControl<TextBox>("SearchQueryBox")!.Text = "print";
            Assert.Equal(1, window.RunProjectSearch());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "1 result in 1 file.");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Line 1: 10 PRINT \"HELLO\"");
            RenderCapture.Save(window, "project-search.png");

            // Going to the match opens the file and selects the match.
            var match = vm.SearchResults.Single().Matches.Single();
            window.GoToSearchMatch(match);
            Dispatcher.UIThread.RunJobs();
            var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            Assert.EndsWith("a.bas", vm.ActiveTab!.FilePath);
            Assert.Equal("PRINT", editor.SelectedText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, MainViewModel Vm, string Dir) ShowWithFolder()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        string dir = Path.Combine(Path.GetTempPath(), $"readycode-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (window, vm, dir);
    }

    #endregion
}
