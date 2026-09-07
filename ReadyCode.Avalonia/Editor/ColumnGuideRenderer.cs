// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Draws the vertical column guide line (e.g. at column 40, the C64's screen width). AvaloniaEdit
/// has no built-in column ruler, unlike AvalonEdit's <c>Options.ColumnRulerPosition</c> the WPF
/// app uses, so the line is drawn here instead.
/// </summary>
public sealed class ColumnGuideRenderer : IBackgroundRenderer
{
    #region Private Fields

    private IPen _pen = MakePen(Color.Parse("#40FF0000"));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the 1-based column the guide is drawn after. Zero or less hides it.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets the rendering layer this renderer draws into.
    /// </summary>
    public KnownLayer Layer => KnownLayer.Background;

    #endregion

    #region Public Methods

    /// <summary>
    /// Changes the guide line's color.
    /// </summary>
    /// <param name="color">The new line color.</param>
    public void SetColor(Color color) => _pen = MakePen(color);

    /// <summary>
    /// Draws the guide line, if one is configured and currently scrolled into view.
    /// </summary>
    /// <param name="textView">The text view being rendered.</param>
    /// <param name="drawingContext">The drawing context to render into.</param>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Column <= 0 || textView.WideSpaceWidth <= 0) return;

        // Snapped to a half pixel so the 1px line renders crisply rather than blurred across two.
        double x = Math.Round(Column * textView.WideSpaceWidth - textView.ScrollOffset.X) + 0.5;
        if (x < 0 || x > textView.Bounds.Width) return;

        drawingContext.DrawLine(_pen, new Point(x, 0), new Point(x, textView.Bounds.Height));
    }

    #endregion

    #region Private Methods

    private static IPen MakePen(Color color) => new Pen(new SolidColorBrush(color), 1);

    #endregion
}
