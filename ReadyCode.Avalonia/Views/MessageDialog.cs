// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// A minimal modal message box: a message plus a row of buttons, returning the label of the
/// button pressed (or null if the dialog was dismissed).
/// </summary>
public static class MessageDialog
{
    public static Task ShowAsync(Window owner, string title, string message) =>
        ShowAsync(owner, title, message, "OK");

    public static async Task<string?> ShowAsync(Window owner, string title, string message, params string[] buttons)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        string? result = null;
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (string label in buttons)
        {
            var button = new Button { Content = label, MinWidth = 80, IsDefault = label == buttons[0], IsCancel = label == buttons[^1] };
            button.Click += (_, _) => { result = label; dialog.Close(); };
            buttonRow.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            MaxWidth = 520,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttonRow,
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
