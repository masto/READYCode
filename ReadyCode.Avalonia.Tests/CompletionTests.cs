// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Editor;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Keyword completion: the ghost-text suggestion after the caret, Tab to accept it, and the
/// Ctrl+Space popup.
/// </summary>
public class CompletionTests
{
    #region Public Methods

    [AvaloniaFact]
    public void TypingAKeywordPrefix_ShowsTheRestOfItsSnippet_AndTabAcceptsIt()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        window.KeyTextInput("10 pri");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("10 PRI", editor.Document.Text);
        Assert.Equal("NT \"\"", window.GhostText);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // The snippet is PRINT "|": the prefix is replaced and the caret lands at the marker.
        Assert.Equal("10 PRINT \"\"", editor.Document.Text);
        Assert.Equal(10, editor.CaretOffset);
        Assert.Equal("", window.GhostText);
    }

    [AvaloniaFact]
    public void GhostText_IsRenderedAfterTheCaret()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        window.KeyTextInput("10 pri");
        Dispatcher.UIThread.RunJobs();

        // Pixels to the right of the typed text, on the same row, must be painted: the grey
        // "NT" of the suggestion. A blank line there would mean the renderer never drew.
        var frame = RenderCapture.Save(window, "completion-ghost-text.png");
        var textView = editor.TextArea.TextView;
        var caretPoint = textView.TranslatePoint(new Point(0, 0), window)!.Value;
        var caretRect = editor.TextArea.Caret.CalculateCaretRectangle();
        int y = (int)(caretPoint.Y + caretRect.Top + caretRect.Height / 2);
        int xStart = (int)(caretPoint.X + caretRect.Left) + 2;
        int width = (int)caretRect.Height * 2;

        Assert.True(RenderCapture.HasInk(frame, xStart, y, width), "no ghost text drawn after the caret");
    }

    [AvaloniaFact]
    public void NoSuggestion_MidWord_OrAfterANumber_OrWithNoMatch()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);

        editor.Document.Text = "10 PRINT";
        editor.CaretOffset = 5; // between PR and INT
        Assert.Equal("", window.GhostText);

        editor.CaretOffset = 2; // after the line number
        Assert.Equal("", window.GhostText);

        editor.Document.Text = "10 ZQ";
        editor.CaretOffset = 5;
        Assert.Equal("", window.GhostText);
    }

    [AvaloniaFact]
    public void NoSuggestion_PastARem_ButStillBeforeIt()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        editor.Document.Text = "10 REM PRI";
        editor.CaretOffset = editor.Document.TextLength;
        Assert.Equal("", window.GhostText);

        editor.Document.Text = "10 PRI";
        editor.CaretOffset = editor.Document.TextLength;
        Assert.Equal("NT \"\"", window.GhostText);
    }

    [AvaloniaFact]
    public void AssemblyTabs_SuggestMnemonics_NotBasicKeywords()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Asm);
        editor.Document.Text = "        ld";
        editor.CaretOffset = editor.Document.TextLength;

        string expected = AsmCompletionProvider.GetMatches("LD")[0].InsertText[2..];
        Assert.Equal(expected, window.GhostText);

        // And not what the BASIC provider would have said for the same prefix.
        string basic = BasicCompletionProvider.GetMatches("LD").Select(i => i.InsertText[2..]).FirstOrDefault() ?? "";
        Assert.NotEqual(basic, window.GhostText);
    }

    [AvaloniaFact]
    public void CtrlSpace_OpensThePopup_WithEverythingWhenNothingIsTyped_AndMatchesOtherwise()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        window.KeyTextInput("10 ");
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsCompletionPopupOpen);
        Assert.Equal(BasicCompletionProvider.AllItems.Count, window.CompletionPopupItems.Count);
        Assert.Equal("10 ", editor.Document.Text); // the Space itself was not typed

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsCompletionPopupOpen);

        window.KeyTextInput("pr");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsCompletionPopupOpen);
        Assert.All(window.CompletionPopupItems, text => Assert.StartsWith("PR", text));
        Assert.Equal("", window.GhostText);
    }

    [AvaloniaFact]
    public void Enter_WithThePopupOpen_AcceptsTheSelection_InsteadOfAutoNumbering()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        window.KeyTextInput("10 pri");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsCompletionPopupOpen);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsCompletionPopupOpen);
        Assert.Equal("10 PRINT \"\"", editor.Document.Text);
    }

    [AvaloniaFact]
    public void Enter_WithNothingTypedBeforeThePopup_InsertsTheSelection_AtTheCaret()
    {
        var (window, editor) = ShowEditor(EditorLanguage.Basic);
        window.KeyTextInput("10 ");
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsCompletionPopupOpen);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsCompletionPopupOpen);
        Assert.Equal("10 ABS()", editor.Document.Text);
        Assert.Equal(7, editor.CaretOffset); // inside the parentheses
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, AvaloniaEdit.TextEditor Editor) ShowEditor(EditorLanguage language)
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
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
