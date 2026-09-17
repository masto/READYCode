// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using ReadyCode.Avalonia.Models;
using ReadyCode.C64U;
using ReadyCode.Debugger;
using ReadyCode.Models;
using ReadyCode.Settings;
using ReadyCode.Tokenizer;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The debugger half of <see cref="MainViewModel"/>: breakpoints, the live debug session (VICE or
/// a C64 Ultimate), and the halted-state data (current line, variables, GOSUB call stack). Ported
/// from the WPF view model.
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
            OnPropertyChanged(nameof(IsStepOverOutEnabled));
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
            OnPropertyChanged(nameof(IsStepOverOutEnabled));
        }
    }

    /// <summary>Gets whether a session is active and the program is running (Pause is possible).</summary>
    public bool IsDebugRunning => IsDebugging && !IsDebugStopped;

    /// <summary>
    /// Gets whether Step Over/Step Out can be used: halted, and on a target that can read the
    /// 6502 stack pointer (VICE - not the C64 Ultimate, whose REST API has no register access).
    /// </summary>
    public bool IsStepOverOutEnabled => IsDebugStopped && (DebugSession?.SupportsCallStackAndStepOut ?? false);

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
    public ObservableCollection<DebugVariableNode> DebugVariables { get; } = new();

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
    public Task DebugStartOrContinueAsync() => IsDebugging ? RunDebugCommandAsync(s => s.ContinueAsync(), "Continue") : DebugStartOnViceAsync();

    /// <summary>
    /// Stops the session and starts a fresh one on whichever target was active (VICE by default,
    /// e.g. when nothing was running yet) - shared by the generic Debug menu's "Restart
    /// Debugging", so it restarts the right target even when a C64U session is the one active.
    /// </summary>
    public async Task DebugRestartAsync()
    {
        bool wasC64U = DebugSession is C64UDebugSession;
        await DebugStopAsync();
        if (wasC64U) await DebugStartOnC64UAsync();
        else await DebugStartOnViceAsync();
    }

    /// <summary>
    /// Starts debugging the active BASIC tab on the C64 Ultimate, or continues a halted session.
    /// "Restart Debugging" has no C64U-specific counterpart: the generic Debug menu's own
    /// <see cref="DebugRestartAsync"/> already restarts whichever target was active.
    /// </summary>
    public Task DebugStartOrContinueOnC64UAsync() => IsDebugging ? RunDebugCommandAsync(s => s.ContinueAsync(), "Continue") : DebugStartOnC64UAsync();

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
    /// <param name="completionMessage">Status text to show once stopped.</param>
    public async Task DebugStopAsync(string completionMessage = "Debug session stopped.")
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
            SetStatus(completionMessage);
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

    /// <summary>
    /// Re-reads the variable table and GOSUB stack from the halted machine. Simple variables
    /// are read in full (strings resolved, up to a limit); arrays are listed by their headers
    /// only, their elements fetched when expanded - and re-fetched here for ones already
    /// expanded. Existing nodes are updated in place so the tree keeps its expansion.
    /// </summary>
    public async Task RefreshDebugVariablesAndCallStackAsync()
    {
        if (DebugSession is not { } session) return;

        try
        {
            byte[] zeroPage = await session.ReadMemoryAsync(0x2D, 6); // VARTAB ($2D-$2E), ARYTAB ($2F-$30), STREND ($31-$32)
            ushort vartab = (ushort)(zeroPage[0] | (zeroPage[1] << 8));
            ushort arytab = (ushort)(zeroPage[2] | (zeroPage[3] << 8));
            ushort strend = (ushort)(zeroPage[4] | (zeroPage[5] << 8));
            bool isUpperCaseModeActive = DebugTab?.IsUpperCaseModeActive ?? true;

            var nodes = new List<DebugVariableNode>();
            if (arytab > vartab)
            {
                byte[] tableBytes = await session.ReadMemoryAsync(vartab, arytab - vartab);
                var simpleVars = VariableTableParser.ParseSimpleVariables(tableBytes, vartab, arytab).ToList();
                await ResolveStringValuesAsync(session, simpleVars, maxResolutions: 300);
                foreach (var variable in simpleVars)
                    nodes.Add(new DebugVariableNode(variable) { IsUpperCaseModeActive = isUpperCaseModeActive });
            }
            if (strend > arytab)
            {
                foreach (var header in await LoadArrayHeadersAsync(session, arytab, strend))
                    nodes.Add(new DebugVariableNode(header.Name, header.ElementType, header.DimensionSizes, header.DataAddress) { IsUpperCaseModeActive = isUpperCaseModeActive });
            }
            nodes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            IReadOnlyList<GosubFrame> callStack = Array.Empty<GosubFrame>();
            if (session.SupportsCallStackAndStepOut)
            {
                byte stackPointer = await session.ReadStackPointerAsync();
                byte[] stackPage = await session.ReadMemoryAsync(0x0100, 256);
                callStack = GosubStackParser.Parse(stackPage, stackPointer, DebugLineAddressTable);
            }

            var reexpand = new List<DebugVariableNode>();
            RunOnUiThread(() =>
            {
                var existingByKey = DebugVariables.GroupBy(n => (n.Name, n.IsArray)).ToDictionary(g => g.Key, g => g.First());
                DebugVariables.Clear();
                foreach (var node in nodes)
                {
                    var toAdd = node;
                    if (existingByKey.TryGetValue((node.Name, node.IsArray), out var existing))
                    {
                        if (existing.IsArray)
                        {
                            existing.RefreshArrayShape(node.DimensionSizes, node.DataAddress);
                            existing.ChildrenLoaded = false;
                            if (existing.IsExpanded) reexpand.Add(existing);
                        }
                        else
                        {
                            existing.UpdateVariable(node.Variable!);
                        }
                        existing.IsUpperCaseModeActive = isUpperCaseModeActive;
                        toAdd = existing;
                    }
                    DebugVariables.Add(toAdd);
                }
                DebugCallStack.Clear();
                foreach (var frame in callStack) DebugCallStack.Add(frame);
            });

            foreach (var node in reexpand)
                await LoadArrayChildrenAsync(node);
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => SetStatus($"Failed to refresh variables/call stack: {ex.Message}", StatusType.Error));
        }
    }

    /// <summary>Reads an array's elements from the machine into its node's children (once; a refresh re-reads an expanded one).</summary>
    public async Task LoadArrayChildrenAsync(DebugVariableNode node)
    {
        if (!node.IsArray || node.ChildrenLoaded || DebugSession is not { } session) return;

        node.IsLoading = true;
        try
        {
            int elementWidth = node.ElementType switch { BasicVariableType.Integer => 2, BasicVariableType.String => 3, _ => 5 };
            int elementCount = 1;
            foreach (int size in node.DimensionSizes) elementCount *= size;

            byte[] data = elementCount > 0 ? await session.ReadMemoryAsync(node.DataAddress, elementCount * elementWidth) : [];
            var elements = VariableTableParser.ParseArrayElements(data, node.DataAddress, node.Name, node.ElementType, node.DimensionSizes).ToList();
            await ResolveStringValuesAsync(session, elements, maxResolutions: int.MaxValue);

            RunOnUiThread(() =>
            {
                node.Children.Clear();
                foreach (var element in elements)
                    node.Children.Add(new DebugVariableNode(element) { IsUpperCaseModeActive = node.IsUpperCaseModeActive });
                node.ChildrenLoaded = true;
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => SetStatus($"Failed to load {node.Name}'s contents: {ex.Message}", StatusType.Error));
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    /// <summary>Re-applies the debugged tab's Upper/Lower Case Mode to every node's display, after the mode is toggled.</summary>
    public void RefreshDebugVariablesDisplayMode()
    {
        bool isUpperCaseModeActive = DebugTab?.IsUpperCaseModeActive ?? true;
        foreach (var node in DebugVariables)
        {
            node.IsUpperCaseModeActive = isUpperCaseModeActive;
            foreach (var child in node.Children) child.IsUpperCaseModeActive = isUpperCaseModeActive;
        }
    }

    private const int _headerReadSize = 64;
    private const int _bigHeaderReadSize = 1024;

    // Walks the array table header by header; a header that doesn't fit the small read is
    // retried with a bigger one, and a corrupt one ends the walk rather than looping.
    private static async Task<List<(string Name, BasicVariableType ElementType, IReadOnlyList<int> DimensionSizes, ushort DataAddress)>> LoadArrayHeadersAsync(IDebugSession session, ushort arytab, ushort strend)
    {
        var headers = new List<(string, BasicVariableType, IReadOnlyList<int>, ushort)>();
        ushort address = arytab;
        while (address < strend)
        {
            int remaining = strend - address;
            byte[] chunk = await session.ReadMemoryAsync(address, Math.Min(_headerReadSize, remaining));
            var header = VariableTableParser.TryParseArrayHeader(chunk, 0);
            if (header == null && remaining > _headerReadSize)
            {
                chunk = await session.ReadMemoryAsync(address, Math.Min(_bigHeaderReadSize, remaining));
                header = VariableTableParser.TryParseArrayHeader(chunk, 0);
            }
            if (header == null) break;

            headers.Add((header.Name, header.ElementType, header.DimensionSizes, (ushort)(address + header.DataOffset)));
            if (header.EntryLength <= 0) break;
            address = (ushort)(address + header.EntryLength);
        }
        return headers;
    }

    private static async Task ResolveStringValuesAsync(IDebugSession session, List<BasicVariable> variables, int maxResolutions)
    {
        int resolutions = 0;
        for (int i = 0; i < variables.Count && resolutions < maxResolutions; i++)
        {
            if (variables[i] is not { Type: BasicVariableType.String, Value: StringDescriptor descriptor }) continue;
            resolutions++;
            if (descriptor.Length == 0) continue;

            byte[] chars = await session.ReadMemoryAsync(descriptor.HeapPointer, descriptor.Length);
            string text = new(chars.Select(b => (char)b).ToArray());
            variables[i] = variables[i] with { Value = new ResolvedStringValue(text, descriptor.HeapPointer) };
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

    // Starts a new BASIC debug session on VICE for the active tab. Test hooks
    // (DebugTransferOverride/DebugSessionFactoryOverride) let unit tests substitute a fake
    // session and skip the real transfer.
    private Task DebugStartOnViceAsync()
    {
        if (DebugTransferOverride == null && !EnsureVicePathConfigured()) return Task.CompletedTask;

        return StartDebugSessionAsync(
            "VICE",
            async (tab, prgData) =>
            {
                if (DebugTransferOverride != null)
                    await DebugTransferOverride(tab, prgData);
                else
                    await new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort)
                        .TransferAsync(Settings.ViceEmulatorPath, prgData, tab.FileName, Settings.ViceBringToForeground);
            },
            async () => DebugSessionFactoryOverride != null
                ? await DebugSessionFactoryOverride()
                : (IDebugSession)await ViceDebugSession.StartAsync(Settings.ViceMonitorHost, Settings.ViceMonitorPort),
            session =>
            {
                if (session is not ViceDebugSession vice) return Task.CompletedTask;
                // From here on, a checkpoint hit at the direct-mode sentinel means the program
                // really finished, not startup noise from the autostart transfer's reset.
                vice.MarkRunTyped();
                return vice.TypeAsync("RUN\r");
            });
    }

    // Starts a new BASIC debug session on a C64 Ultimate for the active tab. Unlike VICE's
    // persistent binary-monitor connection, every C64U REST call is an independent, stateless
    // HTTP request, so typing RUN\r through a fresh C64UltimateClient (rather than routing it
    // through the session) is safe here.
    private Task DebugStartOnC64UAsync()
    {
        if (string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("Please set the Commodore 64 Ultimate URL in Settings first.", StatusType.Error);
            return Task.CompletedTask;
        }

        var client = new C64UltimateClient();

        return StartDebugSessionAsync(
            "the C64 Ultimate",
            async (_, prgData) =>
            {
                await client.LoadPrgAsync(Settings.C64UUrl, prgData);
                // A load appears to trigger a machine reset, same as VICE's autostart - without
                // settling first, uploading the debug stub and patching the GONE vector
                // immediately afterward races that reset.
                await Task.Delay(_sysCommandDelay);
            },
            async () => (IDebugSession)await C64UDebugSession.StartAsync(Settings.C64UUrl),
            _ => client.TypeAsync(Settings.C64UUrl, "RUN\r"));
    }

    // Shared session-start orchestration for both targets: builds the line table and tokenized
    // program from the exact editor text (no minify, so the lines the user set breakpoints on
    // are the lines that run), transfers it, opens the session, arms every enabled breakpoint,
    // and types RUN.
    private async Task StartDebugSessionAsync(
        string targetName,
        Func<EditorTab, byte[], Task> transferAsync,
        Func<Task<IDebugSession>> createSessionAsync,
        Func<IDebugSession, Task> typeRunAsync)
    {
        if (DebugSession != null) return;

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

            SetStatus($"Transferring program to {targetName}…");
            await transferAsync(tab, prgData);

            SetStatus("Opening debug connection…");
            session = await createSessionAsync();

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
            await typeRunAsync(session);

            SetStatus($"Debugging on {targetName}. Running…");
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
        // Curlin 0xFFFF is BASIC's "no program running" sentinel: the program returned to READY
        // on its own (END, falling off the end, STOP, a runtime error). Nothing is left to
        // inspect, so detach entirely rather than staying attached with Restart/Stop enabled.
        if (e.Curlin == 0xFFFF)
        {
            RunOnUiThread(() => _ = DebugStopAsync("Program finished - back at READY."));
            return;
        }

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
