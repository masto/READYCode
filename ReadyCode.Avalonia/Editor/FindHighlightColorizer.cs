// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Highlights every find match, with the current one in a distinct color.
/// </summary>
public class FindHighlightColorizer : DocumentColorizingTransformer
{
    // AnchorSegment (rather than raw offset/length) so a highlight tracks its matched text through
    // edits elsewhere in the document between debounced re-searches.
    private readonly List<AnchorSegment> _matches = new();
    private int _currentIndex = -1;

    public IBrush MatchBrush          { get; set; } = Brushes.Yellow;
    public IBrush MatchFgBrush        { get; set; } = Brushes.Black;
    public IBrush CurrentMatchBrush   { get; set; } = Brushes.Orange;
    public IBrush CurrentMatchFgBrush { get; set; } = Brushes.Black;

    public bool HasMatches => _matches.Count > 0;

    public void SetMatches(TextDocument document, IEnumerable<(int Offset, int Length)> matches, int currentIndex)
    {
        _matches.Clear();
        foreach (var (offset, length) in matches)
            _matches.Add(new AnchorSegment(document, offset, length));
        _currentIndex = currentIndex;
    }

    public void Clear()
    {
        _matches.Clear();
        _currentIndex = -1;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_matches.Count == 0) return;

        int lineStart = line.Offset;
        int lineEnd   = line.EndOffset;

        for (int i = 0; i < _matches.Count; i++)
        {
            var segment = _matches[i];
            int offset = segment.Offset;
            int length = segment.Length;
            if (offset + length <= lineStart || offset >= lineEnd) continue;

            int start = Math.Max(offset, lineStart);
            int end   = Math.Min(offset + length, lineEnd);
            bool isCurrent = i == _currentIndex;
            var bg = isCurrent ? CurrentMatchBrush   : MatchBrush;
            var fg = isCurrent ? CurrentMatchFgBrush : MatchFgBrush;

            ChangeLinePart(start, end, e =>
            {
                e.TextRunProperties.SetBackgroundBrush(bg);
                e.TextRunProperties.SetForegroundBrush(fg);
            });
        }
    }
}
