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
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FindBarControl"/> class.
    /// </summary>
    public FindBarControl()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the bar is closed, so the owner can clear its highlights.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Occurs when the search text or any option changes.
    /// </summary>
    public event EventHandler? SearchChanged;

    /// <summary>
    /// Occurs when the user asks for the next match.
    /// </summary>
    public event EventHandler? FindNextRequested;

    /// <summary>
    /// Occurs when the user asks for the previous match.
    /// </summary>
    public event EventHandler? FindPreviousRequested;

    /// <summary>
    /// Occurs when the user asks to replace the current match.
    /// </summary>
    public event EventHandler? ReplaceRequested;

    /// <summary>
    /// Occurs when the user asks to replace every match.
    /// </summary>
    public event EventHandler? ReplaceAllRequested;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the text to search for.
    /// </summary>
    public string SearchText => SearchBox.Text ?? "";

    /// <summary>
    /// Gets the text matches are replaced with.
    /// </summary>
    public string ReplaceText => ReplaceBox.Text ?? "";

    /// <summary>
    /// Gets whether the search is case sensitive.
    /// </summary>
    public bool MatchCase => MatchCaseBtn.IsChecked == true;

    /// <summary>
    /// Gets whether the search matches whole words only.
    /// </summary>
    public bool WholeWord => WholeWordBtn.IsChecked == true;

    /// <summary>
    /// Gets whether the search text is a regular expression.
    /// </summary>
    public bool UseRegex => RegexBtn.IsChecked == true;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the bar, seeding the search box with the given text, and focuses it.
    /// </summary>
    /// <param name="initialText">Text to seed the search box with (usually the editor's selection).</param>
    /// <param name="replaceMode">Whether to show the replace row as well.</param>
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

    /// <summary>
    /// Hides the bar and raises <see cref="CloseRequested"/>.
    /// </summary>
    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the "n of m" match counter, flagging the search box when nothing matched.
    /// </summary>
    /// <param name="current">The 1-based index of the current match.</param>
    /// <param name="total">The total number of matches.</param>
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

    #endregion

    #region Private Methods

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

    #endregion
}
