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
    #region Private Fields

    // AnchorSegment (rather than raw offset/length) so a highlight tracks its matched text through
    // edits elsewhere in the document between debounced re-searches.
    private readonly List<AnchorSegment> _matches = new();
    private int _currentIndex = -1;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the background brush used for matches other than the current one.
    /// </summary>
    public IBrush MatchBrush { get; set; } = Brushes.Yellow;

    /// <summary>
    /// Gets or sets the foreground brush used for matches other than the current one.
    /// </summary>
    public IBrush MatchFgBrush { get; set; } = Brushes.Black;

    /// <summary>
    /// Gets or sets the background brush used for the current match.
    /// </summary>
    public IBrush CurrentMatchBrush { get; set; } = Brushes.Orange;

    /// <summary>
    /// Gets or sets the foreground brush used for the current match.
    /// </summary>
    public IBrush CurrentMatchFgBrush { get; set; } = Brushes.Black;

    #endregion

    #region Public Methods

    /// <summary>
    /// Replaces the highlighted matches.
    /// </summary>
    /// <param name="document">The document the offsets refer to.</param>
    /// <param name="matches">Each match's offset and length, in document order.</param>
    /// <param name="currentIndex">Index of the match to draw as current, or -1 for none.</param>
    public void SetMatches(TextDocument document, IEnumerable<(int Offset, int Length)> matches, int currentIndex)
    {
        _matches.Clear();
        foreach (var (offset, length) in matches)
            _matches.Add(new AnchorSegment(document, offset, length));
        _currentIndex = currentIndex;
    }

    /// <summary>
    /// Removes every highlight.
    /// </summary>
    public void Clear()
    {
        _matches.Clear();
        _currentIndex = -1;
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes the portion of each match that falls on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
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

    #endregion
}
