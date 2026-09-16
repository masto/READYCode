// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The hex editor: <see cref="HexGridCanvas"/> under a fixed column header, with the one
/// shared edit box that appears over whichever cell is being edited. Owns the chrome and the
/// edit box's keys; the grid owns the bytes, selection, and undo. The window talks to this and
/// never to the grid directly, as in WPF.
/// </summary>
public partial class HexEditorControl : UserControl
{
    #region Private Fields

    private bool _typing;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="HexEditorControl"/> class.
    /// </summary>
    public HexEditorControl()
    {
        InitializeComponent();

        for (int col = 0; col < 16; col++)
            CellsHeaderPanel.Children.Add(new TextBlock { Text = col.ToString("X2"), Classes = { "hexHeader" }, TextAlignment = global::Avalonia.Media.TextAlignment.Center });

        HexGrid.HostScrollViewer = RowsScrollViewer;
        HexGrid.ByteEdited += (_, _) => ByteEdited?.Invoke(this, EventArgs.Empty);
        HexGrid.EditRequested += HexGrid_EditRequested;
        HexGrid.EditCommitRequested += (_, _) => HexGrid.CommitEdit(EditBox.Text ?? "", advanceDelta: null);
        HexGrid.TypeRequested += (_, digit) => EditBox.Text = digit.ToString();

        // The grid draws only the rows the scroll viewer says are on screen, so it must redraw
        // whenever that changes - from the wheel, the bar, or its own scroll-into-view.
        RowsScrollViewer.ScrollChanged += (_, _) => HexGrid.InvalidateVisual();

        EditBox.AddHandler(KeyDownEvent, EditBox_KeyDown, RoutingStrategies.Tunnel);
        EditBox.AddHandler(TextInputEvent, EditBox_TextInput, RoutingStrategies.Tunnel);
        EditBox.TextChanged += EditBox_TextChanged;
        EditBox.LostFocus += EditBox_LostFocus;

        RescaleHeaderAndEditBox();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the font size the grid is drawn at (the editor's, so the two match); the
    /// header and edit box scale with it.
    /// </summary>
    public double HexFontSize
    {
        get => HexGrid.FontSize;
        set
        {
            if (HexGrid.FontSize == value) return;
            HexGrid.FontSize = value;
            RescaleHeaderAndEditBox();
        }
    }

    /// <summary>Gets the offset of the active cell - the hex analog of the caret offset.</summary>
    public int SelectedOffset => HexGrid.SelectedOffset;

    /// <summary>Gets whether one or more bytes are selected.</summary>
    public bool HasSelection => HexGrid.HasSelection;

    /// <summary>Gets whether there is an edit to undo.</summary>
    public bool CanUndo => HexGrid.CanUndo;

    /// <summary>Gets whether there is an edit to redo.</summary>
    public bool CanRedo => HexGrid.CanRedo;

    /// <summary>Gets the grid, for tests.</summary>
    internal HexGridCanvas Grid => HexGrid;

    /// <summary>Gets the shared edit box, for tests.</summary>
    internal TextBox EditTextBox => EditBox;

    #endregion

    #region Public Events

    /// <summary>Occurs when the user changes a byte.</summary>
    public event EventHandler? ByteEdited;

    #endregion

    #region Public Methods

    /// <summary>Shows <paramref name="bytes"/> with the given byte active and <paramref name="undoStack"/> as the history.</summary>
    public void LoadBytes(byte[] bytes, int selectedOffset, HexUndoStack undoStack)
    {
        EditBox.IsVisible = false;
        HexGrid.LoadBytes(bytes, selectedOffset, undoStack);
        Dispatcher.UIThread.Post(() => HexGrid.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Puts keyboard focus on the grid.</summary>
    public void FocusGrid() => HexGrid.Focus();

    /// <summary>Copies the selected bytes to the clipboard as space-separated hex.</summary>
    public async Task CopyAsync()
    {
        string text = HexGrid.GetSelectionHexText();
        if (text.Length == 0) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>Copies the selected bytes, then zero-fills them - the fixed-size buffer can't shrink.</summary>
    public async Task CutAsync()
    {
        if (!HexGrid.HasSelection) return;
        await CopyAsync();
        HexGrid.Delete();
    }

    /// <summary>Overwrites bytes from the selection with the hex digits on the clipboard, as many as fit.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await clipboard.TryGetTextAsync() is { } text)
            HexGrid.PasteHexText(text);
    }

    /// <summary>Zero-fills the selected bytes.</summary>
    public void Delete() => HexGrid.Delete();

    /// <summary>Selects every byte.</summary>
    public void SelectAll() => HexGrid.SelectAll();

    /// <summary>Reverts the most recent edit, if any.</summary>
    public void Undo() { CancelEditBox(); HexGrid.Undo(); }

    /// <summary>Reapplies the most recently undone edit, if any.</summary>
    public void Redo() { CancelEditBox(); HexGrid.Redo(); }

    #endregion

    #region Private Methods

    // The header and edit box were tuned to line up with the grid at 13pt; scale them by the
    // grid's own factor rather than recomputing, so they can't drift apart.
    private void RescaleHeaderAndEditBox()
    {
        double scale = HexGrid.Scale;

        HeaderPanel.Margin = new Thickness(6 * scale, 4 * scale, 6 * scale, 4 * scale);
        OffsetHeaderText.Width = HexGrid.OffsetLabelWidth;
        OffsetHeaderText.FontSize = 11 * scale;
        HeaderDivider1.Margin = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale);
        HeaderDivider2.Margin = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale);

        CellsHeaderPanel.Margin = new Thickness(8 * scale, 0, 8 * scale, 0);
        foreach (var cell in CellsHeaderPanel.Children.OfType<TextBlock>())
        {
            cell.Width = HexGrid.CellContentWidth;
            cell.Margin = new Thickness(1 * scale, 0, 1 * scale, 0);
            cell.FontSize = 11 * scale;
        }

        AsciiHeaderText.Margin = new Thickness(8 * scale, 0, 0, 0);
        AsciiHeaderText.FontSize = 11 * scale;
        EditBox.FontSize = HexGrid.FontSize;
    }

    private void HexGrid_EditRequested(object? sender, int offset)
    {
        Rect bounds = HexGrid.GetCellBounds(offset);
        EditBox.Margin = new Thickness(bounds.X, bounds.Y + HexGrid.Scale, 0, 0);
        EditBox.Width = bounds.Width;
        EditBox.Height = bounds.Height - 2 * HexGrid.Scale;
        EditBox.Text = HexGrid.GetHexText(offset);
        EditBox.IsVisible = true;

        // Opened by double-click/Enter the box holds the current byte, selected so typing
        // replaces it; opened by typing a digit (TypeRequested has run by now) it holds that
        // digit, with the caret after it for the second.
        Dispatcher.UIThread.Post(() =>
        {
            EditBox.Focus();
            if (EditBox.Text?.Length == 2) EditBox.SelectAll();
            else EditBox.CaretIndex = EditBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    private void EditBox_TextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && !e.Text.All(Uri.IsHexDigit)) { e.Handled = true; return; }
        _typing = true;
    }

    // Once both digits have been typed the byte is complete: commit it and move to the next,
    // so a run of bytes can be typed straight through. Only for typed input - the box opens
    // with the current two digits already in it, and that must not count.
    private void EditBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_typing) return;
        _typing = false;
        if (EditBox.IsVisible && EditBox.Text?.Length == 2)
            Dispatcher.UIThread.Post(() =>
            {
                if (!EditBox.IsVisible) return;
                CommitEditBox(advanceDelta: 1);
                HexGrid.Focus();
            });
    }

    private void EditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                CommitEditBox(advanceDelta: null);
                HexGrid.Focus();
                break;

            case Key.Tab:
                // Tab moves on to the next byte (Shift+Tab the previous) so a run can be typed
                // straight through; typing the second digit does the same automatically below.
                e.Handled = true;
                CommitEditBox(advanceDelta: e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                HexGrid.Focus();
                break;

            case Key.Escape:
                e.Handled = true;
                HexGrid.CancelEdit();
                EditBox.IsVisible = false;
                HexGrid.Focus();
                break;
        }
    }

    // Commits automatically when focus leaves for any reason the keys above didn't cover -
    // clicking another cell, clicking elsewhere, switching tabs.
    private void EditBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (!EditBox.IsVisible) return;
        CommitEditBox(advanceDelta: null);
    }

    private void CommitEditBox(int? advanceDelta)
    {
        HexGrid.CommitEdit(EditBox.Text ?? "", advanceDelta);
        EditBox.IsVisible = false;
    }

    private void CancelEditBox()
    {
        if (!EditBox.IsVisible) return;
        HexGrid.CancelEdit();
        EditBox.IsVisible = false;
    }

    #endregion
}
