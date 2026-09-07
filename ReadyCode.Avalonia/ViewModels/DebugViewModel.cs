// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using ReadyCode.Avalonia.Models;
using ReadyCode.Debugger;
using ReadyCode.Models;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The debugger half of <see cref="MainViewModel"/>: breakpoints, the live VICE session, and the
/// halted-state data (current line, variables, GOSUB call stack). Ported from the WPF view model
/// with the C64 Ultimate target left out.
/// </summary>
public partial class MainViewModel
{
    #region Private Fields

    private IDebugSession? _debugSession;
    private bool _isDebugStopped;
    private EditorTab? _debugTab;
    private BasicLineAddressTable? _debugLineAddressTable;
    private int? _debugCurrentDocumentLine;
    private ushort? _runToCursorTempBreakpointLine;

    #endregion

    #region Public Properties

    /// <summary>
    /// Runs an action on the UI thread: debug session events arrive on a background read loop.
    /// The Avalonia app installs Dispatcher.UIThread.Invoke; the default (used by tests) runs
    /// the action inline.
    /// </summary>
    public static Action<Action> RunOnUiThread { get; set; } = action => action();

    /// <summary>Lets tests substitute a fake session for the real VICE one.</summary>
    internal Func<Task<IDebugSession>>? DebugSessionFactoryOverride { get; set; }

    /// <summary>Lets tests skip the program transfer to VICE.</summary>
    internal Func<EditorTab, byte[], Task>? DebugTransferOverride { get; set; }

    /// <summary>Gets the breakpoints for every file, keyed by file path (or name for unsaved tabs).</summary>
    public BreakpointStore BreakpointStore { get; } = new();

    /// <summary>Gets the app-level store of per-project breakpoints.</summary>
    public DebugConfigStore DebugConfig { get; } = DebugConfigStore.Load();

