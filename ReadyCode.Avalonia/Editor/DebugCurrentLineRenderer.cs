// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Tints the line a halted debug session is stopped on.
/// </summary>
public sealed class DebugCurrentLineRenderer : IBackgroundRenderer
{
    private static readonly IBrush _fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xD7, 0x00));

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Gets or sets the 1-based document line to highlight, or null for none.</summary>
    public int? CurrentLine { get; set; }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (CurrentLine is not { } currentLine) return;
        textView.EnsureVisualLines();

        foreach (var vl in textView.VisualLines)
        {
            if (vl.FirstDocumentLine.LineNumber > currentLine) break;
            if (vl.LastDocumentLine.LineNumber < currentLine) continue;

            double top = vl.VisualTop - textView.ScrollOffset.Y;
            drawingContext.FillRectangle(_fill, new Rect(0, top, textView.Bounds.Width, vl.Height));
            break;
        }
    }
}
