// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors the leading BASIC line number on each line, with a distinct brush for the line the
/// caret is on.
/// </summary>
public class LineNumberColorizer : DocumentColorizingTransformer
{
    private static readonly Regex _pattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);

    public IBrush LineNumberBrush       { get; set; } = Brushes.Gray;
    public IBrush ActiveLineNumberBrush { get; set; } = Brushes.White;
    public int    ActiveDocumentLineNumber { get; set; } = -1;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match match = _pattern.Match(text);
        if (!match.Success) return;

        int start = line.Offset + match.Groups[2].Index;
        int end   = start + match.Groups[2].Length;

        bool isActive = line.LineNumber == ActiveDocumentLineNumber;
        var brush = isActive ? ActiveLineNumberBrush : LineNumberBrush;
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
    }
}
