// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using DiffPlex.DiffBuilder.Model;

namespace ReadyCode.Avalonia.Editor;

/// <summary>The two hues every diff element uses - the same ones as WPF, and not themed, like GitHub's.</summary>
public static class DiffColors
{
    /// <summary>Gets the color of inserted lines and words.</summary>
    public static Color Inserted { get; } = Color.FromRgb(46, 160, 67);

    /// <summary>Gets the color of deleted lines and words.</summary>
    public static Color Deleted { get; } = Color.FromRgb(220, 53, 69);
}

/// <summary>Which side of a comparison a pane shows, which decides how a "Modified" line is tinted.</summary>
public enum DiffPaneRole
{
    /// <summary>The left (old) side of the split view.</summary>
    Old,

    /// <summary>The right (new) side of the split view.</summary>
    New,

    /// <summary>The single unified pane.</summary>
    Unified,
}

/// <summary>
/// Tints each row of a compare pane by its change type, across the full width - red for
/// deleted, green for inserted, grey for the filler rows the split view pads with - so an empty
/// filler row is tinted like any other. The differing words of a modified line are painted more
/// strongly by <see cref="DiffLineColorizer"/> on top.
/// </summary>
public sealed class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    #region Private Fields

    private static readonly IBrush _insertedBg = DiffLineColorizer.MakeBrush(DiffColors.Inserted, 40);
    private static readonly IBrush _deletedBg = DiffLineColorizer.MakeBrush(DiffColors.Deleted, 40);
    private static readonly IBrush _imaginaryBg = DiffLineColorizer.MakeBrush(Color.FromRgb(128, 128, 128), 35);

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the diff pieces, one per document line.</summary>
    public IReadOnlyList<DiffPiece>? Lines { get; set; }

    /// <summary>Gets or sets which side this pane shows.</summary>
    public DiffPaneRole Role { get; set; } = DiffPaneRole.Unified;

    /// <inheritdoc/>
    public KnownLayer Layer => KnownLayer.Background;

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var pieces = Lines;
        if (pieces == null || !textView.VisualLinesValid) return;

        foreach (var line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= pieces.Count) continue;

            IBrush? brush = pieces[index].Type switch
            {
                ChangeType.Inserted => _insertedBg,
                ChangeType.Deleted => _deletedBg,
                ChangeType.Imaginary => _imaginaryBg,
                ChangeType.Modified => Role == DiffPaneRole.New ? _insertedBg : _deletedBg,
                _ => null,
            };
            if (brush == null) continue;

            double top = line.VisualTop - textView.ScrollOffset.Y;
            drawingContext.FillRectangle(brush, new Rect(0, top, textView.Bounds.Width + textView.ScrollOffset.X, line.Height));
        }
    }

    #endregion
}

/// <summary>
/// Paints the differing words of a modified line more strongly than the row tint
/// <see cref="DiffLineBackgroundRenderer"/> gives the whole line. Ported from WPF, which tints
/// the rows here as well; the row tint moved to the background renderer so empty filler rows
/// get it too.
/// </summary>
public sealed class DiffLineColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly IBrush _insertedSubBg = MakeBrush(DiffColors.Inserted, 100);
    private static readonly IBrush _deletedSubBg = MakeBrush(DiffColors.Deleted, 100);

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the diff pieces, one per document line.</summary>
    public IReadOnlyList<DiffPiece>? Lines { get; set; }

    /// <summary>Gets or sets which side this pane shows.</summary>
    public DiffPaneRole Role { get; set; } = DiffPaneRole.Unified;

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override void ColorizeLine(DocumentLine line)
    {
        var pieces = Lines;
        if (pieces == null) return;
        int index = line.LineNumber - 1;
        if (index < 0 || index >= pieces.Count) return;

        var piece = pieces[index];
        if (piece.Type != ChangeType.Modified || piece.SubPieces == null || piece.SubPieces.Count == 0)
            return;

        // The sub-pieces concatenate back into the line, so walking them locates each changed word.
        IBrush subBrush = Role == DiffPaneRole.New ? _insertedSubBg : _deletedSubBg;
        int offset = line.Offset;
        foreach (var sub in piece.SubPieces)
        {
            int length = sub.Text?.Length ?? 0;
            if (length > 0 && sub.Type != ChangeType.Unchanged)
            {
                int start = Math.Min(offset, line.EndOffset);
                int end = Math.Min(offset + length, line.EndOffset);
                if (end > start)
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(subBrush));
            }
            offset += length;
        }
    }

    #endregion

    #region Private Methods

    internal static IBrush MakeBrush(Color color, byte alpha) => new ImmutableSolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

    #endregion
}

