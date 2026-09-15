// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Input;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The Variable Explorer / Symbol Explorer under the folder explorer: its show/hide layout
/// (View > Variables, persisted with the split height), jumping to an occurrence, and renaming
/// a variable. The index itself is the view model's - see MainViewModel.SymbolIndex.cs.
/// </summary>
public partial class MainWindow
{
    #region Private Methods

    // Same row arrangement as WPF: the folder tree takes a persisted fixed height and the
    // Variable Explorer the rest; hidden, the folder tree takes the whole column.
    private void ApplyVariableExplorerLayout()
    {
        bool show = ViewModel.ShowVariableExplorer;
        var folderRow = ExplorerPanel.RowDefinitions[0];
        var splitterRow = ExplorerPanel.RowDefinitions[1];
        var variablesRow = ExplorerPanel.RowDefinitions[2];

        VariablesPanel.IsVisible = show;
        ExplorerRowSplitter.IsVisible = show;
        if (show)
        {
            folderRow.Height = new GridLength(Math.Clamp(ViewModel.Settings.ExplorerFolderTreeHeight, 60, 600));
            folderRow.MinHeight = 60;
            splitterRow.Height = new GridLength(4);
            variablesRow.MinHeight = 60;
            variablesRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            PersistFolderTreeHeight();
            folderRow.Height = new GridLength(1, GridUnitType.Star);
            folderRow.MinHeight = 0;
            splitterRow.Height = new GridLength(0);
            variablesRow.MinHeight = 0;
            variablesRow.Height = new GridLength(0);
        }
    }

    private void PersistFolderTreeHeight()
    {
        var folderRow = ExplorerPanel.RowDefinitions[0];
        if (ViewModel.ShowVariableExplorer && folderRow.Height.IsAbsolute && folderRow.Height.Value > 0)
            ViewModel.Settings.ExplorerFolderTreeHeight = folderRow.Height.Value;
    }

    private void ViewVariables_Click(object? sender, EventArgs e) => ViewModel.ShowVariableExplorer = !ViewModel.ShowVariableExplorer;

    private void VariablesTree_DoubleTapped(object? sender, TappedEventArgs e) => JumpToSelectedOccurrence(VariablesTree);

    private void SymbolsTree_DoubleTapped(object? sender, TappedEventArgs e) => JumpToSelectedOccurrence(SymbolsTree);

    private async void VariablesTree_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2 when VariablesTree.SelectedItem is VariableInfo variable:
                e.Handled = true;
                await RenameVariableAsync(variable);
                break;
            case Key.Enter:
                e.Handled = JumpToSelectedOccurrence(VariablesTree);
                break;
        }
    }

    private void SymbolsTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            e.Handled = JumpToSelectedOccurrence(SymbolsTree);
    }

    private void VariablesTree_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var menu = new MenuFlyout();
        switch (VariablesTree.SelectedItem)
        {
            case VariableInfo variable:
                menu.Items.Add(MenuItemFor("Rename…", "F2", async () => await RenameVariableAsync(variable)));
                break;
            case VariableOccurrenceInfo occurrence:
                menu.Items.Add(MenuItemFor("Go to Line", null, () => MoveCaretToDocumentLine(occurrence.DocumentLineNumber)));
                break;
            default:
                return;
        }
        menu.ShowAt(VariablesTree, showAtPointer: true);
        e.Handled = true;
    }

    private static MenuItem MenuItemFor(string header, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = gesture == null ? null : KeyGesture.Parse(gesture) };
        item.Click += (_, _) => action();
        return item;
    }

    // Double-click / Enter on an occurrence row goes to its line. A variable or symbol row just
    // toggles, which the tree does itself.
    private bool JumpToSelectedOccurrence(TreeView tree)
    {
        int? line = tree.SelectedItem switch
        {
            VariableOccurrenceInfo v => v.DocumentLineNumber,
            AsmSymbolOccurrenceInfo s => s.DocumentLineNumber,
            _ => null,
        };
        if (line is not { } documentLine) return false;
        MoveCaretToDocumentLine(documentLine);
        return true;
    }

    private async Task RenameVariableAsync(VariableInfo variable)
    {
        string? newName = await TextPromptDialog.ShowAsync(this, "Rename Variable", $"Rename {variable.Name} everywhere in this file to:", variable.Name);
        if (newName == null) return;
        ViewModel.RenameVariable(variable.Name, newName);
    }

    #endregion
}
