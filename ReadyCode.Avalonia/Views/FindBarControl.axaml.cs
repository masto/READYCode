// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The in-editor find/replace bar. It owns the search inputs and options; the main window
/// listens to its events and does the actual searching and replacing.
/// </summary>
public partial class FindBarControl : UserControl
{
    public FindBarControl()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? SearchChanged;
    public event EventHandler? FindNextRequested;
    public event EventHandler? FindPreviousRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? ReplaceAllRequested;

    public string SearchText  => SearchBox.Text ?? "";
    public string ReplaceText => ReplaceBox.Text ?? "";
    public bool MatchCase     => MatchCaseBtn.IsChecked == true;
    public bool WholeWord     => WholeWordBtn.IsChecked == true;
    public bool UseRegex      => RegexBtn.IsChecked == true;

    /// <summary>Shows the bar, seeding the search box with the given text, and focuses it.</summary>
    public void Open(string initialText, bool replaceMode)
    {
        if (!string.IsNullOrEmpty(initialText) && !initialText.Contains('\n'))
            SearchBox.Text = initialText;
        ExpandBtn.IsChecked = replaceMode;
        IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }, DispatcherPriority.Render);
    }

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetMatchCount(int current, int total)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            MatchCountText.Text = "";
            SearchBox.Classes.Remove("noResults");
        }
        else if (total == 0)
        {
            MatchCountText.Text = "No results";
            SearchBox.Classes.Add("noResults");
        }
        else
        {
            MatchCountText.Text = $"{current} of {total}";
            SearchBox.Classes.Remove("noResults");
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close(); e.Handled = true; break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                FindPreviousRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; break;
            case Key.Enter:
                FindNextRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; break;
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => SearchChanged?.Invoke(this, EventArgs.Empty);

    private void ReplaceBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close(); e.Handled = true; break;
            case Key.Enter:
                ReplaceRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; break;
        }
    }

    private void Option_Changed(object? sender, RoutedEventArgs e) => SearchChanged?.Invoke(this, EventArgs.Empty);

    private void ExpandBtn_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        bool expanded = ExpandBtn.IsChecked == true;
        ExpandArrow.Text = expanded ? "▾" : "▸";
        ReplaceRow.IsVisible = expanded;
    }

    private void PrevMatch_Click(object? sender, RoutedEventArgs e) => FindPreviousRequested?.Invoke(this, EventArgs.Empty);
    private void NextMatch_Click(object? sender, RoutedEventArgs e) => FindNextRequested?.Invoke(this, EventArgs.Empty);
    private void Replace_Click(object? sender, RoutedEventArgs e) => ReplaceRequested?.Invoke(this, EventArgs.Empty);
    private void ReplaceAll_Click(object? sender, RoutedEventArgs e) => ReplaceAllRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
