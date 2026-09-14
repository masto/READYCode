// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The smaller File/Edit commands: Comment / Uncomment Selection, Make Uppercase / Lowercase,
/// Reopen Closed Tab, Open Recent, and Export / Import as text.
/// </summary>
public class EditCommandTests
{
    #region Public Methods

    [AvaloniaFact]
    public void CommentSelection_PrependsRemAfterTheLineNumber_AndUncommentRemovesIt()
    {
        var (window, _, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 PRINT \"A\"\n20GOTO 10\n30 REM DONE\n\n40 END";
        editor.Select(0, editor.Document.GetLineByNumber(4).EndOffset);

        window.ExecuteCommentSelection();
        Assert.Equal("10 REM PRINT \"A\"\n20 REM GOTO 10\n30 REM DONE\n\n40 END", editor.Document.Text);

        editor.Select(0, editor.Document.TextLength);
        window.ExecuteUncommentSelection();
        Assert.Equal("10 PRINT \"A\"\n20 GOTO 10\n30 DONE\n\n40 END", editor.Document.Text);
    }

    [AvaloniaFact]
    public void CommentSelection_WithNoSelection_CommentsTheCaretLineOnly()
    {
        var (window, _, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 PRINT \"A\"\n20 END";
        editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset + 3;

        window.ExecuteCommentSelection();
        Assert.Equal("10 PRINT \"A\"\n20 REM END", editor.Document.Text);
    }

    [AvaloniaFact]
    public void ChangeSelectionCase_ConvertsAndKeepsTheSelection()
    {
        var (window, _, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 print \"hello\"";
        editor.Select(3, 5);

        window.ExecuteChangeSelectionCase(upper: true);
        Assert.Equal("10 PRINT \"hello\"", editor.Document.Text);
        Assert.Equal("PRINT", editor.SelectedText);

        window.ExecuteChangeSelectionCase(upper: false);
        Assert.Equal("10 print \"hello\"", editor.Document.Text);

        editor.Select(0, 0);
        window.ExecuteChangeSelectionCase(upper: true);
        Assert.Equal("10 print \"hello\"", editor.Document.Text);
    }

    [AvaloniaFact]
    public void ReopenClosedTab_RestoresUnsavedText_ModifiedFlag_AndCaret()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        var tab = vm.ActiveTab!;
        editor.Document.Text = "10 PRINT \"UNSAVED\"";
        editor.CaretOffset = 5;
        tab.IsModified = true;
        Assert.False(vm.HasClosedTabHistory);

        tab.CaretOffset = editor.CaretOffset;
        vm.CloseTab(tab, rememberForReopen: true);
        Assert.True(vm.HasClosedTabHistory);
        Assert.DoesNotContain(tab, vm.OpenTabs);

        var reopened = vm.ReopenClosedTab();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(reopened);
        Assert.Same(reopened, vm.ActiveTab);
        Assert.Equal("10 PRINT \"UNSAVED\"", reopened!.Document.Text);
        Assert.True(reopened.IsModified);
        Assert.Equal(5, editor.CaretOffset);
        Assert.False(vm.HasClosedTabHistory);
        Assert.Null(vm.ReopenClosedTab());
    }

    [AvaloniaFact]
    public void CloseTab_WithoutRemember_LeavesNoHistory()
    {
        var (_, vm, _) = Show(EditorLanguage.Basic);
        vm.CloseTab(vm.ActiveTab!);
        Assert.False(vm.HasClosedTabHistory);
    }

    [AvaloniaFact]
    public void SwitchingTabs_KeepsEachTabsCaret()
    {
        var (_, vm, editor) = Show(EditorLanguage.Basic);
        var first = vm.ActiveTab!;
        editor.Document.Text = "10 PRINT \"FIRST\"";
        editor.CaretOffset = 7;

        var second = vm.NewTab(EditorLanguage.Basic);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, editor.CaretOffset);
        Assert.Equal(7, first.CaretOffset);

        vm.ActiveTab = first;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7, editor.CaretOffset);
        Assert.Same(first, vm.ActiveTab);
        Assert.Equal(0, second.CaretOffset);
    }

    [AvaloniaFact]
    public void OpeningAndSavingFiles_TracksThemAsRecent_AndRebuildsTheMenu()
    {
        var (window, vm, _) = Show(EditorLanguage.Basic);
        string path = Path.Combine(Path.GetTempPath(), $"readycode-recent-{Guid.NewGuid():N}.bas");
        File.WriteAllText(path, "10 PRINT \"RECENT\"");
        try
        {
            Assert.True(vm.OpenFile(path));
            Assert.Equal(path, vm.RecentFiles[0]);

            var recent = RecentMenu(window);
            var item = Assert.IsType<NativeMenuItem>(recent.Items[0]);
            Assert.Equal(Path.GetFileName(path), item.Header);
            Assert.Equal(path, item.ToolTip);

            // Saving under a new name puts that name at the top, and the old one is still listed.
            string saved = Path.ChangeExtension(path, ".saved.bas");
            try
            {
                Assert.True(vm.SaveTab(vm.ActiveTab!, saved));
                Assert.Equal(saved, vm.RecentFiles[0]);
                Assert.Equal(path, vm.RecentFiles[1]);
                Assert.Equal(Path.GetFileName(saved), Assert.IsType<NativeMenuItem>(RecentMenu(window).Items[0]).Header);
            }
            finally { File.Delete(saved); }
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void ExportAndImport_RoundTripTheRawText_WithoutTokenizing()
    {
        var (_, vm, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 print \"lower case stays\"";
        string path = Path.Combine(Path.GetTempPath(), $"readycode-export-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.True(vm.ExportText(path));
            Assert.Equal("10 print \"lower case stays\"", File.ReadAllText(path));

            Assert.True(vm.ImportText(path));
            var imported = vm.ActiveTab!;
            Assert.Null(imported.FilePath);
            Assert.Equal(EditorLanguage.Basic, imported.Language);
            Assert.Equal("10 print \"lower case stays\"", imported.Document.Text);
            Assert.False(imported.IsModified);
        }
        finally { File.Delete(path); }
    }

    #endregion

    #region Private Methods

    private static NativeMenu RecentMenu(MainWindow window)
    {
        var file = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "File").Menu!;
        return file.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Open Recent").Menu!;
    }

    private static (MainWindow Window, MainViewModel Vm, AvaloniaEdit.TextEditor Editor) Show(EditorLanguage language)
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NewTab(language);
        Dispatcher.UIThread.RunJobs();
        return (window, vm, window.FindControl<AvaloniaEdit.TextEditor>("Editor")!);
    }

    #endregion
}
