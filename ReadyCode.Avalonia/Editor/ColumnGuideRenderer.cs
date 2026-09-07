// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Draws a vertical guide line after a given text column (e.g. 40 for the C64's screen width).
/// </summary>
public sealed class ColumnGuideRenderer : IBackgroundRenderer
{
    private IPen _pen = new Pen(new SolidColorBrush(Color.Parse("#40FF0000")), 1);

    /// <summary>Gets or sets the 1-based column the guide is drawn after; 0 or less hides it.</summary>
    public int Column { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetColor(Color color) => _pen = new Pen(new SolidColorBrush(color), 1);

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Column <= 0 || textView.WideSpaceWidth <= 0) return;

        double x = Math.Round(Column * textView.WideSpaceWidth - textView.ScrollOffset.X) + 0.5;
        if (x < 0 || x > textView.Bounds.Width) return;

        drawingContext.DrawLine(_pen, new Point(x, 0), new Point(x, textView.Bounds.Height));
    }
}
