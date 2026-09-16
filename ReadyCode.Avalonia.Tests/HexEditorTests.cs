// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.C64U;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The hex editor: which files open in it, editing/selection/undo in the grid, and saving the
/// bytes back.
/// </summary>
public class HexEditorTests
{
    #region Public Methods

    [AvaloniaFact]
    public void MachineLanguagePrg_OpensInHexMode_AndSavesItsBytes()
    {
        var (window, vm) = Show();
        // Load address $C000 followed by code: FileClassifier calls a .prg that doesn't start at
        // $0801 machine language.
        byte[] bytes = [0x00, 0xC0, 0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];
        string path = TempFile(".prg", bytes);
        try
        {
            Assert.True(vm.OpenFile(path));
            var tab = vm.ActiveTab!;
            Assert.True(tab.IsHexMode);
            Assert.Equal(C64UFileKind.Ml, tab.Kind);
            Assert.Equal(bytes, tab.RawBytes);
            Assert.Equal("", tab.Document.Text);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<HexEditorControl>("HexEditor")!.IsVisible);
            Assert.False(window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.IsVisible);

            tab.RawBytes[3] = 0x07;
            Assert.True(vm.SaveTab(tab, path));
            Assert.Equal(0x07, File.ReadAllBytes(path)[3]);
            Assert.False(tab.IsModified);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void OpenInHexEditor_ReloadsATextTabAsHex_UnlessItHasUnsavedChanges()
    {
        var (_, vm) = Show();
        string path = TempFile(".bas", "10 PRINT \"HI\""u8.ToArray());
        try
        {
            Assert.True(vm.OpenFile(path));
            var tab = vm.ActiveTab!;
            Assert.False(tab.IsHexMode);

            Assert.True(vm.OpenFile(path, forceHex: true));
            Assert.Same(tab, vm.ActiveTab);
            Assert.True(tab.IsHexMode);
            Assert.Equal((byte)'1', tab.RawBytes![0]);

            tab.IsModified = true;
            Assert.True(vm.OpenFile(path));
            Assert.True(tab.IsHexMode); // refused: still hex
            Assert.Contains("unsaved changes", vm.StatusText);

            tab.IsModified = false;
            Assert.True(vm.OpenFile(path));
            Assert.False(tab.IsHexMode);
            Assert.Equal("10 PRINT \"HI\"", tab.Document.Text);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Grid_KeyboardNavigation_Selection_AndDelete()
    {
        var (window, hex) = ShowHex(Enumerable.Range(0, 40).Select(i => (byte)i).ToArray());
        var grid = hex.Grid;
        Assert.Equal(0, grid.SelectedOffset);

        Press(window, PhysicalKey.ArrowRight);
        Press(window, PhysicalKey.ArrowDown);
        Assert.Equal(17, grid.SelectedOffset);

        Press(window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Press(window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Equal(17, grid.SelectionStart);
        Assert.Equal(19, grid.SelectionEnd);

        Press(window, PhysicalKey.End);
        Assert.Equal(31, grid.SelectedOffset);
        Press(window, PhysicalKey.End, RawInputModifiers.Control);
        Assert.Equal(39, grid.SelectedOffset);
        Press(window, PhysicalKey.ArrowDown); // no row below: stays put
        Assert.Equal(39, grid.SelectedOffset);

        grid.Select(17, 3);
        hex.Delete();
        Assert.Equal([0, 0, 0], hex.Grid.GetSelectionHexText().Split(' ').Select(h => Convert.ToInt32(h, 16)));
        Assert.True(hex.CanUndo);
        hex.Undo();
        Assert.Equal("11 12 13", grid.GetSelectionHexText());
        hex.Redo();
        Assert.Equal("00 00 00", grid.GetSelectionHexText());
    }

    [AvaloniaFact]
    public void TypingHexDigits_EditsTheByteAndMovesOn_AndTheTabBecomesModified()
    {
        var (window, hex, vm) = ShowHexTab([0x00, 0x00, 0x00, 0x00]);
        var tab = vm.ActiveTab!;
        Assert.False(tab.IsModified);

        // A hex digit on the active cell opens the edit box with it typed; the second digit
        // completes the byte and moves on to the next.
        Press(window, PhysicalKey.A);
        Dispatcher.UIThread.RunJobs();
        Assert.True(hex.Grid.IsEditing);
        Assert.Equal("A", hex.EditTextBox.Text);
        window.KeyTextInput("9");
        Dispatcher.UIThread.RunJobs();

        Assert.False(hex.Grid.IsEditing);
        Assert.Equal(0xA9, tab.RawBytes![0]);
        Assert.Equal(1, hex.SelectedOffset);
        Assert.True(tab.IsModified);

        // Enter opens the box on the current byte; Escape leaves it unchanged.
        Press(window, PhysicalKey.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(hex.Grid.IsEditing);
        window.KeyTextInput("F");
        Press(window, PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(hex.Grid.IsEditing);
        Assert.Equal(0x00, tab.RawBytes[1]);

        hex.Undo();
        Assert.Equal(0x00, tab.RawBytes[0]);
    }

    [AvaloniaFact]
    public void Paste_OverwritesFromTheSelection_AndNeverGrowsTheBuffer()
    {
        var (_, hex, vm) = ShowHexTab([1, 2, 3, 4]);
        hex.Grid.Select(2, 1);
        hex.Grid.PasteHexText("AA BB CC DD"); // four bytes offered, two fit
        Assert.Equal(new byte[] { 1, 2, 0xAA, 0xBB }, vm.ActiveTab!.RawBytes);
        Assert.Equal("AA BB", hex.Grid.GetSelectionHexText());
    }

    [AvaloniaFact]
    public void HexTab_KeepsTheEditMenusTextCommandsOffTheEmptyDocument()
    {
        var (window, _, vm) = ShowHexTab([1, 2, 3]);
        var tab = vm.ActiveTab!;

        // Quick keys and the completion popup act on the text editor; on a hex tab they must
        // not touch its (empty) document.
        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", tab.Document.Text);
        Assert.False(vm.IsBasicTabActive);
        Assert.False(vm.IsShiftModeApplicable);
    }

    [AvaloniaFact]
    public void Renders()
    {
        var (window, hex) = ShowHex(Enumerable.Range(0, 100).Select(i => (byte)(i * 7)).ToArray());
        hex.Grid.Select(20, 6);
        Dispatcher.UIThread.RunJobs();
        var frame = RenderCapture.Save(window, "hex-editor.png");
        Assert.True(frame.PixelSize.Width > 100);
    }

    #endregion

    #region Private Methods

    private static string TempFile(string extension, byte[] content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"readycode-hex-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, MainViewModel Vm) Show()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static (MainWindow Window, HexEditorControl Hex, MainViewModel Vm) ShowHexTab(byte[] bytes)
    {
        var (window, vm) = Show();
        var tab = new Models.EditorTab { DisplayName = "test.ml", RawBytes = bytes, Kind = C64UFileKind.Ml };
        vm.OpenTabs.Add(tab);
        vm.ActiveTab = tab;
        tab.IsModified = false;
        Dispatcher.UIThread.RunJobs();
        var hex = window.FindControl<HexEditorControl>("HexEditor")!;
        hex.FocusGrid();
        Dispatcher.UIThread.RunJobs();
        return (window, hex, vm);
    }

    private static (MainWindow Window, HexEditorControl Hex) ShowHex(byte[] bytes)
    {
        var (window, hex, _) = ShowHexTab(bytes);
        return (window, hex);
    }

    #endregion
}
