// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// A byte array drawn as a hex grid (offset / 16 hex bytes / ASCII) with selection, keyboard
/// navigation, in-place editing, and undo. Draws everything itself in <see cref="Render"/> and
/// hit-tests by arithmetic, so a large file costs one control, not one per byte - a port of the
/// WPF app's canvas of the same name, with the same layout so the two look alike. Hosted by
/// <see cref="Views.HexEditorControl"/>, which supplies the scroll viewer and the shared edit
/// box; this control asks for the box through <see cref="EditRequested"/> and commits what was
/// typed through <see cref="CommitEdit"/>.
/// </summary>
public sealed class HexGridCanvas : Control
{
    #region Private Fields

    private const int BytesPerRow = 16;

    // Every dimension below is the WPF grid's 13pt tuning scaled by the font size, so the header
    // row the host draws with the same numbers lines up with the cells at any size.
    private const double _baseFontSize = 13;
    private static readonly FontFamily _font = new("Menlo,Consolas,DejaVu Sans Mono,monospace");

    private double _fontSize = _baseFontSize;
    private byte[]? _bytes;
    private int _byteCount;
    private HexUndoStack? _undoStack;

    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private int? _selectionAnchor;
    private int _selectedOffset;
    private int? _editingOffset;
    private bool _isDragSelecting;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HexGridCanvas"/> class.
    /// </summary>
    public HexGridCanvas()
    {
        Focusable = true;
    }

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the font size the hex and ASCII text is drawn at; every dimension scales with it.</summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize == value) return;
            _fontSize = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Gets the ratio of <see cref="FontSize"/> to the 13pt size the layout was tuned at.</summary>
    public double Scale => _fontSize / _baseFontSize;

    /// <summary>Gets the height of a byte row.</summary>
    public double RowHeight => 22 * Scale;

    /// <summary>Gets the X position of the offset column's label.</summary>
    public double OffsetLabelX => 6 * Scale;

    /// <summary>Gets the width reserved for the offset column.</summary>
    public double OffsetLabelWidth => 64 * Scale;

    /// <summary>Gets the X position of the divider between the offset and hex-byte columns.</summary>
    public double Divider1X => OffsetLabelX + OffsetLabelWidth + 6 * Scale;

    /// <summary>Gets the X position where the hex-byte cells begin.</summary>
    public double CellsAreaX => Divider1X + 1 * Scale + 6 * Scale + 8 * Scale;

    /// <summary>Gets the total width (content plus margin) of one hex-byte cell.</summary>
    public double CellWidth => 24 * Scale;

    /// <summary>Gets the width of a hex-byte cell's text content, excluding its margin.</summary>
    public double CellContentWidth => 22 * Scale;

    /// <summary>Gets the total width of all 16 hex-byte cells in a row.</summary>
    public double CellsAreaWidth => BytesPerRow * CellWidth;

    /// <summary>Gets the X position of the divider between the hex-byte and ASCII columns.</summary>
    public double Divider2X => CellsAreaX + CellsAreaWidth + 8 * Scale + 6 * Scale;

    /// <summary>Gets the X position where the ASCII column begins.</summary>
    public double AsciiAreaX => Divider2X + 1 * Scale + 8 * Scale;

    /// <summary>Gets the width of one ASCII column character.</summary>
    public double AsciiCharWidth => 10 * Scale;

    /// <summary>Gets the byte offset of the active cell.</summary>
    public int SelectedOffset => _selectedOffset;

    /// <summary>Gets the first selected offset, or -1 with no selection.</summary>
    public int SelectionStart => HasSelection ? _selectionStart : -1;

    /// <summary>Gets the last selected offset, or -1 with no selection.</summary>
    public int SelectionEnd => HasSelection ? _selectionEnd : -1;

    /// <summary>Gets whether one or more bytes are selected.</summary>
    public bool HasSelection => _bytes != null && _selectionStart >= 0 && _selectionEnd >= _selectionStart;

    /// <summary>Gets whether a cell is being edited.</summary>
    public bool IsEditing => _editingOffset != null;

    /// <summary>Gets whether there is an edit to undo.</summary>
    public bool CanUndo => _undoStack?.CanUndo ?? false;

    /// <summary>Gets whether there is an edit to redo.</summary>
    public bool CanRedo => _undoStack?.CanRedo ?? false;

    /// <summary>
    /// Gets or sets the scroll viewer hosting this control, for knowing which rows are on
    /// screen and for scrolling a newly selected byte into view.
    /// </summary>
    public ScrollViewer? HostScrollViewer { get; set; }

    #endregion

    #region Public Events

    /// <summary>Occurs when the user changes a byte (not when <see cref="LoadBytes"/> repopulates the grid).</summary>
    public event EventHandler? ByteEdited;

    /// <summary>Occurs when the user double-clicks or presses Enter on a cell: the host shows its edit box there. The argument is the byte offset.</summary>
    public event EventHandler<int>? EditRequested;

    /// <summary>Occurs when a different cell is about to take over the edit box while one is still being edited, so the host commits the pending text first.</summary>
    public event EventHandler? EditCommitRequested;

    /// <summary>Occurs when a hex-digit key is pressed on the active cell: the host opens the edit box with that digit typed.</summary>
    public event EventHandler<char>? TypeRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows <paramref name="bytes"/>, which edits write straight back into, with the given
    /// byte active and <paramref name="undoStack"/> as the history.
    /// </summary>
    public void LoadBytes(byte[] bytes, int selectedOffset, HexUndoStack undoStack)
    {
        _bytes = bytes;
        _byteCount = bytes.Length;
        _undoStack = undoStack;
        _selectionStart = -1;
        _selectionEnd = -1;
        _selectionAnchor = null;
        _editingOffset = null;
        _selectedOffset = Math.Clamp(selectedOffset, 0, Math.Max(0, bytes.Length - 1));

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Gets the bounds of a byte's hex cell, in this control's coordinates.</summary>
    public Rect GetCellBounds(int offset)
    {
        int row = offset / BytesPerRow;
        int col = offset % BytesPerRow;
        return new Rect(CellsAreaX + col * CellWidth, row * RowHeight, CellContentWidth, RowHeight);
    }

    /// <summary>Gets a byte's value as two hex digits.</summary>
    public string GetHexText(int offset) => _bytes![offset].ToString("X2");

    /// <summary>Gets the selected bytes as space-separated hex, or "" with no selection.</summary>
    public string GetSelectionHexText()
    {
        if (!HasSelection) return "";
        var sb = new StringBuilder();
        for (int offset = _selectionStart; offset <= _selectionEnd; offset++)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(_bytes![offset].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes the edit box's text back to the byte being edited if it is a complete valid byte
    /// (a half-typed one is left unchanged) and leaves edit mode, moving on by
    /// <paramref name="advanceDelta"/> bytes if given.
    /// </summary>
    public void CommitEdit(string typedText, int? advanceDelta)
    {
        if (_editingOffset is not { } offset || _bytes == null) return;

        if (typedText.Length == 2 && byte.TryParse(typedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
            SetByte(offset, value);

        _editingOffset = null;
        InvalidateVisual();

        if (advanceDelta is { } delta)
        {
            int newOffset = offset + delta;
            if (newOffset >= 0 && newOffset < _byteCount)
                MoveSelectionTo(newOffset, extend: false);
        }
    }

    /// <summary>Leaves edit mode without writing anything back.</summary>
    public void CancelEdit()
    {
        if (_editingOffset == null) return;
        _editingOffset = null;
        InvalidateVisual();
    }

    /// <summary>Selects every byte.</summary>
    public void SelectAll()
    {
        if (_byteCount == 0) return;
        _selectionAnchor = 0;
        SetSelectionRange(0, _byteCount - 1);
    }

    /// <summary>Selects <paramref name="length"/> bytes from <paramref name="offset"/> and scrolls them into view.</summary>
    public void Select(int offset, int length)
    {
        if (_byteCount == 0) return;
        offset = Math.Clamp(offset, 0, _byteCount - 1);
        int end = Math.Clamp(offset + Math.Max(1, length) - 1, offset, _byteCount - 1);
        _selectionAnchor = offset;
        SetSelectionRange(offset, end);
        SetSelectedOffset(end);
        ScrollIntoViewIfNeeded(offset);
    }

    /// <summary>Zero-fills the selection - the buffer is fixed-size, so this is the nearest thing to deleting.</summary>
    public void Delete()
    {
        if (!HasSelection) return;
        ZeroSelection();
    }

    /// <summary>
    /// Overwrites bytes from the selection's start (or the active cell) with the hex digits in
    /// <paramref name="text"/>, as many as fit - never inserts. Non-hex characters are ignored.
    /// </summary>
    public void PasteHexText(string text)
    {
        if (_bytes == null) return;

        string hexDigits = new(text.Where(Uri.IsHexDigit).ToArray());
        if (hexDigits.Length < 2) return;

        int startOffset = HasSelection ? _selectionStart : SelectedOffset;
        int available = _byteCount - startOffset;
        if (available <= 0) return;

        int byteCount = Math.Min(hexDigits.Length / 2, available);
        var oldValues = new byte[byteCount];
        Array.Copy(_bytes, startOffset, oldValues, 0, byteCount);
        var newValues = new byte[byteCount];

        bool changed = false;
        for (int i = 0; i < byteCount; i++)
        {
            byte value = byte.Parse(hexDigits.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            newValues[i] = value;
            if (_bytes[startOffset + i] == value) continue;
            _bytes[startOffset + i] = value;
            changed = true;
        }

        int endOffset = startOffset + byteCount - 1;
        _selectionAnchor = startOffset;
        SetSelectionRange(startOffset, endOffset);
        SetSelectedOffset(endOffset);
        ScrollIntoViewIfNeeded(endOffset);

        if (changed)
        {
            _undoStack?.Push(startOffset, oldValues, newValues);
            ByteEdited?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    /// <summary>Reverts the most recent edit, if any, selecting the bytes it touched.</summary>
    public void Undo()
    {
        if (_bytes == null || _undoStack?.Undo(_bytes) is not { } range) return;
        SelectAndReveal(range.Offset, range.Length);
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reapplies the most recently undone edit, if any.</summary>
    public void Redo()
    {
        if (_bytes == null || _undoStack?.Redo(_bytes) is not { } range) return;
        SelectAndReveal(range.Offset, range.Length);
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the edit box on the active cell (Enter).</summary>
    public void BeginEditSelected() => BeginEdit(SelectedOffset);

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? AsciiAreaX + BytesPerRow * AsciiCharWidth + 8 * Scale : availableSize.Width;
        double totalRows = _byteCount == 0 ? 1 : Math.Ceiling(_byteCount / (double)BytesPerRow);
        return new Size(Math.Max(width, AsciiAreaX + BytesPerRow * AsciiCharWidth + 8 * Scale), totalRows * RowHeight);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext dc)
    {
        // A transparent fill so pointer hit-testing works across the whole grid, not just on glyphs.
        dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_bytes == null || _byteCount == 0) return;

        double viewportTop = HostScrollViewer?.Offset.Y ?? 0;
        double viewportHeight = HostScrollViewer?.Viewport.Height ?? Bounds.Height;
        if (viewportHeight <= 0) viewportHeight = Bounds.Height;

        int totalRows = (int)Math.Ceiling(_byteCount / (double)BytesPerRow);
        int firstRow = Math.Max(0, (int)(viewportTop / RowHeight) - 1);
        int lastRow = Math.Min(totalRows - 1, (int)((viewportTop + viewportHeight) / RowHeight) + 1);
        if (firstRow > lastRow) return;

        var typeface = new Typeface(_font);
        var brushes = new Palette(
            ResolveBrush("ThemeSettingsDescFg"),
            // ThemeEditorFg, not ThemeFileFg: this is drawn on ThemeEditorBg like the editor's own
            // text, and ThemeFileFg only contrasts with the explorer's background.
            ResolveBrush("ThemeEditorFg"),
            ResolveBrush("ThemeHexSelectedFg"),
            ResolveBrush("ThemeSettingsAccent"),
            ResolveBrush("ThemeSettingsDivider"));
        var activePen = new Pen(brushes.Accent, 1);

        for (int row = firstRow; row <= lastRow; row++)
            DrawRow(dc, row, typeface, brushes, activePen);
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        int? offset = HitTestOffset(point.Position);

        if (point.Properties.IsRightButtonPressed)
        {
            // Right-clicking inside the selection keeps it, so the context menu acts on all of
            // it; outside, the click selects just that byte.
            if (offset is { } o && !(HasSelection && o >= _selectionStart && o <= _selectionEnd))
            {
                _selectionAnchor = o;
                SetSelectionRange(o, o);
                SetSelectedOffset(o);
            }
            Focus();
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || offset is not { } hit) return;

        if (e.ClickCount >= 2)
        {
            BeginEdit(hit);
            e.Handled = true;
            return;
        }

        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _selectionAnchor.HasValue;
        if (!extend) _selectionAnchor = hit;
        SetSelectionRange(_selectionAnchor!.Value, hit);
        SetSelectedOffset(hit);

        _isDragSelecting = true;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragSelecting || !_selectionAnchor.HasValue) return;

        if (HitTestOffset(e.GetPosition(this)) is not { } offset) return;
        SetSelectionRange(_selectionAnchor.Value, offset);
        SetSelectedOffset(offset);
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragSelecting) e.Pointer.Capture(null);
        _isDragSelecting = false;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editingOffset != null || _bytes == null || _byteCount == 0) return;

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        switch (e.Key)
        {
            case Key.A when ctrl:
                e.Handled = true;
                SelectAll();
                break;

            case Key.Enter:
                e.Handled = true;
                BeginEdit(SelectedOffset);
                break;

            case Key.Left or Key.Right or Key.Up or Key.Down:
                e.Handled = true;
                HandleArrowKey(e.Key, shift);
                break;

            case Key.Home or Key.End or Key.PageUp or Key.PageDown:
            {
                e.Handled = true;
                int target = e.Key switch
                {
                    Key.Home => ctrl ? 0 : RowStart(SelectedOffset),
                    Key.End => ctrl ? _byteCount - 1 : RowStart(SelectedOffset) + RowLength(SelectedOffset) - 1,
                    Key.PageUp => SelectedOffset - PageSizeInBytes(),
                    Key.PageDown => SelectedOffset + PageSizeInBytes(),
                    _ => SelectedOffset,
                };
                MoveSelectionTo(Math.Clamp(target, 0, _byteCount - 1), shift);
                break;
            }

            default:
                // A hex digit starts editing the active cell with that digit already typed, so
                // overwriting a run of bytes is just typing.
                if (!ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && HexDigitFor(e.Key, shift) is { } digit)
                {
                    e.Handled = true;
                    BeginEdit(SelectedOffset);
                    TypeRequested?.Invoke(this, digit);
                }
                break;
        }
    }

    #endregion

    #region Private Methods

    private readonly record struct Palette(IBrush Desc, IBrush Byte, IBrush SelectedByte, IBrush Accent, IBrush Divider);

    private void DrawRow(DrawingContext dc, int row, Typeface typeface, Palette brushes, IPen activePen)
    {
        int rowStart = row * BytesPerRow;
        double y = row * RowHeight;

        DrawText(dc, rowStart.ToString("X6"), OffsetLabelX, y, brushes.Desc, typeface);
        dc.FillRectangle(brushes.Divider, new Rect(Divider1X, y + 2, 1, RowHeight - 4));
        dc.FillRectangle(brushes.Divider, new Rect(Divider2X, y + 2, 1, RowHeight - 4));

        int rowLength = Math.Min(BytesPerRow, _byteCount - rowStart);
        for (int col = 0; col < rowLength; col++)
        {
            int offset = rowStart + col;
            if (offset == _editingOffset) continue; // the host's edit box covers this cell

            bool selected = offset >= _selectionStart && offset <= _selectionEnd;
            double cellX = CellsAreaX + col * CellWidth;
            double asciiX = AsciiAreaX + col * AsciiCharWidth;

            if (selected)
            {
                dc.FillRectangle(brushes.Accent, new Rect(cellX, y, CellContentWidth, RowHeight));
                dc.FillRectangle(brushes.Accent, new Rect(asciiX, y, AsciiCharWidth, RowHeight));
            }
            if (offset == SelectedOffset)
                dc.DrawRectangle(null, activePen, new Rect(cellX + 0.5, y + 0.5, CellContentWidth - 1, RowHeight - 1));

            byte b = _bytes![offset];
            DrawText(dc, b.ToString("X2"), cellX, y, selected ? brushes.SelectedByte : brushes.Byte, typeface, CellContentWidth);

            char c = b is >= 0x20 and <= 0x7E ? (char)b : '.';
            DrawText(dc, c.ToString(), asciiX, y, selected ? brushes.SelectedByte : brushes.Desc, typeface, AsciiCharWidth);
        }
    }

    // Centered within [x, x+width) when a width is given, else left-aligned; always vertically
    // centered in the row.
    private void DrawText(DrawingContext dc, string text, double x, double y, IBrush brush, Typeface typeface, double width = 0)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, brush);
        double drawX = width > 0 ? x + (width - formatted.Width) / 2 : x;
        double drawY = y + (RowHeight - formatted.Height) / 2;
        dc.DrawText(formatted, new Point(drawX, drawY));
    }

    private int? HitTestOffset(Point p)
    {
        if (_bytes == null) return null;
        if (p.X < CellsAreaX || p.X >= CellsAreaX + CellsAreaWidth) return null;

        int row = (int)(p.Y / RowHeight);
        int col = (int)((p.X - CellsAreaX) / CellWidth);
        if (row < 0 || col < 0 || col >= BytesPerRow) return null;

        int offset = row * BytesPerRow + col;
        return offset >= 0 && offset < _byteCount ? offset : null;
    }

    private static char? HexDigitFor(Key key, bool shift)
    {
        if (shift) return null;
        return key switch
        {
            >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            >= Key.A and <= Key.F => (char)('A' + (key - Key.A)),
            _ => null,
        };
    }

    private void BeginEdit(int offset)
    {
        if (_editingOffset is { } previous && previous != offset)
            EditCommitRequested?.Invoke(this, EventArgs.Empty);

        _selectionAnchor = offset;
        SetSelectionRange(offset, offset);
        SetSelectedOffset(offset);
        ScrollIntoViewIfNeeded(offset);

        _editingOffset = offset;
        InvalidateVisual();
        EditRequested?.Invoke(this, offset);
    }

    private void ZeroSelection()
    {
        int length = _selectionEnd - _selectionStart + 1;
        var oldValues = new byte[length];
        Array.Copy(_bytes!, _selectionStart, oldValues, 0, length);

        bool changed = false;
        for (int offset = _selectionStart; offset <= _selectionEnd; offset++)
        {
            if (_bytes![offset] == 0) continue;
            _bytes[offset] = 0;
            changed = true;
        }

        InvalidateVisual();
        if (changed)
        {
            _undoStack?.Push(_selectionStart, oldValues, new byte[length]);
            ByteEdited?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetByte(int offset, byte value)
    {
        if (_bytes![offset] == value) return;
        _undoStack?.Push(offset, [_bytes[offset]], [value]);
        _bytes[offset] = value;
        InvalidateVisual();
        ByteEdited?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelectedOffset(int offset)
    {
        _selectedOffset = offset;
        InvalidateVisual();
    }

    private void SetSelectionRange(int offsetA, int offsetB)
    {
        int newStart = Math.Min(offsetA, offsetB);
        int newEnd = Math.Max(offsetA, offsetB);
        if (newStart == _selectionStart && newEnd == _selectionEnd) return;

        _selectionStart = newStart;
        _selectionEnd = newEnd;
        InvalidateVisual();
    }

    private void MoveSelectionTo(int targetOffset, bool extend)
    {
        if (extend)
        {
            _selectionAnchor ??= SelectedOffset;
            SetSelectionRange(_selectionAnchor.Value, targetOffset);
        }
        else
        {
            _selectionAnchor = targetOffset;
            SetSelectionRange(targetOffset, targetOffset);
        }

        SetSelectedOffset(targetOffset);
        ScrollIntoViewIfNeeded(targetOffset);
    }

    private void SelectAndReveal(int offset, int length)
    {
        int endOffset = offset + length - 1;
        _selectionAnchor = offset;
        SetSelectionRange(offset, endOffset);
        SetSelectedOffset(endOffset);
        ScrollIntoViewIfNeeded(offset);
    }

    // Bounds-checked rather than clamped, so Up from the top row or Down into a short last row
    // does nothing instead of jumping to an unrelated column.
    private void HandleArrowKey(Key key, bool extend)
    {
        int delta = key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -BytesPerRow,
            Key.Down => BytesPerRow,
            _ => 0,
        };

        int newOffset = SelectedOffset + delta;
        if (newOffset < 0 || newOffset >= _byteCount) return;
        MoveSelectionTo(newOffset, extend);
    }

    private void ScrollIntoViewIfNeeded(int offset)
    {
        if (HostScrollViewer == null) return;

        int row = offset / BytesPerRow;
        double topRow = HostScrollViewer.Offset.Y / RowHeight;
        double visibleRows = Math.Max(1, HostScrollViewer.Viewport.Height / RowHeight);

        if (row < topRow)
            HostScrollViewer.Offset = HostScrollViewer.Offset.WithY(row * RowHeight);
        else if (row > topRow + visibleRows - 1)
            HostScrollViewer.Offset = HostScrollViewer.Offset.WithY((row - visibleRows + 1) * RowHeight);
    }

    private int RowStart(int offset) => offset - (offset % BytesPerRow);

    private int RowLength(int offset) => Math.Min(BytesPerRow, _byteCount - RowStart(offset));

    private int PageSizeInBytes()
    {
        double viewportHeight = HostScrollViewer?.Viewport.Height ?? Bounds.Height;
        return Math.Max(1, (int)(viewportHeight / RowHeight)) * BytesPerRow;
    }

    private IBrush ResolveBrush(string resourceKey) =>
        this.TryFindResource(resourceKey, out object? value) && value is IBrush brush ? brush : Brushes.Gray;

    #endregion
}
