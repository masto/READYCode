// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors the leading BASIC line number on each line, with a distinct brush for the line the
/// caret is currently on.
/// </summary>
public class LineNumberColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _pattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw line numbers.
    /// </summary>
    public IBrush LineNumberBrush { get; set; } = Brushes.Gray;

    /// <summary>
    /// Gets or sets the brush used to draw the line number of the caret's line.
    /// </summary>
    public IBrush ActiveLineNumberBrush { get; set; } = Brushes.White;

    /// <summary>
    /// Gets or sets the 1-based document line the caret is on, or -1 for none.
    /// </summary>
    public int ActiveDocumentLineNumber { get; set; } = -1;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes the leading line number of <paramref name="line"/>, if it has one.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match match = _pattern.Match(text);
        if (!match.Success) return;

        int start = line.Offset + match.Groups[2].Index;
        int end   = start + match.Groups[2].Length;

        bool isActive = line.LineNumber == ActiveDocumentLineNumber;
        var brush = isActive ? ActiveLineNumberBrush : LineNumberBrush;
        ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
    }

    #endregion
}
