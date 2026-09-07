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

public class DiagnosticsTests
{
    private static readonly string? _renderDir = Environment.GetEnvironmentVariable("READYCODE_RENDER_DIR");

    [Fact]
    public void AnalyzeTab_FindsMissingGotoTarget_AndListsIt()
    {
        var vm = new MainViewModel();
        var tab = vm.ActiveTab!;
        tab.Document.Text = "10 PRINT \"HI\"\n20 GOTO 99\n30 FOR I=1 TO 3";

        var diagnostics = vm.AnalyzeTab(tab);

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, d => d.Message.Contains("99"));
        Assert.Equal(2, vm.ErrorListRows.Count);
        Assert.Equal("PROBLEMS (2)", vm.ProblemsTitle);
        Assert.Equal(20, vm.ErrorListRows[0].BasicLineNumber);
        Assert.Equal("Untitled.prg", vm.ErrorListRows[0].FileName);

        vm.Settings.EnableLinting = false;
        Assert.Empty(vm.AnalyzeTab(tab));
        Assert.Empty(vm.ErrorListRows);
    }

    [AvaloniaFact]
    public void MainWindow_ShowsSquigglesAndProblemsPanel()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 450 };
        window.Show();
        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;

        editor.Document.Text = "10 PRINT \"HELLO\"\n20 GOTO 99\n30 GOSUB 500";
        vm.IsBottomPanelOpen = true;
        window.RunDiagnosticsNow(); // instead of waiting for the 300 ms debounce
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.ErrorListRows.Count);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (!string.IsNullOrEmpty(_renderDir))
        {
            Directory.CreateDirectory(_renderDir);
            frame!.Save(Path.Combine(_renderDir, "diagnostics.png"));
        }
    }
}
