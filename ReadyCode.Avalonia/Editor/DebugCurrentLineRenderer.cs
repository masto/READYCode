// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Tints the line a halted debug session is currently stopped on.
/// </summary>
public sealed class DebugCurrentLineRenderer : IBackgroundRenderer
{
    #region Private Fields

    private static readonly IBrush _fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xD7, 0x00));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the 1-based document line to highlight, or null when not stopped.
    /// </summary>
    public int? CurrentLine { get; set; }

    /// <summary>
    /// Gets the rendering layer this renderer draws into.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Background;

    #endregion

    #region Public Methods

    /// <summary>
    /// Draws the highlight behind the current line, if it's scrolled into view.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to render into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (CurrentLine is not { } currentLine) return;

        textView.EnsureVisualLines();

        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine.LineNumber > currentLine) break;
            if (visualLine.LastDocumentLine.LineNumber < currentLine) continue;

            double top = visualLine.VisualTop - textView.ScrollOffset.Y;
            drawingContext.FillRectangle(_fill, new Rect(0, top, textView.Bounds.Width, visualLine.Height));
            break;
        }
    }

    #endregion
}