/// <summary>The unified pane's gutter: "+" on inserted lines, "-" on deleted ones.</summary>
public sealed class DiffPrefixMargin : AbstractMargin
{
    #region Private Fields

    private const double _rightPadding = 6;
    private static readonly Typeface _typeface = new(new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"));
    private static readonly IBrush _insertedBrush = new ImmutableSolidColorBrush(DiffColors.Inserted);
    private static readonly IBrush _deletedBrush = new ImmutableSolidColorBrush(DiffColors.Deleted);

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the unified diff pieces, one per document line.</summary>
    public IReadOnlyList<DiffPiece>? Lines { get; set; }

    /// <summary>Gets or sets the font size, matching the pane's.</summary>
    public double FontSize { get; set; } = 14;

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(Format("+", _insertedBrush).Width + _rightPadding + 4, 0);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        var lines = Lines;
        if (textView == null || lines == null || !textView.VisualLinesValid) return;

        foreach (var line in textView.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= lines.Count) continue;

            var (prefix, brush) = lines[index].Type switch
            {
                ChangeType.Inserted => ("+", _insertedBrush),
                ChangeType.Deleted => ("-", _deletedBrush),
                _ => ((string?)null, _insertedBrush),
            };
            if (prefix == null) continue;

            var formatted = Format(prefix, brush);
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

    #endregion

    #region Private Methods

    private void TextView_Changed(object? sender, EventArgs e) => InvalidateVisual();

    private FormattedText Format(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, brush);

    #endregion
}

/// <summary>
/// The whole-document change map beside a compare pane: a red mark for each run of deleted
/// lines and a green one for each run of inserted lines, at their proportional positions, so
/// changes far off screen can be seen and reached.
/// </summary>
public sealed class DiffChangeIndicatorStrip : Control
{
    #region Private Fields

    // A mark is never shorter than this, so a one-line change stays visible beside a big one.
    private const double MinMarkHeight = 3;
    private static readonly IBrush _deletedBrush = new ImmutableSolidColorBrush(DiffColors.Deleted);
    private static readonly IBrush _insertedBrush = new ImmutableSolidColorBrush(DiffColors.Inserted);

    private IReadOnlyList<(int Start, int Count)> _redRuns = [];
    private IReadOnlyList<(int Start, int Count)> _greenRuns = [];
    private int _totalLines = 1;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="DiffChangeIndicatorStrip"/> class.
    /// </summary>
    public DiffChangeIndicatorStrip()
    {
        IsHitTestVisible = false;
    }

    #endregion

    #region Public Methods

    /// <summary>Sets the runs to mark, as (start line, line count), over a document of <paramref name="totalLines"/> lines.</summary>
    public void Update(IReadOnlyList<(int Start, int Count)> redRuns, IReadOnlyList<(int Start, int Count)> greenRuns, int totalLines)
    {
        _redRuns = redRuns;
        _greenRuns = greenRuns;
        _totalLines = Math.Max(1, totalLines);
        InvalidateVisual();
    }

    #endregion

    #region Public Methods - Rendering

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        double height = Bounds.Height;
        double width = Bounds.Width;
        if (height <= 0 || width <= 0) return;

        foreach (var run in _redRuns) DrawRun(context, run, _deletedBrush, width, height);
        foreach (var run in _greenRuns) DrawRun(context, run, _insertedBrush, width, height);
    }

    #endregion

    #region Private Methods

    private void DrawRun(DrawingContext context, (int Start, int Count) run, IBrush brush, double width, double height)
    {
        double y1 = (double)run.Start / _totalLines * height;
        double y2 = (double)(run.Start + run.Count) / _totalLines * height;
        if (y2 - y1 < MinMarkHeight)
        {
            double mid = (y1 + y2) / 2;
            y1 = mid - MinMarkHeight / 2;
            y2 = mid + MinMarkHeight / 2;
        }
        y1 = Math.Clamp(y1, 0, Math.Max(0, height - MinMarkHeight));
        y2 = Math.Clamp(y2, y1 + MinMarkHeight, height);
        context.FillRectangle(brush, new Rect(1, y1, Math.Max(0, width - 2), y2 - y1));
    }

