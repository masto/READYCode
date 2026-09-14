// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// View > Code Statistics: character, word, line, and tokenized (or assembled) byte counts for
/// the active document, as in WPF.
/// </summary>
public partial class CodeStatisticsWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStatisticsWindow"/> class. Exists for
    /// the XAML designer; the app always uses the overload below.
    /// </summary>
    public CodeStatisticsWindow() : this(0, 0, 0, "Tokenized bytes", "0", "") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeStatisticsWindow"/> class with the
    /// given document statistics.
    /// </summary>
    /// <param name="charCount">The total character count.</param>
    /// <param name="wordCount">The total word count.</param>
    /// <param name="lineCount">The total line count.</param>
    /// <param name="byteCountLabel">The label for the byte-count row, e.g. "Tokenized bytes" or "Assembled bytes".</param>
    /// <param name="byteCountValue">The already-formatted byte-count value, e.g. "123 / 38,911" or "Assembly errors".</param>
    /// <param name="byteCountDescription">The explanatory text shown below the stats grid.</param>
    public CodeStatisticsWindow(int charCount, int wordCount, int lineCount, string byteCountLabel, string byteCountValue, string byteCountDescription)
    {
        InitializeComponent();

        CharCountText.Text = charCount.ToString("N0");
        WordCountText.Text = wordCount.ToString("N0");
        LineCountText.Text = lineCount.ToString("N0");
        TokenBytesLabelText.Text = byteCountLabel;
        TokenBytesText.Text = byteCountValue;
        TokenBytesDescriptionText.Text = byteCountDescription;
    }

    #endregion

    #region Private Methods

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    #endregion
}
