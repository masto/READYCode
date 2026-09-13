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
/// Edit > Renumber Code and Format Code, the two whole-document transforms with no options
/// dialog. Minify and Prettify share the same apply path and differ only in the dialog.
/// </summary>
public class CodeTransformTests
{
    #region Public Methods

    [AvaloniaFact]
    public async Task Renumber_RenumbersInAutoNumberSteps_AndFixesReferences()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        vm.Settings.AutoNumberIncrement = 10;
        vm.Settings.LineNumberPadding = 0;
        editor.Document.Text = "5 PRINT \"A\"\n7 GOTO 5\n12 END";

        await window.ExecuteRenumberAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 PRINT \"A\"\n20 GOTO 10\n30 END", editor.Document.Text);
        Assert.Equal("Code renumbered.", vm.StatusText);

        await window.ExecuteRenumberAsync();
        Assert.Equal("No changes — line numbers are already sequential.", vm.StatusText);
    }

    [AvaloniaFact]
    public void Format_AlignsAssemblyToTheFormattingSettings()
    {
        var (window, vm, editor) = Show(EditorLanguage.Asm);
        vm.Settings.AsmMnemonicIndentColumn = 9;
        vm.Settings.AsmCommentAlignColumn = 24;
        editor.Document.Text = "lda #0 ; zero\nsta $d020";

        window.ExecuteFormatAsm();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("        LDA #0         ; zero\n        STA $d020", editor.Document.Text);
        Assert.Equal("Code formatted.", vm.StatusText);
    }

    [AvaloniaFact]
    public void Menu_OffersBasicTransformsOnBasicTabs_AndFormatOnAssemblyTabs()
    {
        var (window, vm, _) = Show(EditorLanguage.Basic);
        var edit = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Edit").Menu!;
        NativeMenuItem Item(string header) => edit.Items.OfType<NativeMenuItem>().Single(item => item.Header == header);

        Assert.True(Item("Minify Code").IsVisible);
        Assert.True(Item("Renumber Code").IsVisible);
        Assert.False(Item("Format Code").IsVisible);

        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Item("Minify Code").IsVisible);
        Assert.False(Item("Prettify Code").IsVisible);
        Assert.True(Item("Format Code").IsVisible);
    }

    #endregion

    #region Private Methods

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
