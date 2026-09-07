// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AvaloniaEdit.Document;
using ReadyCode.Editor;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Bridges AvaloniaEdit's <see cref="TextDocument"/> to the editor-independent line model the
/// cross-platform analyses in <see cref="ReadyCode.Editor"/> work over.
/// </summary>
public static class TextDocumentExtensions
{
    #region Public Methods

    /// <summary>
    /// Snapshots every line of <paramref name="document"/> as a <see cref="SourceLine"/>, with the
    /// same offsets AvaloniaEdit reports for the corresponding <see cref="DocumentLine"/>.
    /// </summary>
    /// <param name="document">The document to snapshot.</param>
    public static List<SourceLine> ToSourceLines(this TextDocument document)
    {
        var lines = new List<SourceLine>(document.LineCount);

        foreach (var line in document.Lines)
            lines.Add(new SourceLine(line.Offset, document.GetText(line)));

        return lines;
    }

    #endregion
}
