// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// A minimal modal prompt for a single line of text (new file name, rename...). Returns the
/// trimmed text, or null if cancelled or left empty.
/// </summary>
public static class TextPromptDialog
{
    public static async Task<string?> ShowAsync(Window owner, string title, string label, string initialText = "")
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

        string? result = null;
        var box = new TextBox { Text = initialText };
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => { result = box.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 6, 0, 0),
                    Children = { cancel, ok },
                },
            },
        };

        dialog.Opened += (_, _) =>
        {
            box.Focus();
            // Select the base name so typing replaces it but the extension is kept by default.
            int dot = initialText.LastIndexOf('.');
            box.SelectionStart = 0;
            box.SelectionEnd = dot > 0 ? dot : initialText.Length;
        };

        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
