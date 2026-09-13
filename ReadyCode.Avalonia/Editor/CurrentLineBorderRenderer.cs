// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Draws a 1px top and bottom border on the line that contains the caret, matching VS Code's
/// current-line indicator style. A port of the WPF app's renderer of the same name.
/// </summary>
public sealed class CurrentLineBorderRenderer : IBackgroundRenderer
{
    #region Private Fields

    private readonly TextEditor _editor;
    private IPen _pen = MakePen(Color.FromRgb(0x45, 0x45, 0x45));

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentLineBorderRenderer"/> class.
    /// </summary>
    /// <param name="editor">The text editor whose current line should be bordered.</param>
    public CurrentLineBorderRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the rendering layer this renderer draws into.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Background;

    #endregion

    #region Public Methods

    /// <summary>
    /// Changes the border color used for the current line.
    /// </summary>
    /// <param name="color">The new border color.</param>
    public void SetColor(Color color) => _pen = MakePen(color);

    /// <summary>
    /// Draws the top and bottom border around the line that contains the caret.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to draw into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_editor.Document == null) return;

        textView.EnsureVisualLines();

        var caretLine = _editor.Document.GetLineByOffset(Math.Min(_editor.CaretOffset, _editor.Document.TextLength));

        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine.LineNumber > caretLine.LineNumber) break;
            if (visualLine.LastDocumentLine.LineNumber < caretLine.LineNumber) continue;

            double top, bottom;
            if (visualLine.TextLines.Count == 1)
            {
                // The caret positions itself from TextTop/TextBottom (baseline-anchored), not
                // LineTop/LineBottom, and the two differ whenever a line's own baseline/height
                // isn't the default 'x'-measured one - true for a genuinely empty line with the
                // Pet Me 64 font, which would otherwise leave the border a couple of pixels short
                // of the caret until the first character was typed. Using the caret's own formula
                // keeps them in agreement.
                var textLine = visualLine.TextLines[0];
                top = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.ScrollOffset.Y;
                bottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom) - textView.ScrollOffset.Y;
            }
            else
            {
                // A wrapped current line spans multiple rows - border the whole logical line
                // rather than collapsing to the caret's own row.
                top = visualLine.VisualTop - textView.ScrollOffset.Y;
                bottom = top + visualLine.Height;
            }
            double width = textView.Bounds.Width;

            // +0.5 / -0.5 snaps to pixel centres for a crisp 1px hairline. The top line gets an
            // extra 2px of padding above the text so it doesn't sit flush against it.
            drawingContext.DrawLine(_pen, new Point(0, top - 2.5), new Point(width, top - 2.5));
            drawingContext.DrawLine(_pen, new Point(0, bottom + 0.5), new Point(width, bottom + 0.5));
            break;
        }
    }

    #endregion

    #region Private Methods

    private static IPen MakePen(Color color) => new ImmutablePen(new ImmutableSolidColorBrush(color), 1.0);

    #endregion
}
