// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Renders each tab character as a blank run reaching the next tab stop, counted from the start
/// of the line. Left to the text formatter, a tab is measured from the start of its own text
/// run - and the colorizers split a line into runs, so a tab after a label came out as a fixed
/// four columns wherever it was rather than aligning the mnemonic with the lines around it.
/// Columns are exact here because the assembly font is monospace. For assembly tabs only;
/// BASIC never contains real tabs (CHR$(9) is a PETSCII control code there).
/// </summary>
public sealed class TabStopElementGenerator : VisualLineElementGenerator
{
    #region Public Properties

    /// <summary>Gets or sets whether tabs are handled; off for BASIC tabs.</summary>
    public bool IsEnabled { get; set; }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!IsEnabled) return -1;

        var document = CurrentContext.Document;
        int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        for (int i = startOffset; i < endOffset; i++)
            if (document.GetCharAt(i) == '\t') return i;
        return -1;
    }

    /// <inheritdoc/>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        if (document.GetCharAt(offset) != '\t') return null;

        int indent = Math.Max(1, CurrentContext.TextView.Options.IndentationSize);
        int column = 0;
        for (int i = CurrentContext.VisualLine.FirstDocumentLine.Offset; i < offset; i++)
            column += document.GetCharAt(i) == '\t' ? indent - column % indent : 1;

        return new TabStopElement(indent - column % indent);
    }

    #endregion

    private sealed class TabStopElement : VisualLineElement
    {
        private readonly int _columns;

        public TabStopElement(int columns) : base(1, 1)
        {
            _columns = columns;
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
            new TabStopRun(TextRunProperties, _columns * context.TextView.WideSpaceWidth, context.TextView.DefaultLineHeight, context.TextView.DefaultBaseline);

        public override bool IsWhitespace(int visualColumn) => true;
    }

    private sealed class TabStopRun : DrawableTextRun
    {
        private readonly double _width;
        private readonly double _height;

        public TabStopRun(TextRunProperties properties, double width, double height, double baseline)
        {
            Properties = properties;
            _width = width;
            _height = height;
            Baseline = baseline;
        }

        public override TextRunProperties Properties { get; }

        public override double Baseline { get; }

        public override Size Size => new(_width, _height);

        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }
}
