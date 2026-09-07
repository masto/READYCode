// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors everything from a REM keyword to the end of the line as a comment, skipping REMs that
/// appear inside string literals.
/// </summary>
public class RemCommentColorizer : DocumentColorizingTransformer
{
    /// <summary>
    /// Gets or sets the brush used to draw comments.
    /// </summary>
    public IBrush CommentBrush { get; set; } = Brushes.Green;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            // Greedy match: REM at this exact position (no word-boundary guards -
            // CBM BASIC tokenizes LOREM as L·O·REM, coloring from REM to end of line).
            if (i + 3 > text.Length) break;

            bool isRem = char.ToUpperInvariant(text[i])     == 'R'
                      && char.ToUpperInvariant(text[i + 1]) == 'E'
                      && char.ToUpperInvariant(text[i + 2]) == 'M';
            if (!isRem) continue;

            int start = line.Offset + i;
            int end   = line.Offset + text.Length;
            ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(CommentBrush));
            return;
        }
    }
}
