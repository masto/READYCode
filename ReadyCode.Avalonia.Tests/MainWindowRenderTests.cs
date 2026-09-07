// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Smoke tests that show the main windows on the headless platform and capture what they
/// render. Set READYCODE_RENDER_DIR to also write the captured frames to disk for eyeballing.
/// </summary>
public class MainWindowRenderTests
{
    private static readonly string? _renderDir = Environment.GetEnvironmentVariable("READYCODE_RENDER_DIR");

    [AvaloniaFact]
    public void MainWindow_RendersPetsciiStyledListing()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
        window.Show();

        var tab = vm.ActiveTab!;
        tab.Kind = C64UFileKind.Prg; // PETSCII-styled, like a detokenized .prg
        tab.Document.Text =
            "10 PRINT \"HELLO FROM READYCODE\"\n" +
            "20 FOR I=1 TO 5:PRINT \"LINE\";I:NEXT I\n" +
            "30 PRINT \"ÑÑÑ     ÓÓÓ \"\n" + // graphics chars + CLR
            "40 X=.5+3E2\n" +
            "50 REM A COMMENT\n" +
            "60 REM ANOTHER";
        vm.SetStatus("Rendered for test.");

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.True(frame!.PixelSize.Width > 100 && frame.PixelSize.Height > 100);
        Save(frame, "main-window-prg.png");
    }

    [AvaloniaFact]
    public void MainWindow_RendersAsciiStyledBasSource()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 400 };
        window.Show();

        var tab = vm.ActiveTab!;
        tab.Kind = C64UFileKind.Bas;
        tab.Document.Text = "10 PRINT \"HELLO\"\n20 GOTO 10\n30 REM END";

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Save(frame!, "main-window-bas.png");
    }

    [AvaloniaFact]
    public void MainWindow_RendersAssemblySource()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 400 };
        window.Show();

        var tab = vm.ActiveTab!;
        tab.Kind = C64UFileKind.Asm;
        tab.Language = EditorLanguage.Asm;
        tab.Document.Text = "; border colour demo\n; second comment line\nstart:\n        LDA #$00\n        STA $D020\n        LDX #%1010\nloop:   DEX\n        BNE loop ; spin\n        RTS";

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Save(frame!, "main-window-asm.png");
    }

    [AvaloniaFact]
    public void SettingsWindow_Renders()
    {
        var window = new SettingsWindow(new ReadyCode.Settings.AppSettings { ViceEmulatorPath = "/opt/homebrew/bin/x64sc" });
        window.Show();

        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Save(frame!, "settings-window.png");
    }

    private static void Save(global::Avalonia.Media.Imaging.Bitmap frame, string name)
    {
        if (string.IsNullOrEmpty(_renderDir)) return;
        Directory.CreateDirectory(_renderDir);
        frame.Save(Path.Combine(_renderDir, name));
    }
}
