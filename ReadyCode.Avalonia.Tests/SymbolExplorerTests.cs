// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The Variable Explorer (BASIC) / Symbol Explorer (assembly) under the folder explorer.
/// </summary>
public class SymbolExplorerTests
{
    #region Public Methods

    [AvaloniaFact]
    public void BasicTab_ListsEveryVariable_WithReadWriteOccurrencesByBasicLine()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 A=1:B$=\"X\"\n20 PRINT A;B$\n30 A=A+1";
        window.RunDiagnosticsNow();

        Assert.Equal(["A", "B$"], vm.Variables.Select(v => v.Name));
        Assert.Equal("VARIABLES (2)", vm.SymbolPanelTitle);
        Assert.Equal("FLT", vm.Variables[0].TypeBadge);
        Assert.Equal("STR", vm.Variables[1].TypeBadge);

        var a = vm.Variables[0].Occurrences;
        Assert.Equal(["Line 10 — Set", "Line 20 — Read", "Line 30 — Set", "Line 30 — Read"], a.Select(o => o.DisplayText));
        Assert.Equal([1, 2, 3, 3], a.Select(o => o.DocumentLineNumber));
        Assert.Empty(vm.ConstantSymbols.Symbols);
    }

    [AvaloniaFact]
    public void Editing_UpdatesInPlace_KeepingAnExpandedVariableExpanded()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 A=1\n20 B=2";
        window.RunDiagnosticsNow();
        var a = vm.Variables[0];
        a.IsExpanded = true;

        editor.Document.Text = "10 A=1\n20 C=3\n30 A=A";
        window.RunDiagnosticsNow();

        Assert.Same(a, vm.Variables[0]);
        Assert.True(vm.Variables[0].IsExpanded);
        Assert.Equal(["A", "C"], vm.Variables.Select(v => v.Name));
        Assert.Equal(3, a.Occurrences.Count);
    }

    [AvaloniaFact]
    public void AssemblyTab_GroupsConstantsAndLabels_WithValues()
    {
        var (window, vm, editor) = Show(EditorLanguage.Asm);
        editor.Document.Text = "BORDER = $D020\n        * = $C000\nSTART:  LDA #0\n        STA BORDER\n        JMP START";
        window.RunDiagnosticsNow();

        Assert.Empty(vm.Variables);
        Assert.Equal(["BORDER"], vm.ConstantSymbols.Symbols.Select(s => s.Name));
        Assert.Equal("$D020", vm.ConstantSymbols.Symbols[0].ValueText);
        Assert.Equal("CONST", vm.ConstantSymbols.Symbols[0].TypeBadge);
        Assert.Equal(["START"], vm.LabelSymbols.Symbols.Select(s => s.Name));
        Assert.Equal("$C000", vm.LabelSymbols.Symbols[0].ValueText);
        Assert.Equal(["Line 3 — Defined", "Line 5 — Used"], vm.LabelSymbols.Symbols[0].Occurrences.Select(o => o.DisplayText));
        Assert.Equal("SYMBOLS (2)", vm.SymbolPanelTitle);
    }

    [AvaloniaFact]
    public void RenameVariable_ReplacesEveryOccurrence_AsOneUndoStep_AndRejectsKeywords()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 SC=0:S=1\n20 SC=SC+S\n30 PRINT SC";
        window.RunDiagnosticsNow();

        // Lower case is accepted and upper-cased. (Not "SCORE": C64 BASIC crunches that as
        // SC OR E, and the analyzer knows it.)
        Assert.Equal(4, vm.RenameVariable("SC", "pts"));
        Assert.Equal("10 PTS=0:S=1\n20 PTS=PTS+S\n30 PRINT PTS", editor.Document.Text);
        Assert.Equal(["PTS", "S"], vm.Variables.Select(v => v.Name));

        editor.Undo();
        Assert.Equal("10 SC=0:S=1\n20 SC=SC+S\n30 PRINT SC", editor.Document.Text);

        Assert.Equal(-1, vm.RenameVariable("SC", "FOR"));
        Assert.Equal(-1, vm.RenameVariable("SC", "1A"));
        Assert.Equal(0, vm.RenameVariable("SC", "SC"));
        Assert.Equal("10 SC=0:S=1\n20 SC=SC+S\n30 PRINT SC", editor.Document.Text);
    }

    [AvaloniaFact]
    public void Panel_ShowsUnderTheExplorer_AndViewVariablesHidesIt()
    {
        var (window, vm, editor) = Show(EditorLanguage.Basic);
        editor.Document.Text = "10 A=1";
        window.RunDiagnosticsNow();
        Dispatcher.UIThread.RunJobs();

        var panel = window.FindControl<DockPanel>("VariablesPanel")!;
        var tree = window.FindControl<TreeView>("VariablesTree")!;
        Assert.True(vm.ShowVariableExplorer);
        Assert.True(panel.IsVisible);
        Assert.True(tree.IsVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "VARIABLES (1)");
        Assert.Contains(tree.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A");
        RenderCapture.Save(window, "symbol-explorer.png");

        vm.ShowVariableExplorer = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.IsVisible);
        Assert.Equal(0, window.FindControl<Grid>("ExplorerPanel")!.RowDefinitions[2].Height.Value);

        vm.ShowVariableExplorer = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsVisible);
    }

    [AvaloniaFact]
    public void SwitchingToAnAssemblyTab_ShowsTheSymbolsTree_Instead()
    {
        var (window, vm, _) = Show(EditorLanguage.Basic);
        var variables = window.FindControl<TreeView>("VariablesTree")!;
        var symbols = window.FindControl<TreeView>("SymbolsTree")!;
        Assert.True(variables.IsVisible);
        Assert.False(symbols.IsVisible);

        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        Assert.False(variables.IsVisible);
        Assert.True(symbols.IsVisible);
        Assert.StartsWith("SYMBOLS", vm.SymbolPanelTitle);
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, MainViewModel Vm, AvaloniaEdit.TextEditor Editor) Show(EditorLanguage language)
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        vm.NewTab(language);
        Dispatcher.UIThread.RunJobs();
        return (window, vm, window.FindControl<AvaloniaEdit.TextEditor>("Editor")!);
    }

    #endregion
}
