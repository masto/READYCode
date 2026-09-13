// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The right-hand reference panels and the Quick Keys shortcuts, all generated from the shared
/// tables in ReadyCode.Core.
/// </summary>
public class ReferencePanelTests
{
    #region Public Methods

    [Fact]
    public void QuickKeys_AreAllControlCodes_WithOneShortcutEach()
    {
        foreach (var section in PetsciiReference.QuickKeySections)
        {
            Assert.Equal(8, section.Keys.Count);
            Assert.Equal(Enumerable.Range(1, 8), section.Keys.Select(k => k.KeyNumber));
            foreach (var key in section.Keys)
            {
                Assert.Equal(PetsciiCodeKind.Control, PetsciiReference.Classify(key.Code));
                Assert.Contains($"CHR$({key.Code})", section.Tooltip(key));
            }
        }

        Assert.Equal("CLR: Ctrl+2; CHR$(147)", PetsciiReference.QuickKeySections[0].Tooltip(PetsciiReference.QuickKeySections[0].Keys[1]));
        Assert.Equal("Cyan: Ctrl+Shift+4; CHR$(159); POKE(3)", PetsciiReference.QuickKeySections[1].Tooltip(PetsciiReference.QuickKeySections[1].Keys[3]));
    }

    [AvaloniaFact]
    public void QuickKeyShortcuts_InsertThePetsciiCode()
    {
        var (window, editor) = ShowEditor();

        window.KeyTextInput("10 PRINT \"");
        // Ctrl+2 is CLR (CHR$(147)); Ctrl+Shift+4 is Cyan (CHR$(159)); Shift+F1 is F1 (CHR$(133)).
        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Digit4, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 PRINT \"" + (char)147 + (char)159 + (char)133, editor.Document.Text);
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);
    }

    [AvaloniaFact]
    public void ShiftF3_IsFindPreviousWhileTheFindBarShows_AndTheF3QuickKeyOtherwise()
    {
        var (window, editor) = ShowEditor();
        editor.Document.Text = "10 PRINT \"A\"\n20 PRINT \"A\"";
        var findBar = window.FindControl<FindBarControl>("FindBar")!;

        // With the Find bar showing, Shift+F3 navigates and inserts nothing (Windows/Linux bind
        // Find Previous to Shift+F3; macOS to Cmd+Shift+G, but the quick key defers either way).
        findBar.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain((char)134, editor.Document.Text);

        findBar.IsVisible = false;
        editor.CaretOffset = editor.Document.TextLength;
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.EndsWith(((char)134).ToString(), editor.Document.Text);
    }

    [AvaloniaFact]
    public void ActivityBar_SwitchesAndCollapsesTheRightPanel_AndGatesByLanguage()
    {
        var (window, _) = ShowEditor();
        var vm = (MainViewModel)window.DataContext!;
        vm.IsRightPanelOpen = false;

        Click(window, "ActivityPetscii");
        Assert.True(vm.IsRightPanelOpen);
        Assert.Equal("Petscii", vm.ActiveRightPanel);
        Assert.True(vm.IsPetsciiToggleChecked);
        Assert.Equal("PETSCII REFERENCE", vm.RightPanelTitle);

        Click(window, "ActivityPetscii");
        Assert.False(vm.IsRightPanelOpen);
        Assert.False(vm.IsPetsciiToggleChecked);

        // BASIC Keywords is offered on a BASIC tab; switching to assembly hides it, closes its
        // panel if open, and offers ASM Mnemonics instead.
        Click(window, "ActivityBasicKeywords");
        Assert.True(vm.IsBasicTabActive);
        Assert.True(vm.IsBasicKeywordsToggleChecked);

        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsBasicTabActive);
        Assert.True(vm.IsAsmTabActive);
        Assert.False(vm.IsRightPanelOpen);

        // View > Secondary Side Bar opens Quick Keys from closed and closes whatever is open.
        vm.ToggleSecondarySideBar();
        Assert.True(vm.IsRightPanelOpen);
        Assert.Equal("QuickKeys", vm.ActiveRightPanel);
        vm.ToggleSecondarySideBar();
        Assert.False(vm.IsRightPanelOpen);
    }

    [AvaloniaFact]
    public void ReferencePanels_Render()
    {
        var (window, editor) = ShowEditor();
        var vm = (MainViewModel)window.DataContext!;
        editor.Document.Text = "10 PRINT \"HELLO\"";

        foreach (string panel in MainViewModel.RightPanels)
        {
            if (panel == "AsmKeywords") vm.NewTab(EditorLanguage.Asm); // it's only offered there
            vm.ActiveRightPanel = panel;
            vm.IsRightPanelOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsRightPanelOpen, panel);
            RenderCapture.Save(window, $"right-panel-{panel.ToLowerInvariant()}.png");
        }

        // The panels have real content: a card per quick key, a row per listed PETSCII code.
        Assert.Equal(PetsciiReference.AllQuickKeys.Count(), window.FindControl<StackPanel>("QuickKeysPanel")!.GetLogicalDescendants().OfType<Button>().Count());
        Assert.True(window.FindControl<StackPanel>("PetsciiTablePanel")!.Children.Count > PetsciiReference.LastListedCode);
    }

    #endregion

    #region Private Methods

    private static void Click(MainWindow window, string buttonName) =>
        window.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static (MainWindow Window, AvaloniaEdit.TextEditor Editor) ShowEditor()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 600 };
        window.Show();
        vm.NewTab(EditorLanguage.Basic);
        Dispatcher.UIThread.RunJobs();

        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    #endregion
}
