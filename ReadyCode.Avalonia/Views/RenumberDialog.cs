// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Edit > Renumber Code's options, as in WPF since 2.5.0: the starting line number, the
/// increment, and whether to renumber every line or only the selected ones.
/// </summary>
public static class RenumberDialog
{
    /// <summary>What the user chose.</summary>
    public sealed record Choice(int StartLineNumber, int Increment, bool SelectedOnly);

    #region Public Methods

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    /// <param name="owner">The window to show the dialog over.</param>
    /// <param name="defaultStart">The starting number to offer.</param>
    /// <param name="defaultIncrement">The increment to offer.</param>
    /// <param name="hasSelection">Whether lines are selected; if so "Selected lines only" is offered and preselected.</param>
    /// <returns>The choice, or null if cancelled.</returns>
    public static async Task<Choice?> ShowAsync(Window owner, int defaultStart, int defaultIncrement, bool hasSelection)
    {
        var dialog = new Window
        {
            Title = "Renumber Code",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var startBox = new TextBox { Text = defaultStart.ToString() };
        var incrementBox = new TextBox { Text = defaultIncrement.ToString() };
        var allRadio = new RadioButton { Content = "All lines", GroupName = "scope", IsChecked = !hasSelection };
        var selectedRadio = new RadioButton { Content = "Selected lines only", GroupName = "scope", IsChecked = hasSelection, IsEnabled = hasSelection };

        Choice? result = null;
        void TryAccept()
        {
            if (!int.TryParse(startBox.Text?.Trim(), out int start) || start < 0) return;
            if (!int.TryParse(incrementBox.Text?.Trim(), out int increment) || increment <= 0) return;
            result = new Choice(start, increment, selectedRadio.IsChecked == true);
            dialog.Close();
        }

        var ok = new Button { Content = "Renumber", MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => TryAccept();
        cancel.Click += (_, _) => dialog.Close();
        foreach (var box in new[] { startBox, incrementBox })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { TryAccept(); e.Handled = true; } };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Starting line number:" },
                startBox,
                new TextBlock { Text = "Increment:", Margin = new Thickness(0, 6, 0, 0) },
                incrementBox,
                new TextBlock { Text = "Renumber:", Margin = new Thickness(0, 6, 0, 0) },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { allRadio, selectedRadio } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { cancel, ok },
                },
            },
        };

        dialog.Opened += (_, _) => { startBox.Focus(); startBox.SelectAll(); };

        await dialog.ShowDialog(owner);
        return result;
    }

    #endregion
}
