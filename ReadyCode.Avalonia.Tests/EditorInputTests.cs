// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Typing into the editor follows C64 conventions for BASIC: unshifted keys produce upper-case
/// letters, and a shifted letter completing a keyword abbreviation becomes the PETSCII graphic.
/// </summary>
public class EditorInputTests
{
    [AvaloniaFact]
    public void BasicTab_TypedTextIsUpperCased()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        window.KeyTextInput("10 print \"hi\"");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 PRINT \"HI\"", editor.Document.Text);
        Assert.Equal(13, editor.CaretOffset);
    }

    [AvaloniaFact]
    public void BasicTab_ShiftedLetterAfterPrefix_InsertsKeywordAbbreviationGlyph()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        // "g" then shift-O is the C64 keyboard abbreviation for GOTO: the prefix is stored upper
        // case and the shifted letter as its lower-case ASCII code (= the PETSCII graphic byte).
        window.KeyTextInput("10 g");
        window.KeyTextInput("O");
        window.KeyTextInput("10");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 Go10", editor.Document.Text);
    }

    [AvaloniaFact]
    public void BasicTab_ShiftedLetterInsideString_IsJustUpperCased()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        window.KeyTextInput("10 PRINT \"g");
        window.KeyTextInput("O");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 PRINT \"GO", editor.Document.Text);
    }

    [AvaloniaFact]
    public void AsmTab_KeepsTypedCase()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Asm);

        window.KeyTextInput("loop: lda #$00");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("loop: lda #$00", editor.Document.Text);
    }

    private static (MainWindow Window, AvaloniaEdit.TextEditor Editor) ShowEditor(EditorLanguage language)
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NewTab(language);
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }
}
