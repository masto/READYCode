// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The Search panel's view side: Edit > Find in Files / Replace in Files open it, Enter in the
/// query box runs the search, a double-click on a result opens the file at the match, and
/// Replace All confirms then replaces. The search itself is the view model's - see
/// MainViewModel.Search.cs.
/// </summary>
public partial class MainWindow
{
    #region Private Methods

    private void EditFindInFiles_Click(object? sender, EventArgs e) => OpenProjectSearch(replaceMode: false);

    private void EditReplaceInFiles_Click(object? sender, EventArgs e) => OpenProjectSearch(replaceMode: true);

    // Shows the Search tab of the left panel with the query box focused - seeded with the
    // editor's selection, if any, as the single-file Find bar is.
    private void OpenProjectSearch(bool replaceMode)
    {
        if (!ViewModel.IsFolderOpen)
        {
            ViewModel.SetStatus("Open a folder to search across its files.", StatusType.Warning);
            return;
        }

        ViewModel.ActiveLeftPanelTab = "Search";
        ViewModel.IsExplorerOpen = true;
        SearchReplaceExpandBtn.IsChecked = replaceMode;

        if (!IsHexTabActive && !IsCompareTabActive && Editor.SelectionLength > 0 && !Editor.SelectedText.Contains('\n'))
            SearchQueryBox.Text = Editor.SelectedText;

        Dispatcher.UIThread.Post(() =>
        {
            SearchQueryBox.Focus();
            SearchQueryBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void SearchReplaceExpandBtn_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        bool expanded = SearchReplaceExpandBtn.IsChecked == true;
        SearchReplaceRow.IsVisible = expanded;
        SearchReplaceExpandArrow.Text = expanded ? "▾" : "▸";
    }

    private void SearchQueryBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        RunProjectSearch();
    }

    internal int RunProjectSearch() => ViewModel.RunProjectSearch(SearchQueryBox.Text ?? "",
        SearchMatchCaseBtn.IsChecked == true, SearchWholeWordBtn.IsChecked == true, SearchRegexBtn.IsChecked == true);

    private void SearchResultsTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ProjectSearchMatchInfo match)
        {
            e.Handled = true;
            GoToSearchMatch(match);
        }
    }

    private void SearchResultsTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchResultsTree.SelectedItem is ProjectSearchMatchInfo match)
        {
            e.Handled = true;
            GoToSearchMatch(match);
        }
    }

    // Opens (or activates) the match's file and selects the match.
    internal void GoToSearchMatch(ProjectSearchMatchInfo match)
    {
        if (ViewModel.FindOpenTab(match.File.FilePath) is { IsModified: true } dirty)
            ViewModel.ActiveTab = dirty; // keep the unsaved edits; the result may be off by a line, but nothing is lost
        else if (!ViewModel.OpenFile(match.File.FilePath))
            return;

        var document = Editor.Document;
        if (match.LineNumber > document.LineCount) return;
        int offset = document.GetOffset(match.LineNumber, match.ColumnOffset + 1);
        Editor.Select(offset, Math.Min(match.MatchLength, document.TextLength - offset));
        Editor.ScrollToLine(match.LineNumber);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private async void SearchReplaceAll_Click(object? sender, RoutedEventArgs e)
    {
        int totalMatches = ViewModel.SearchMatchCount;
        if (totalMatches == 0) return;

        int fileCount = ViewModel.SearchResults.Count;
        string? choice = await MessageDialog.ShowAsync(this, "Replace All in Project",
            $"Replace {totalMatches} occurrence{(totalMatches == 1 ? "" : "s")} across {fileCount} file{(fileCount == 1 ? "" : "s")}?",
            "Replace All", "Cancel");
        if (choice != "Replace All") return;

        ViewModel.ReplaceAllInProject(SearchReplaceBox.Text ?? "");
        if (FindBar.IsVisible) UpdateFindMatches();
        RunProjectSearch();
    }

    #endregion
}
