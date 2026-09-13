// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// A modal list of check boxes with an OK/Cancel pair - the shape of the Minify and Prettify
/// dialogs. Returns the final state of every option, or null if cancelled.
/// </summary>
public static class OptionsDialog
{
    #region Public Methods

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    /// <param name="owner">The window to show the dialog over.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="prompt">The line of text above the options.</param>
    /// <param name="options">Each option's label and initial state, in display order.</param>
    /// <param name="okLabel">The confirming button's label.</param>
    /// <param name="footnote">Optional smaller text under the options.</param>
    /// <returns>The state of each option in the order given, or null if cancelled.</returns>
    public static async Task<bool[]?> ShowAsync(Window owner, string title, string prompt,
        IReadOnlyList<(string Label, bool IsChecked)> options, string okLabel, string? footnote = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var boxes = options.Select(o => new CheckBox { Content = o.Label, IsChecked = o.IsChecked }).ToList();
        bool[]? result = null;
        var ok = new Button { Content = okLabel, MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => { result = boxes.Select(b => b.IsChecked == true).ToArray(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        var content = new StackPanel { Margin = new Thickness(20), Spacing = 6 };
        content.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var box in boxes)
            content.Children.Add(box);
        if (footnote != null)
            content.Children.Add(new TextBlock { Text = footnote, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancel, ok },
        });
        dialog.Content = content;

        await dialog.ShowDialog(owner);
        return result;
    }

    #endregion
}
