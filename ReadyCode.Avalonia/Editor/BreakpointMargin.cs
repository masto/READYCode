// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// The breakpoint gutter: a red dot on lines with an enabled breakpoint, a hollow grey one for a
/// disabled breakpoint. Clicking a line asks the window to toggle it.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    #region Private Fields

    private const double _width = 16;
    private const double _dotDiameter = 10;
    private static readonly IBrush _enabledFill = new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x23));
    private static readonly IPen _disabledPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1.0);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the document lines that have an enabled breakpoint (drawn as a filled dot).
    /// </summary>
    public IReadOnlySet<int> EnabledBreakpointLines { get; set; } = new HashSet<int>();

    /// <summary>
    /// Gets or sets the document lines that have a disabled breakpoint (drawn as a hollow dot).
    /// </summary>
    public IReadOnlySet<int> DisabledBreakpointLines { get; set; } = new HashSet<int>();

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the user clicks the margin, carrying the 1-based document line clicked.
    /// </summary>
    public event EventHandler<int>? BreakpointToggleRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Draws a dot for every breakpoint currently scrolled into view.
    /// </summary>
    /// <param name="drawingContext">The drawing context to render into.</param>
    public override void Render(DrawingContext drawingContext)
    {
        var textView = TextView;
        // Something has to be drawn everywhere for hit-testing to reach the margin at all.
        drawingContext.FillRectangle(Brushes.Transparent, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (textView == null || !textView.VisualLinesValid) return;

        foreach (VisualLine line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            bool enabled = EnabledBreakpointLines.Contains(lineNumber);
            bool disabled = !enabled && DisabledBreakpointLines.Contains(lineNumber);
            if (!enabled && !disabled) continue;

            double y = line.VisualTop - textView.ScrollOffset.Y + (line.Height - _dotDiameter) / 2;
            var center = new Point(_width / 2, y + _dotDiameter / 2);
            if (enabled)
                drawingContext.DrawEllipse(_enabledFill, null, center, _dotDiameter / 2, _dotDiameter / 2);
            else
                drawingContext.DrawEllipse(null, _disabledPen, center, _dotDiameter / 2, _dotDiameter / 2);
        }
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Reserves the margin's fixed width.
    /// </summary>
    /// <param name="availableSize">The space offered by the layout system.</param>
    protected override Size MeasureOverride(Size availableSize) => new(_width, 0);

    /// <summary>
    /// Re-subscribes the repaint handlers when the margin is attached to a different text view.
    /// </summary>
    /// <param name="oldTextView">The text view being detached from, if any.</param>
    /// <param name="newTextView">The text view being attached to, if any.</param>
    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= TextView_Changed;
            oldTextView.ScrollOffsetChanged -= TextView_Changed;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += TextView_Changed;
            newTextView.ScrollOffsetChanged += TextView_Changed;
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Raises <see cref="BreakpointToggleRequested"/> for the line the user clicked.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var textView = TextView;
        if (textView != null && textView.VisualLinesValid && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            double clickY = e.GetPosition(this).Y;
            foreach (VisualLine line in textView.VisualLines)
            {
                double top = line.VisualTop - textView.ScrollOffset.Y;
                if (clickY >= top && clickY < top + line.Height)
                {
                    BreakpointToggleRequested?.Invoke(this, line.FirstDocumentLine.LineNumber);
                    e.Handled = true;
                    break;
                }
            }
        }
        base.OnPointerPressed(e);
    }

    #endregion

    #region Private Methods

    private void TextView_Changed(object? sender, EventArgs e) => InvalidateVisual();

    #endregion
}
