// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// The assembly editor's gutter: sequential line numbers (assembly source, unlike BASIC, has
/// none in the text), or - for a disassembly, or assembled source with an explicit origin - the
/// memory address of each line. A port of the WPF margin of the same name.
/// </summary>
public sealed class AsmLineNumberMargin : AbstractMargin
{
    #region Private Fields

    private const double _rightPadding = 6;
    private static readonly Typeface _typeface = new(new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"));

    private IBrush _textBrush = Brushes.Gray;
    private double _fontSize = 12;
    private int _zeroPadWidth;
    private IReadOnlyDictionary<int, ushort>? _lineAddresses;

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the brush the numbers are drawn with.</summary>
    public IBrush TextBrush
    {
        get => _textBrush;
        set { _textBrush = value; InvalidateVisual(); }
    }

    /// <summary>Gets or sets the font size, matching the editor's.</summary>
    public double FontSize
    {
        get => _fontSize;
        set { _fontSize = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>Gets or sets how many digits line numbers are zero-padded to, or 0 for none - the BASIC padding setting, mirrored.</summary>
    public int ZeroPadWidth
    {
        get => _zeroPadWidth;
        set { _zeroPadWidth = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>
    /// Gets or sets the address of each document line (1-based), or null for line numbers. A
    /// line with no entry (an ".org" line, a comment) is left blank rather than shown with a
    /// number that would read as an address.
    /// </summary>
    public IReadOnlyDictionary<int, ushort>? LineAddresses
    {
        get => _lineAddresses;
        set { _lineAddresses = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        string widest = LineAddresses != null
            ? "$FFFF"
            : new string('8', Math.Max(ZeroPadWidth, (Document?.LineCount ?? 1).ToString(CultureInfo.InvariantCulture).Length));
        return new Size(CreateFormattedText(widest).Width + _rightPadding + 4, 0);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        // A transparent fill so the margin has a hit-test area for its own width.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        foreach (var line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            string text;
            if (LineAddresses != null)
            {
                if (!LineAddresses.TryGetValue(lineNumber, out ushort address)) continue;
                text = "$" + address.ToString("X4", CultureInfo.InvariantCulture);
            }
            else
            {
                text = ZeroPadWidth > 0
                    ? lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(ZeroPadWidth, '0')
                    : lineNumber.ToString(CultureInfo.InvariantCulture);
            }

            var formatted = CreateFormattedText(text);
            double y = line.VisualTop - textView.ScrollOffset.Y + (line.Height - formatted.Height) / 2;
            context.DrawText(formatted, new Point(Bounds.Width - _rightPadding - formatted.Width, y));
        }
    }

    /// <inheritdoc/>
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
        InvalidateMeasure();
    }

    /// <inheritdoc/>
    protected override void OnDocumentChanged(AvaloniaEdit.Document.TextDocument? oldDocument, AvaloniaEdit.Document.TextDocument? newDocument)
    {
        base.OnDocumentChanged(oldDocument, newDocument);
        InvalidateMeasure();
    }

    #endregion

    #region Private Methods

    private void TextView_Changed(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private FormattedText CreateFormattedText(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, TextBrush);

    #endregion
}
