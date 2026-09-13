// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Input;
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
    #region Public Methods

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
    public void BasicTab_ShiftHeldForALetter_InsertsTheLowerCaseByte()
    {
        // The C64 keyboard is upper case by default: an unshifted letter types as its upper case
        // ASCII byte, and Shift produces the byte for the C64 graphic occupying that key's
        // shifted position - internally just the letter's lower case ASCII byte, which
        // PetsciiGlyphGenerator renders as the graphic rather than a plain lower case letter.
        // Regression test: before this, Shift was ignored entirely and every letter typed as
        // upper case regardless, so a shifted key could never produce anything but a plain
        // capital - see MainWindow.ApplyC64Shift.
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        window.KeyPress(Key.A, RawInputModifiers.Shift, PhysicalKey.A, "A");
        window.KeyTextInput("A"); // what a real Shift+A keystroke composes to at the OS level
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("a", editor.Document.Text);
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

    [AvaloniaFact]
    public void BasicTab_SpacePadsLineNumber_AndEnterAutoNumbers()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        var vm = (MainViewModel)window.DataContext!;
        vm.Settings.LineNumberPadding = 4;
        vm.Settings.AutoNumberLines = true;
        vm.Settings.AutoNumberIncrement = 10;

        window.KeyTextInput("10");
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyTextInput(" ");
        window.KeyTextInput("PRINT 1");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("0010 PRINT 1" + Environment.NewLine + "0020 ", editor.Document.Text);
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);
    }

    [AvaloniaFact]
    public void AsmTab_EnterAutoIndentsAndNormalizesMnemonic()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Asm);
        var vm = (MainViewModel)window.DataContext!;
        vm.Settings.AsmAutoIndent = true;
        vm.Settings.AsmMnemonicIndentColumn = 9;
        vm.Settings.AsmCommentAlignColumn = 24;

        window.KeyTextInput("lda #0 ; zero");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Column 24 (index 23) holds the ";": 8 spaces of indent, "LDA #0", then padding to 23.
        Assert.Equal("        LDA #0         ; zero" + Environment.NewLine + "        ", editor.Document.Text);
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

    #endregion
}
