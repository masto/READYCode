// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Draws the inline "ghost text" keyword-completion suggestion after the caret, in the editor's
/// own font but greyed out. A port of the WPF renderer of the same name; that one is an Adorner
/// that repaints itself off layout events, whereas this draws as part of the caret layer's own
/// render pass (above the text, below the caret), so it never has to re-enter the text view's
/// layout - the reentrancy the WPF version had to defer around can't arise.
/// </summary>
public sealed class GhostTextRenderer : IBackgroundRenderer
{
    #region Private Fields

    private static readonly IBrush _ghostBrush = new ImmutableSolidColorBrush(Color.FromArgb(180, 140, 140, 140));

    private readonly TextEditor _editor;
    private string _ghostText = string.Empty;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="GhostTextRenderer"/> class.
    /// </summary>
    /// <param name="editor">The editor to draw the suggestion in.</param>
    public GhostTextRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the text to draw after the caret. Empty means no suggestion. Setting it
    /// repaints the caret layer.
    /// </summary>
    public string GhostText
    {
        get => _ghostText;
        set
        {
            if (_ghostText == value) return;
            _ghostText = value;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        }
    }

    /// <summary>Gets the layer drawn into: the caret's, so the text sits above the document text.</summary>
    public KnownLayer Layer => KnownLayer.Caret;

    #endregion

    #region Public Methods

    /// <summary>
    /// Draws <see cref="GhostText"/> at the caret, if there is any and the caret is on screen.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to draw into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_ghostText.Length == 0 || _editor.Document == null || !textView.VisualLinesValid) return;

        var caret = _editor.TextArea.Caret.Position;
        var visualLine = textView.VisualLines.FirstOrDefault(vl =>
            vl.FirstDocumentLine.LineNumber <= caret.Line && vl.LastDocumentLine.LineNumber >= caret.Line);
        if (visualLine == null) return;

        int visualColumn = caret.VisualColumn >= 0 ? caret.VisualColumn : visualLine.GetVisualColumn(caret.Column - 1);
        var textLine = visualLine.GetTextLine(visualColumn, caret.IsAtEndOfLine);
        double x = visualLine.GetTextLineVisualXPosition(textLine, visualColumn) - textView.ScrollOffset.X;
        double y = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.ScrollOffset.Y;

        var formatted = new FormattedText(_ghostText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(_editor.FontFamily), _editor.FontSize, _ghostBrush);
        drawingContext.DrawText(formatted, new Point(x, y));
    }

    #endregion
}
