// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors double-quoted string literals, including the quotes themselves.
/// </summary>
public class StringLiteralColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw string literals.
    /// </summary>
    public IBrush StringBrush { get; set; } = Brushes.Orange;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes every string literal on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            int start = i;
            i++;
            while (i < text.Length && text[i] != '"') i++;
            if (i < text.Length) i++; // include the closing quote

            int absoluteStart = line.Offset + start;
            int absoluteEnd   = line.Offset + i;
            ChangeLinePart(absoluteStart, absoluteEnd, element => element.TextRunProperties.SetForegroundBrush(StringBrush));
        }
    }

    #endregion
}
