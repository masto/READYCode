// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Edit > Go to Line: jump to a BASIC line number or a file line number, as in WPF. Returns
/// the number entered and which kind it is, or null if cancelled.
/// </summary>
public static class GoToLineDialog
{
    #region Public Methods

    /// <summary>
    /// Shows the dialog.
    /// </summary>
    /// <param name="owner">The window to show the dialog over.</param>
    /// <param name="minBasicLine">The smallest BASIC line number present in the document.</param>
    /// <param name="maxBasicLine">The largest BASIC line number present in the document.</param>
    /// <param name="fileLineCount">The total number of lines in the file.</param>
    /// <param name="hasBasicLines">Whether the document has any BASIC line numbers to jump between; if not, only file lines are offered.</param>
    /// <returns>The line number and whether it is a file line rather than a BASIC line, or null if cancelled.</returns>
    public static async Task<(int Line, bool IsFileLine)?> ShowAsync(Window owner, int minBasicLine, int maxBasicLine, int fileLineCount, bool hasBasicLines)
    {
        var dialog = new Window
        {
            Title = "Go to Line",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var basicRadio = new RadioButton { Content = "BASIC Line Number", IsChecked = hasBasicLines, IsEnabled = hasBasicLines, GroupName = "mode" };
        var fileRadio = new RadioButton { Content = "File Line Number", IsChecked = !hasBasicLines, GroupName = "mode" };
        var prompt = new TextBlock();
        var box = new TextBox();

        void UpdatePrompt() => prompt.Text = basicRadio.IsChecked == true
            ? $"Line number ({minBasicLine} - {maxBasicLine}):"
            : $"Line number (1 - {fileLineCount}):";
        basicRadio.IsCheckedChanged += (_, _) => UpdatePrompt();
        fileRadio.IsCheckedChanged += (_, _) => UpdatePrompt();
        UpdatePrompt();

        (int, bool)? result = null;
        void TryAccept()
        {
            if (int.TryParse(box.Text?.Trim(), out int line))
            {
                result = (line, fileRadio.IsChecked == true);
                dialog.Close();
            }
        }

        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => TryAccept();
        cancel.Click += (_, _) => dialog.Close();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { TryAccept(); e.Handled = true; } };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { basicRadio, fileRadio } },
                prompt,
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

        dialog.Opened += (_, _) => box.Focus();

        await dialog.ShowDialog(owner);
        return result;
    }

    #endregion
}