    /// <summary>Gets the active debug session, or null when not debugging.</summary>
    public IDebugSession? DebugSession
    {
        get => _debugSession;
        private set
        {
            if (ReferenceEquals(_debugSession, value)) return;
            _debugSession = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDebugging));
            OnPropertyChanged(nameof(IsDebugRunning));
        }
    }

    /// <summary>Gets whether a debug session is active.</summary>
    public bool IsDebugging => DebugSession != null;

    /// <summary>Gets whether the session is halted at a line (stepping and variable edits are possible).</summary>
    public bool IsDebugStopped
    {
        get => _isDebugStopped;
        private set
        {
            if (_isDebugStopped == value) return;
            _isDebugStopped = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDebugRunning));
        }
    }

    /// <summary>Gets whether a session is active and the program is running (Pause is possible).</summary>
    public bool IsDebugRunning => IsDebugging && !IsDebugStopped;

    /// <summary>Gets the tab whose program the session is debugging.</summary>
    public EditorTab? DebugTab
    {
        get => _debugTab;
        private set { _debugTab = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the line table built from the debugged program when the session started.</summary>
    public BasicLineAddressTable? DebugLineAddressTable
    {
        get => _debugLineAddressTable;
        private set { _debugLineAddressTable = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the 1-based document line the session is halted on, or null while running.</summary>
    public int? DebugCurrentDocumentLine
    {
        get => _debugCurrentDocumentLine;
        private set { _debugCurrentDocumentLine = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the BASIC variables read from the halted machine.</summary>
    public ObservableCollection<BasicVariable> DebugVariables { get; } = new();

    /// <summary>Gets the GOSUB call stack read from the halted machine.</summary>
    public ObservableCollection<GosubFrame> DebugCallStack { get; } = new();

    #endregion

    #region Public Methods - Breakpoints

    /// <summary>Gets the key breakpoints for <paramref name="tab"/> are stored under.</summary>
    public static string BreakpointFileKey(EditorTab tab) => tab.FilePath ?? tab.FileName;

    /// <summary>
    /// Toggles a breakpoint on a BASIC line of <paramref name="tab"/>, persists, and keeps a live
    /// session on that tab in sync. Returns true if a breakpoint now exists there.
    /// </summary>
    public async Task<bool> ToggleBreakpointAsync(EditorTab tab, ushort basicLine)
    {
        var added = BreakpointStore.Toggle(BreakpointFileKey(tab), basicLine);
        PersistBreakpoints();

        if (DebugSession is { } session && ReferenceEquals(DebugTab, tab))
            await SyncBreakpointToLiveSessionAsync(session, basicLine, added != null);

        return added != null;
    }

    /// <summary>Flips the enabled state of the breakpoint at a line, if there is one.</summary>
    public void ToggleBreakpointEnabled(EditorTab tab, ushort basicLine)
    {
        var existing = BreakpointStore.Find(BreakpointFileKey(tab), basicLine);
        if (existing != null) existing.IsEnabled = !existing.IsEnabled;
    }

    /// <summary>
    /// Reacts to a breakpoint's enabled flag changing (the Breakpoints list's checkbox): persists
    /// it and updates a live session on the same file.
    /// </summary>
    public async Task OnBreakpointEnabledChangedAsync(Breakpoint breakpoint)
    {
        PersistBreakpoints();
        if (DebugSession is not { } session || DebugTab == null) return;
        if (!string.Equals(breakpoint.FilePath, BreakpointFileKey(DebugTab), StringComparison.OrdinalIgnoreCase)) return;
        await SyncBreakpointToLiveSessionAsync(session, breakpoint.LineNumber, breakpoint.IsEnabled);
    }

    /// <summary>Removes every breakpoint in every file, including from a live session.</summary>
    public async Task DeleteAllBreakpointsAsync()
    {
        var session = DebugSession;
        var liveLines = session != null && DebugTab != null
            ? BreakpointStore.EnabledLinesFor(BreakpointFileKey(DebugTab)).ToList()
            : new List<ushort>();

        BreakpointStore.Clear();
        PersistBreakpoints();

        if (session == null) return;
        foreach (ushort line in liveLines)
            await SyncBreakpointToLiveSessionAsync(session, line, enabled: false);
    }

    /// <summary>Saves the breakpoints for the open folder (a no-op with no folder open).</summary>
    public void PersistBreakpoints()
    {
        if (!IsFolderOpen) return;

        string projectKey = DebugConfigStore.GetFolderProjectKey(RootFolderPath);
        DebugConfig.SaveBreakpoints(projectKey, BreakpointStore.Breakpoints.Select(b => new DebugBreakpointRecord
        {
            FilePath = b.FilePath,
            LineNumber = b.LineNumber,
            Enabled = b.IsEnabled,
        }));
    }

    #endregion

    #region Public Methods - Session

    /// <summary>Starts debugging the active BASIC tab on VICE, or continues a halted session.</summary>
    public Task DebugStartOrContinueAsync() => IsDebugging ? RunDebugCommandAsync(s => s.ContinueAsync(), "Continue") : DebugStartAsync();

    /// <summary>Stops the session and starts a fresh one.</summary>
    public async Task DebugRestartAsync()
    {
        await DebugStopAsync();
        await DebugStartAsync();
    }

    /// <summary>
    /// Arms a trap that halts at the start of the next BASIC line without resuming - valid only
    /// while the program is already running.
    /// </summary>
    public Task DebugPauseAsync() => RunDebugCommandAsync(session => session.PauseAsync(), "Pause");

    /// <summary>
    /// Executes one BASIC line and halts again, entering a GOSUB called along the way, if any.
    /// </summary>
    public Task DebugStepIntoAsync() => RunDebugCommandAsync(session => session.StepIntoAsync(), "Step");

    /// <summary>
    /// Executes one BASIC line and halts again, running any GOSUB called along the way to
    /// completion rather than stopping inside it.
    /// </summary>
    public Task DebugStepOverAsync() => RunDebugCommandAsync(session => session.StepOverAsync(), "Step over");

    /// <summary>
    /// Runs until execution returns from the innermost GOSUB or FOR loop active at the stop point.
    /// </summary>
    public Task DebugStepOutAsync() => RunDebugCommandAsync(session => session.StepOutAsync(), "Step out");

    /// <summary>Continues to a BASIC line, arming a temporary breakpoint there if none exists.</summary>
    public async Task RunToLineAsync(ushort basicLine)
    {
        if (DebugSession is not { } session || !IsDebugStopped) return;

        bool alreadyBreakpoint = DebugTab != null
            && BreakpointStore.Find(BreakpointFileKey(DebugTab), basicLine) is { IsEnabled: true };

        try
        {
            if (!alreadyBreakpoint)
            {
                await session.SetLineBreakpointAsync(basicLine);
                _runToCursorTempBreakpointLine = basicLine;
            }
            await session.ContinueAsync();
        }
        catch (Exception ex)
        {
            _runToCursorTempBreakpointLine = null;
            SetStatus($"Run to Cursor failed: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>Ends the session without disturbing the running machine.</summary>
    public async Task DebugStopAsync()
    {
        if (DebugSession == null) return;

        try
        {
            await DebugSession.DisposeAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error stopping debug session: {ex.Message}", StatusType.Error);
        }
        finally
        {
            CleanupDebugSessionState();
            SetStatus("Debug session stopped.");
        }
    }

    /// <summary>
    /// Writes a new value for a variable on the halted machine, then re-reads the table so the
    /// list shows what actually landed (strings are space-padded to their original length).
    /// </summary>
    public async Task SetVariableAsync(BasicVariable variable, string enteredText)
    {
        if (DebugSession is not { } session) return;
        if (!IsDebugStopped)
        {
            SetStatus($"Can't update {variable.Name} while running - pause first.", StatusType.Error);
            return;
        }

        try
        {
            var (address, bytes) = VariableWriteBack.Encode(variable, enteredText);
            await session.WriteMemoryAsync(address, bytes);
            SetStatus($"{variable.Name} updated.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to update {variable.Name}: {ex.Message}", StatusType.Error);
        }

        await RefreshDebugVariablesAndCallStackAsync();
    }

    /// <summary>Formats a variable's value the way the C64 would print it.</summary>
    public static string FormatVariableValue(BasicVariable variable) => variable.Type switch
    {
        BasicVariableType.Float when variable.Value is double d => d.ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
        BasicVariableType.String when variable.Value is ResolvedStringValue s => $"\"{PetsciiScreenCodeMap.ToDisplayText(s.Text)}\"",
        BasicVariableType.String => "",
        _ => variable.Value.ToString() ?? "",
    };

    /// <summary>Re-reads the variable table and GOSUB stack from the halted machine.</summary>
    public async Task RefreshDebugVariablesAndCallStackAsync()
    {
        if (DebugSession is not { } session) return;

        try
        {
            byte[] zeroPage = await session.ReadMemoryAsync(0x2D, 4); // VARTAB ($2D-$2E), ARYTAB ($2F-$30)
            ushort vartab = (ushort)(zeroPage[0] | (zeroPage[1] << 8));
            ushort arytab = (ushort)(zeroPage[2] | (zeroPage[3] << 8));

            var variables = new List<BasicVariable>();
            if (arytab > vartab)
            {
                byte[] tableBytes = await session.ReadMemoryAsync(vartab, arytab - vartab);
                variables.AddRange(VariableTableParser.ParseSimpleVariables(tableBytes, vartab, arytab));

                for (int i = 0; i < variables.Count; i++)
                {
                    if (variables[i] is not { Type: BasicVariableType.String, Value: StringDescriptor descriptor }) continue;
                    if (descriptor.Length == 0) continue;

                    byte[] chars = await session.ReadMemoryAsync(descriptor.HeapPointer, descriptor.Length);
                    string text = new(chars.Select(b => (char)b).ToArray());
                    variables[i] = variables[i] with { Value = new ResolvedStringValue(text, descriptor.HeapPointer) };
                }

                variables.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            }

            IReadOnlyList<GosubFrame> callStack = Array.Empty<GosubFrame>();
            if (session.SupportsCallStackAndStepOut)
            {
                byte stackPointer = await session.ReadStackPointerAsync();
                byte[] stackPage = await session.ReadMemoryAsync(0x0100, 256);
                callStack = GosubStackParser.Parse(stackPage, stackPointer, DebugLineAddressTable);
            }

            RunOnUiThread(() =>
            {
                DebugVariables.Clear();
                foreach (var variable in variables) DebugVariables.Add(variable);
                DebugCallStack.Clear();
                foreach (var frame in callStack) DebugCallStack.Add(frame);
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => SetStatus($"Failed to refresh variables/call stack: {ex.Message}", StatusType.Error));
        }
    }

    #endregion

    #region Private Methods

    private void LoadBreakpointsForProject(string folderPath)
    {
        string projectKey = DebugConfigStore.GetFolderProjectKey(folderPath);
        BreakpointStore.ReplaceAll(DebugConfig.GetBreakpoints(projectKey).Select(r => new Breakpoint
        {
            FilePath = r.FilePath,
            LineNumber = r.LineNumber,
            IsEnabled = r.Enabled,
        }));
    }

    private async Task RunDebugCommandAsync(Func<IDebugSession, Task> action, string failureVerb)
    {
        if (DebugSession is not { } session) return;
        try
        {
            await action(session);
        }
        catch (Exception ex)
        {
            SetStatus($"{failureVerb} failed: {ex.Message}", StatusType.Error);
        }
    }

    private async Task SyncBreakpointToLiveSessionAsync(IDebugSession session, ushort basicLine, bool enabled)
    {
        try
        {
            if (enabled) await session.SetLineBreakpointAsync(basicLine);
            else await session.RemoveBreakpointAsync(basicLine);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to update breakpoint on the live session: {ex.Message}", StatusType.Error);
        }
    }

    // Builds the line table and tokenized program from the exact editor text (no minify, so the
    // lines the user set breakpoints on are the lines that run), transfers it, opens the session,
    // arms every enabled breakpoint, and types RUN over the session's own connection.
    private async Task DebugStartAsync()
    {
        if (DebugSession != null) return;

        if (DebugTransferOverride == null && !EnsureVicePathConfigured()) return;

        if (ActiveTab is not { Language: EditorLanguage.Basic } tab)
        {
            SetStatus("Start Debugging requires an active BASIC tab.", StatusType.Error);
            return;
        }

        string text = tab.Document.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("There is no code to debug. Please write some code first.", StatusType.Error);
            return;
        }

        IDebugSession? session = null;
        try
        {
            SetStatus("Building program…");
            var lineTable = BasicLineAddressTable.Build(text);
            byte[] prgData = new PrgConverter().ConvertToPrg(text);

            SetStatus("Transferring program to VICE…");
            if (DebugTransferOverride != null)
                await DebugTransferOverride(tab, prgData);
            else
                await new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort)
                    .TransferAsync(Settings.ViceEmulatorPath, prgData, tab.FileName, Settings.ViceBringToForeground);

            SetStatus("Opening debug connection…");
            session = DebugSessionFactoryOverride != null
                ? await DebugSessionFactoryOverride()
                : await ViceDebugSession.StartAsync(Settings.ViceMonitorHost, Settings.ViceMonitorPort);

            var enabledLines = BreakpointStore.EnabledLinesFor(BreakpointFileKey(tab))
                .Where(line => lineTable.LineAddresses.ContainsKey(line))
                .ToList();
            if (enabledLines.Count > 0)
            {
                SetStatus($"Arming {enabledLines.Count} breakpoint(s)…");
                foreach (ushort line in enabledLines)
                    await session.SetLineBreakpointAsync(line);
            }

            session.Stopped += OnDebugSessionStopped;
            session.Resumed += OnDebugSessionResumed;
            session.ConnectionLost += OnDebugSessionConnectionLost;

            DebugTab = tab;
            DebugLineAddressTable = lineTable;
            DebugSession = session;
            IsDebugStopped = false;

            SetStatus("Starting program…");
            // The transfer resets the machine; typing before the KERNAL polls the keyboard again
            // would drop the keystrokes.
            await Task.Delay(_sysCommandDelay);
            if (session is ViceDebugSession vice) await vice.TypeAsync("RUN\r");

            SetStatus("Debugging on VICE. Running…");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to start debugging: {ex.Message}", StatusType.Error);
            if (session != null) await session.DisposeAsync();
        }
    }

    private void CleanupDebugSessionState()
    {
        if (DebugSession != null)
        {
            DebugSession.Stopped -= OnDebugSessionStopped;
            DebugSession.Resumed -= OnDebugSessionResumed;
            DebugSession.ConnectionLost -= OnDebugSessionConnectionLost;
        }

        DebugSession = null;
        DebugTab = null;
        DebugLineAddressTable = null;
        DebugCurrentDocumentLine = null;
        DebugVariables.Clear();
        DebugCallStack.Clear();
        IsDebugStopped = false;
    }

    private void OnDebugSessionStopped(object? sender, DebugStoppedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            IsDebugStopped = true;
            DebugCurrentDocumentLine = DebugLineAddressTable != null
                && DebugLineAddressTable.BasicLineToDocumentLine.TryGetValue(e.Curlin, out int documentLine)
                    ? documentLine
                    : null;
            string note = e.CheckpointNumber.HasValue ? " (breakpoint)" : "";
            SetStatus($"Stopped at line {e.Curlin}{note}.");
        });

        if (_runToCursorTempBreakpointLine is { } tempLine)
        {
            _runToCursorTempBreakpointLine = null;
            _ = RemoveTempBreakpointAsync(tempLine);
        }

        _ = RefreshDebugVariablesAndCallStackAsync();
    }

    private async Task RemoveTempBreakpointAsync(ushort basicLine)
    {
        if (DebugSession is not { } session) return;
        try { await session.RemoveBreakpointAsync(basicLine); }
        catch { /* best effort */ }
    }

    private void OnDebugSessionResumed(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            IsDebugStopped = false;
            DebugCurrentDocumentLine = null;
            DebugVariables.Clear();
            DebugCallStack.Clear();
            SetStatus("Running…");
        });
    }

    private void OnDebugSessionConnectionLost(object? sender, string message)
    {
        IDebugSession? session = null;
        RunOnUiThread(() =>
        {
            session = DebugSession;
            SetStatus($"Debug session lost: {message}", StatusType.Error);
            CleanupDebugSessionState();
        });

        // Not awaited: this is raised from the session's own read loop, which DisposeAsync waits for.
        if (session != null)
            _ = Task.Run(async () =>
            {
                try { await session.DisposeAsync(); }
                catch { /* the connection is already gone */ }
            });
    }

    #endregion
}
