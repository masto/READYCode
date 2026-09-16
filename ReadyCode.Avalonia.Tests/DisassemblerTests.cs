// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The disassembler: "Disassemble at…" tabs and their toolbar, "Disassemble file", and the
/// assembly gutter's addresses.
/// </summary>
public class DisassemblerTests
{
    #region Public Methods

    [AvaloniaFact]
    public void DisassembleAt_OpensAReadOnlyTab_WithTheToolbar_AndSaveAsMakesItEditable()
    {
        var (window, vm, editor) = Show();
        var toolbar = window.FindControl<DisassemblyToolbarControl>("DisassemblyToolbar")!;
        Assert.False(toolbar.IsVisible);

        var tab = vm.OpenDisassemblyTab(DisassemblySource.Vice);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(tab, vm.ActiveTab);
        Assert.Equal("Disassembly (VICE).asm", tab.FileName);
        Assert.True(tab.IsDisassemblyMode);
        Assert.True(editor.IsReadOnly);
        Assert.True(toolbar.IsVisible);
        Assert.StartsWith("; Enter a Start and End address", editor.Document.Text);
        Assert.Contains(editor.TextArea.LeftMargins, m => m is AsmLineNumberMargin);
        RenderCapture.Save(window, "disassemble-at.png");

        string path = Path.Combine(Path.GetTempPath(), $"readycode-disasm-{Guid.NewGuid():N}.asm");
        try
        {
            Assert.True(vm.SaveTab(tab, path));
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsDisassemblyMode);
            Assert.False(editor.IsReadOnly);
            Assert.False(toolbar.IsVisible);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Toolbar_ParsesHexAddresses_WithOrWithoutADollar_AndRejectsBadRanges()
    {
        var toolbar = new DisassemblyToolbarControl { StartAddressText = "$C000", EndAddressText = "c0ff" };
        Assert.True(toolbar.TryGetAddressRange(out ushort start, out ushort end, out string? error));
        Assert.Equal(0xC000, start);
        Assert.Equal(0xC0FF, end);
        Assert.Null(error);

        toolbar.EndAddressText = "$BFFF";
        Assert.False(toolbar.TryGetAddressRange(out _, out _, out error));
        Assert.Contains("at or after", error);

        toolbar.StartAddressText = "zz";
        Assert.False(toolbar.TryGetAddressRange(out _, out _, out error));
        Assert.Contains("start address", error);
    }

    [AvaloniaFact]
    public void DisassembleFile_OpensAnEditableListing_WithAddressesInTheGutter()
    {
        var (window, vm, editor) = Show();
        // $C000: LDA #$00 / STA $D020 / RTS
        byte[] prg = [0x00, 0xC0, 0xA9, 0x00, 0x8D, 0x20, 0xD0, 0x60];

        var tab = vm.DisassembleFileBytes(prg, "border.ml");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(tab);
        Assert.Same(tab, vm.ActiveTab);
        Assert.Equal("border (Disassembled).asm", tab!.FileName);
        Assert.False(tab.IsDisassemblyMode);
        Assert.False(editor.IsReadOnly);
        Assert.Contains("LDA #$00", editor.Document.Text);
        Assert.Contains("STA $D020", editor.Document.Text);
        Assert.Contains("RTS", editor.Document.Text);
        Assert.NotNull(tab.DisassemblyLineAddresses);
        Assert.Contains((ushort)0xC000, tab.DisassemblyLineAddresses!.Values);

        var margin = editor.TextArea.LeftMargins.OfType<AsmLineNumberMargin>().Single();
        Assert.Same(tab.DisassemblyLineAddresses, margin.LineAddresses);
        RenderCapture.Save(window, "disassembled-file.png");
    }

    [AvaloniaFact]
    public void AssembledSource_WithAFixedOrigin_ShowsAddressesInTheGutter_ElseLineNumbers()
    {
        var (window, vm, editor) = Show();
        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        var margin = editor.TextArea.LeftMargins.OfType<AsmLineNumberMargin>().Single();

        editor.Document.Text = "        LDA #0\n        RTS";
        window.RunDiagnosticsNow();
        Assert.Null(margin.LineAddresses);

        editor.Document.Text = "        * = $C000\n        LDA #0\n        RTS";
        window.RunDiagnosticsNow();
        Assert.NotNull(margin.LineAddresses);
        Assert.Equal((ushort)0xC000, margin.LineAddresses![2]);
        Assert.Equal((ushort)0xC002, margin.LineAddresses[3]);
    }

    [AvaloniaFact]
    public void ExplorerContextMenu_OffersDisassembleFile_ForMachineLanguageOnly()
    {
        var (window, vm, _) = Show();
        string dir = Path.Combine(Path.GetTempPath(), $"readycode-disasm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "code.prg"), [0x00, 0xC0, 0x60]);
            File.WriteAllText(Path.Combine(dir, "prog.bas"), "10 END");
            vm.LoadFolder(dir);
            Dispatcher.UIThread.RunJobs();

            var ml = vm.FolderItems.Single(i => i.Name == "code.prg");
            var bas = vm.FolderItems.Single(i => i.Name == "prog.bas");
            Assert.Contains(window.ContextMenuHeadersFor(ml), h => h == "Disassemble file");
            Assert.DoesNotContain(window.ContextMenuHeadersFor(bas), h => h == "Disassemble file");
            Assert.Contains(window.ContextMenuHeadersFor(bas), h => h == "Open in Hex editor");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, MainViewModel Vm, AvaloniaEdit.TextEditor Editor) Show()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, window.FindControl<AvaloniaEdit.TextEditor>("Editor")!);
    }

    #endregion
}
