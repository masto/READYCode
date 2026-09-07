// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Debugger;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>A scripted IDebugSession standing in for VICE.</summary>
internal sealed class FakeDebugSession : IDebugSession
{
    public readonly List<ushort> ArmedLines = new();
    public bool Disposed;
    public bool SupportsCallStackAndStepOut => true;

    public event EventHandler<DebugStoppedEventArgs>? Stopped;
    public event EventHandler? Resumed;
    public event EventHandler<string>? ConnectionLost;

    public void RaiseStopped(ushort curlin, bool breakpoint) => Stopped?.Invoke(this, new DebugStoppedEventArgs(0, curlin, breakpoint ? curlin : null));
    public void RaiseLost(string message) => ConnectionLost?.Invoke(this, message);

    public Task<int> SetLineBreakpointAsync(ushort basicLineNumber) { ArmedLines.Add(basicLineNumber); return Task.FromResult((int)basicLineNumber); }
    public Task RemoveBreakpointAsync(int breakpointId) { ArmedLines.Remove((ushort)breakpointId); return Task.CompletedTask; }
    public Task ContinueAsync() { Resumed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task PauseAsync() => Task.CompletedTask;
    public Task StepIntoAsync() { Resumed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public Task StepOverAsync() => StepIntoAsync();
    public Task StepOutAsync() => StepIntoAsync();
    public Task<byte> ReadStackPointerAsync() => Task.FromResult((byte)0xFF);
    public Task<byte[]> ReadMemoryAsync(ushort startAddress, int length) => Task.FromResult(new byte[length]); // VARTAB == ARYTAB: no variables
    public Task WriteMemoryAsync(ushort startAddress, byte[] data) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

public class DebuggerTests
{
    private const string Program = "10 PRINT \"A\"\n20 GOSUB 100\n30 PRINT \"B\"\n40 END\n100 PRINT \"SUB\"\n110 RETURN";

    [Fact]
    public async Task Session_ArmsEnabledBreakpoints_TracksStops_AndCleansUp()
    {
        var vm = new MainViewModel();
        var tab = vm.ActiveTab!;
        tab.Document.Text = Program;

        Assert.True(await vm.ToggleBreakpointAsync(tab, 30));
        Assert.True(await vm.ToggleBreakpointAsync(tab, 100));
        vm.ToggleBreakpointEnabled(tab, 100); // disabled: must not be armed
        Assert.Equal(2, vm.BreakpointStore.Breakpoints.Count);

        var fake = new FakeDebugSession();
        vm.DebugSessionFactoryOverride = () => Task.FromResult<IDebugSession>(fake);
        vm.DebugTransferOverride = (_, _) => Task.CompletedTask;

        await vm.DebugStartOrContinueAsync();
        Assert.True(vm.IsDebugging);
        Assert.True(vm.IsDebugRunning);
        Assert.Equal([30], fake.ArmedLines);
        Assert.Same(tab, vm.DebugTab);

        fake.RaiseStopped(30, breakpoint: true);
        Assert.True(vm.IsDebugStopped);
        Assert.Equal(3, vm.DebugCurrentDocumentLine); // BASIC line 30 is document line 3
        Assert.Contains("Stopped at line 30 (breakpoint)", vm.StatusText);

        // Toggling while attached updates the live session too.
        Assert.False(await vm.ToggleBreakpointAsync(tab, 30));
        Assert.Empty(fake.ArmedLines);

        await vm.DebugStepOverAsync();
        Assert.False(vm.IsDebugStopped);
        Assert.Null(vm.DebugCurrentDocumentLine);

        await vm.DebugStopAsync();
        Assert.True(fake.Disposed);
        Assert.False(vm.IsDebugging);
        Assert.Null(vm.DebugTab);
    }

    [Fact]
    public async Task ConnectionLost_ClearsTheSession()
    {
        var vm = new MainViewModel();
        vm.ActiveTab!.Document.Text = Program;
        var fake = new FakeDebugSession();
        vm.DebugSessionFactoryOverride = () => Task.FromResult<IDebugSession>(fake);
        vm.DebugTransferOverride = (_, _) => Task.CompletedTask;

        await vm.DebugStartOrContinueAsync();
        fake.RaiseLost("socket closed");

        Assert.False(vm.IsDebugging);
        Assert.Contains("Debug session lost", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Gutter_ShowsBreakpoints_AndCurrentLineHighlight()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 450 };
        window.Show();
        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        editor.Document.Text = Program;
        window.RunDiagnosticsNow();
        Dispatcher.UIThread.RunJobs();

        var margin = editor.TextArea.LeftMargins.OfType<BreakpointMargin>().Single();
        vm.BreakpointStore.Toggle(MainViewModel.BreakpointFileKey(vm.ActiveTab!), 30);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(3, margin.EnabledBreakpointLines);

        var fake = new FakeDebugSession();
        vm.DebugSessionFactoryOverride = () => Task.FromResult<IDebugSession>(fake);
        vm.DebugTransferOverride = (_, _) => Task.CompletedTask;
        await vm.DebugStartOrContinueAsync();
        fake.RaiseStopped(100, breakpoint: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, vm.DebugCurrentDocumentLine);
        Assert.True(vm.IsBottomPanelOpen);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? dir = Environment.GetEnvironmentVariable("READYCODE_RENDER_DIR");
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); frame!.Save(Path.Combine(dir, "debugger.png")); }
    }
}
