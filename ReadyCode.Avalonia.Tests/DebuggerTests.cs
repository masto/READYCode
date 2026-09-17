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
    /// <summary>A C64 memory image to serve reads from; null reads as zeros (VARTAB == ARYTAB: no variables).</summary>
    public byte[]? Memory;

    public Task<byte[]> ReadMemoryAsync(ushort startAddress, int length)
    {
        var bytes = new byte[length];
        if (Memory != null)
            for (int i = 0; i < length && startAddress + i < Memory.Length; i++) bytes[i] = Memory[startAddress + i];
        return Task.FromResult(bytes);
    }
    public Task WriteMemoryAsync(ushort startAddress, byte[] data) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

/// <summary>
/// Tests for the BASIC debug session, breakpoints, and the debugger's editor decorations, driven
/// against <see cref="FakeDebugSession"/> rather than a real emulator.
/// </summary>
public class DebuggerTests
{
    #region Public Methods

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
    public async Task ProgramReturningToReady_DetachesTheSession()
    {
        var vm = new MainViewModel();
        vm.ActiveTab!.Document.Text = Program;
        var fake = new FakeDebugSession();
        vm.DebugSessionFactoryOverride = () => Task.FromResult<IDebugSession>(fake);
        vm.DebugTransferOverride = (_, _) => Task.CompletedTask;

        await vm.DebugStartOrContinueAsync();
        fake.RaiseStopped(0xFFFF, breakpoint: false); // BASIC's "no program running" sentinel

        Assert.True(fake.Disposed);
        Assert.False(vm.IsDebugging);
        Assert.False(vm.IsDebugStopped);
        Assert.Contains("Program finished", vm.StatusText);
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
        RenderCapture.Save(window, "debugger.png");
    }

    [AvaloniaFact]
    public async Task Variables_ListSimpleOnesAndArrays_AndAnArrayLoadsItsElementsWhenExpanded()
    {
        var vm = new MainViewModel();
        vm.ActiveTab!.Document.Text = Program;

        // VARTAB at $1000: X = 3.0 (7 bytes). ARYTAB at $1007: DIM A(2), floats 1.0, 2.0, 3.0
        // (7-byte header + 3 x 5). STREND at $1007 + 22 = $101D.
        var memory = new byte[0x1100];
        memory[0x2D] = 0x00; memory[0x2E] = 0x10;
        memory[0x2F] = 0x07; memory[0x30] = 0x10;
        memory[0x31] = 0x1D; memory[0x32] = 0x10;
        new byte[] { 0x58, 0x00, 0x82, 0x40, 0x00, 0x00, 0x00 }.CopyTo(memory, 0x1000);
        new byte[] { 0x41, 0x00, 0x00, 0x16, 0x01, 0x00, 0x03 }.CopyTo(memory, 0x1007);
        new byte[] { 0x81, 0x00, 0x00, 0x00, 0x00 }.CopyTo(memory, 0x100E); // 1.0
        new byte[] { 0x82, 0x00, 0x00, 0x00, 0x00 }.CopyTo(memory, 0x1013); // 2.0
        new byte[] { 0x82, 0x40, 0x00, 0x00, 0x00 }.CopyTo(memory, 0x1018); // 3.0

        var fake = new FakeDebugSession { Memory = memory };
        vm.DebugSessionFactoryOverride = () => Task.FromResult<IDebugSession>(fake);
        vm.DebugTransferOverride = (_, _) => Task.CompletedTask;
        await vm.DebugStartOrContinueAsync();
        fake.RaiseStopped(20, breakpoint: false);
        await vm.RefreshDebugVariablesAndCallStackAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["A", "X"], vm.DebugVariables.Select(n => n.Name));
        var x = vm.DebugVariables.Single(n => n.Name == "X");
        Assert.False(x.IsArray);
        Assert.Equal("3", x.ValueDisplayText);

        var a = vm.DebugVariables.Single(n => n.Name == "A");
        Assert.True(a.IsArray);
        Assert.Empty(a.Children); // not read until expanded
        await vm.LoadArrayChildrenAsync(a);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["1", "2", "3"], a.Children.Select(c => c.ValueDisplayText));

        // A refresh keeps the same nodes (so the tree's expansion survives) and re-reads an
        // expanded array's elements.
        a.IsExpanded = true;
        memory[0x1000 + 2] = 0x83; // X = 6.0
        await vm.RefreshDebugVariablesAndCallStackAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(a, vm.DebugVariables.Single(n => n.Name == "A"));
        Assert.Same(x, vm.DebugVariables.Single(n => n.Name == "X"));
        Assert.Equal("6", x.ValueDisplayText);
        Assert.Equal(3, a.Children.Count);
    }

    #endregion
}