    #endregion
}

/// <summary>
/// The draggable translucent thumb over a change map, showing which part of the document is
/// on screen. Dragging it (or clicking elsewhere in the track) asks the host to scroll through
/// <see cref="ScrollRequested"/>; the host reports the real scroll state back through
/// <see cref="UpdateViewport"/>.
/// </summary>
public sealed class DiffViewportThumb : Control
{
    #region Private Fields

    private const double MinThumbHeight = 6;
    private const double CornerRadius = 8;
    private static readonly IBrush _thumbBrush = new ImmutableSolidColorBrush(Color.FromArgb(51, 128, 128, 128));
    private static readonly IBrush _thumbBrushDragging = new ImmutableSolidColorBrush(Color.FromArgb(77, 128, 128, 128));
    private static readonly IPen _thumbBorderPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(51, 90, 90, 90)), 1);
    private static readonly IPen _thumbBorderPenDragging = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(77, 90, 90, 90)), 1);

    private double _dragGrabOffset;
    private bool _isDragging;
    private double _dragRenderTop;

    #endregion

    #region Public Properties

    /// <summary>Gets the top of the visible part of the document, as a fraction of the whole.</summary>
    public double ViewportStart { get; private set; }

    /// <summary>Gets the visible part's height as a fraction of the whole document.</summary>
    public double ViewportFraction { get; private set; } = 1;

    #endregion

    #region Public Events

    /// <summary>Occurs when a drag asks for the document to scroll so the given fraction is at the top.</summary>
    public event Action<double>? ScrollRequested;

    #endregion

    #region Public Methods

    /// <summary>Updates the thumb from the pane's actual scroll state.</summary>
    public void UpdateViewport(double start, double fraction)
    {
        ViewportFraction = Math.Clamp(fraction, 0, 1);
        ViewportStart = Math.Clamp(start, 0, 1 - ViewportFraction);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        double height = Bounds.Height;
        double width = Bounds.Width;
        // A transparent fill so the whole track takes pointer input.
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, width, height));
        if (height <= 0 || width <= 0) return;

        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * height);
        double top = _isDragging
            ? Math.Clamp(_dragRenderTop, 0, Math.Max(0, height - thumbHeight))
            : Math.Clamp(ViewportStart * height, 0, Math.Max(0, height - thumbHeight));
        context.DrawRectangle(_isDragging ? _thumbBrushDragging : _thumbBrush, _isDragging ? _thumbBorderPenDragging : _thumbBorderPen,
            new Rect(0, top, width, thumbHeight), CornerRadius, CornerRadius);
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Height <= 0) return;

        double clickY = e.GetPosition(this).Y;
        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * Bounds.Height);
        double thumbTop = Math.Clamp(ViewportStart * Bounds.Height, 0, Math.Max(0, Bounds.Height - thumbHeight));

        // Grabbing the thumb keeps the grab point; clicking elsewhere centers it under the pointer.
        _dragGrabOffset = clickY >= thumbTop && clickY <= thumbTop + thumbHeight ? clickY - thumbTop : thumbHeight / 2;
        _isDragging = true;
        e.Pointer.Capture(this);
        ApplyDrag(clickY - _dragGrabOffset);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging) ApplyDrag(e.GetPosition(this).Y - _dragGrabOffset);
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;
        ApplyDrag(e.GetPosition(this).Y - _dragGrabOffset);
        _isDragging = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    #endregion

    #region Private Methods

    private void ApplyDrag(double rawThumbTop)
    {
        if (Bounds.Height <= 0) return;
        double thumbHeight = Math.Max(MinThumbHeight, ViewportFraction * Bounds.Height);
        _dragRenderTop = Math.Clamp(rawThumbTop, 0, Math.Max(0, Bounds.Height - thumbHeight));
        InvalidateVisual();
        ScrollRequested?.Invoke(Math.Clamp(_dragRenderTop / Bounds.Height, 0, 1));
    }

    #endregion
}
