// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using DiffPlex.DiffBuilder.Model;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Diff;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The read-only File Compare view for a <see cref="FileCompareResult"/>: a GitHub-style
/// split (side-by-side) or unified diff with color-coded lines, word-level highlighting, a
/// change map with a draggable viewport thumb, previous/next change, an Ignore Whitespace
/// toggle, and long unchanged runs folded away. Shown in the editor's place for a compare tab,
/// like the hex editor. Ported from WPF's control of the same name.
/// </summary>
public partial class FileCompareControl : UserControl
{
    #region Private Fields

    private readonly DiffLineColorizer _leftColorizer = new() { Role = DiffPaneRole.Old };
    private readonly DiffLineColorizer _rightColorizer = new() { Role = DiffPaneRole.New };
    private readonly DiffLineColorizer _unifiedColorizer = new() { Role = DiffPaneRole.Unified };
    private readonly DiffLineBackgroundRenderer _leftRowTint = new() { Role = DiffPaneRole.Old };
    private readonly DiffLineBackgroundRenderer _rightRowTint = new() { Role = DiffPaneRole.New };
    private readonly DiffLineBackgroundRenderer _unifiedRowTint = new() { Role = DiffPaneRole.Unified };
    private readonly DiffPrefixMargin _unifiedPrefixMargin = new();

    // Each pane needs its own PETSCII generator: left, right, and unified can show
    // differently-styled files.
    private readonly PetsciiGlyphGenerator _leftPetscii = new();
    private readonly PetsciiGlyphGenerator _rightPetscii = new();
    private readonly PetsciiGlyphGenerator _unifiedPetscii = new();

    private FoldingManager? _leftFoldingManager;
    private FoldingManager? _rightFoldingManager;
    private FoldingManager? _unifiedFoldingManager;

    private FileCompareResult? _result;
    private bool _isUnified;
    private bool _ignoreWhitespace;
    private int _currentHunkIndex = -1;
    private bool _syncingScroll;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FileCompareControl"/> class.
    /// </summary>
    public FileCompareControl()
    {
        InitializeComponent();

        foreach (var (editor, colorizer, rowTint, petscii) in new[]
        {
            (LeftEditor, _leftColorizer, _leftRowTint, _leftPetscii),
            (RightEditor, _rightColorizer, _rightRowTint, _rightPetscii),
            (UnifiedEditor, _unifiedColorizer, _unifiedRowTint, _unifiedPetscii),
        })
        {
            editor.TextArea.TextView.LineTransformers.Add(colorizer);
            editor.TextArea.TextView.BackgroundRenderers.Add(rowTint);
            // As the main editor: the built-in control-character boxes would otherwise beat the
            // PETSCII generator to the C0/C1 codes.
            editor.Options.ShowBoxForControlCharacters = false;
            editor.TextArea.TextView.ElementGenerators.Add(petscii);
        }
        UnifiedEditor.TextArea.LeftMargins.Insert(0, _unifiedPrefixMargin);

        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(LeftEditor, RightEditor);
        RightEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => SyncScroll(RightEditor, LeftEditor);

        LeftEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateSplitViewport();
        LeftEditor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateSplitViewport();
        UnifiedEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateUnifiedViewport();
        UnifiedEditor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateUnifiedViewport();

        // The split thumb scrolls the left pane; the sync above carries the right along.
        SplitViewportThumb.ScrollRequested += fraction => ScrollToFraction(LeftEditor, fraction);
        UnifiedViewportThumb.ScrollRequested += fraction => ScrollToFraction(UnifiedEditor, fraction);
    }

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the font size of all three panes, the editor's.</summary>
    public double EditorFontSize
    {
        get => LeftEditor.FontSize;
        set
        {
            LeftEditor.FontSize = value;
            RightEditor.FontSize = value;
            UnifiedEditor.FontSize = value;
            _unifiedPrefixMargin.FontSize = value;
        }
    }

    /// <summary>Gets the comparison being shown.</summary>
    public FileCompareResult? Result => _result;

    /// <summary>Gets whether the unified view is showing rather than the split one.</summary>
    public bool IsUnified => _isUnified;

    /// <summary>Gets the number of changes in the current view.</summary>
    public int HunkCount => _result == null ? 0 : (_isUnified ? _result.UnifiedHunkStartLines : _result.SplitHunkStartRows).Count;

    #endregion

    #region Public Events

    /// <summary>Occurs when the user changes Split/Unified or Ignore Whitespace, so the tab can remember them.</summary>
    public event Action<bool, bool>? ViewStateChanged;

    #endregion

    #region Public Methods

    /// <summary>Shows a comparison, in the given view mode and whitespace setting.</summary>
    public void LoadResult(FileCompareResult result, bool isUnified, bool ignoreWhitespace)
    {
        _result = result;
        _isUnified = isUnified;
        _ignoreWhitespace = ignoreWhitespace;
        _currentHunkIndex = -1;

        SplitViewToggle.IsChecked = !isUnified;
        UnifiedViewToggle.IsChecked = isUnified;
        IgnoreWhitespaceToggle.IsChecked = ignoreWhitespace;

        Render();
    }

    /// <summary>Scrolls to the next (+1) or previous (-1) change, wrapping around.</summary>
    public void NavigateHunk(int direction)
    {
        if (_result == null) return;

        var hunks = _isUnified ? _result.UnifiedHunkStartLines : _result.SplitHunkStartRows;
        if (hunks.Count == 0) return;

        _currentHunkIndex = ((_currentHunkIndex + direction) % hunks.Count + hunks.Count) % hunks.Count;
        int row = hunks[_currentHunkIndex];

        if (_isUnified)
        {
            UnifiedEditor.ScrollToLine(row + 1);
        }
        else
        {
            LeftEditor.ScrollToLine(row + 1);
            RightEditor.ScrollToLine(row + 1);
        }
    }

    /// <summary>Unfolds every collapsed run of unchanged lines.</summary>
    public void ExpandAll()
    {
        foreach (var manager in new[] { _leftFoldingManager, _rightFoldingManager, _unifiedFoldingManager })
            if (manager != null)
                foreach (var fold in manager.AllFoldings)
                    fold.IsFolded = false;

        Dispatcher.UIThread.Post(() => { UpdateSplitViewport(); UpdateUnifiedViewport(); }, DispatcherPriority.Loaded);
    }

    /// <summary>Switches between the split and unified views.</summary>
    public void SetView(bool isUnified)
    {
        _isUnified = isUnified;
        SplitViewToggle.IsChecked = !isUnified;
        UnifiedViewToggle.IsChecked = isUnified;
        _currentHunkIndex = -1;
        Render();
        ViewStateChanged?.Invoke(_isUnified, _ignoreWhitespace);
    }

    /// <summary>Recomputes the comparison with whitespace-only differences ignored or not.</summary>
    public void SetIgnoreWhitespace(bool ignore)
    {
        if (_result == null) return;

        _ignoreWhitespace = ignore;
        IgnoreWhitespaceToggle.IsChecked = ignore;
        _result = FileCompareEngine.Compute(
            _result.LeftName, _result.LeftText, _result.LeftIsAsciiStyled, _result.LeftWarning,
            _result.RightName, _result.RightText, _result.RightIsAsciiStyled, _result.RightWarning,
            ignore);
        _currentHunkIndex = -1;
        Render();
        ViewStateChanged?.Invoke(_isUnified, _ignoreWhitespace);
    }

    #endregion

    #region Private Methods

    private void SplitViewToggle_Click(object? sender, RoutedEventArgs e) => SetView(isUnified: false);
    private void UnifiedViewToggle_Click(object? sender, RoutedEventArgs e) => SetView(isUnified: true);
    private void PrevHunk_Click(object? sender, RoutedEventArgs e) => NavigateHunk(-1);
    private void NextHunk_Click(object? sender, RoutedEventArgs e) => NavigateHunk(1);
    private void ExpandAll_Click(object? sender, RoutedEventArgs e) => ExpandAll();
    private void IgnoreWhitespaceToggle_Click(object? sender, RoutedEventArgs e) => SetIgnoreWhitespace(IgnoreWhitespaceToggle.IsChecked == true);

    private void Render()
    {
        if (_result == null) return;

        SplitViewGrid.IsVisible = !_isUnified;
        UnifiedViewGrid.IsVisible = _isUnified;

        LeftHeaderText.Text = _result.LeftName;
        RightHeaderText.Text = _result.RightName;
        UnifiedHeaderText.Text = $"{_result.LeftName} ↔ {_result.RightName}";

        bool hasLeftWarning = !string.IsNullOrEmpty(_result.LeftWarning);
        bool hasRightWarning = !string.IsNullOrEmpty(_result.RightWarning);
        WarningsPanel.IsVisible = hasLeftWarning || hasRightWarning;
        LeftWarningText.IsVisible = hasLeftWarning;
        LeftWarningText.Text = _result.LeftWarning;
        RightWarningText.IsVisible = hasRightWarning;
        RightWarningText.Text = _result.RightWarning;

        // Each side is styled for its own kind; the unified pane, which mixes both files' lines
        // in one editor, follows the left side.
        var leftFont = _result.LeftIsAsciiStyled ? EditorFonts.Ascii : EditorFonts.Petscii;
        var rightFont = _result.RightIsAsciiStyled ? EditorFonts.Ascii : EditorFonts.Petscii;
        LeftEditor.FontFamily = leftFont;
        RightEditor.FontFamily = rightFont;
        UnifiedEditor.FontFamily = leftFont;
        _leftPetscii.IsAsmMode = _result.LeftIsAsciiStyled;
        _rightPetscii.IsAsmMode = _result.RightIsAsciiStyled;
        _unifiedPetscii.IsAsmMode = _result.LeftIsAsciiStyled;

        var oldLines = _result.SideBySide.OldText.Lines;
        var newLines = _result.SideBySide.NewText.Lines;
        var unifiedLines = _result.Unified.Lines;

        _leftColorizer.Lines = oldLines;
        _rightColorizer.Lines = newLines;
        _unifiedColorizer.Lines = unifiedLines;
        _leftRowTint.Lines = oldLines;
        _rightRowTint.Lines = newLines;
        _unifiedRowTint.Lines = unifiedLines;
        _unifiedPrefixMargin.Lines = unifiedLines;

        RebindDocument(LeftEditor, ref _leftFoldingManager, JoinLines(oldLines), _result.SplitCollapsedRuns);
        RebindDocument(RightEditor, ref _rightFoldingManager, JoinLines(newLines), _result.SplitCollapsedRuns);
        RebindDocument(UnifiedEditor, ref _unifiedFoldingManager, JoinLines(unifiedLines), _result.UnifiedCollapsedRuns);

        LeftMapStrip.Update(_result.LeftChangeRuns, [], oldLines.Count);
        RightMapStrip.Update([], _result.RightChangeRuns, newLines.Count);
        UnifiedMapStrip.Update(_result.UnifiedDeletedRuns, _result.UnifiedInsertedRuns, unifiedLines.Count);

        // After layout, so the panes' extents are real rather than the pre-layout zero that
        // would size the thumb to the whole track.
        Dispatcher.UIThread.Post(() => { UpdateSplitViewport(); UpdateUnifiedViewport(); }, DispatcherPriority.Loaded);

        int hunkCount = HunkCount;
        HunkCountText.Text = hunkCount == 1 ? "1 change" : $"{hunkCount} changes";
    }

    private void UpdateSplitViewport()
    {
        double extent = LeftEditor.ExtentHeight;
        SplitViewportThumb.UpdateViewport(extent > 0 ? LeftEditor.VerticalOffset / extent : 0, extent > 0 ? LeftEditor.ViewportHeight / extent : 1);
    }

    private void UpdateUnifiedViewport()
    {
        double extent = UnifiedEditor.ExtentHeight;
        UnifiedViewportThumb.UpdateViewport(extent > 0 ? UnifiedEditor.VerticalOffset / extent : 0, extent > 0 ? UnifiedEditor.ViewportHeight / extent : 1);
    }

    private static void ScrollToFraction(TextEditor editor, double fraction) =>
        editor.ScrollToVerticalOffset(fraction * editor.ExtentHeight);

    private static string JoinLines(IReadOnlyList<DiffPiece> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text ?? string.Empty));

    // A FoldingManager can't move to a new document, so each render installs a fresh one.
    private static void RebindDocument(TextEditor editor, ref FoldingManager? foldingManager, string text,
        IReadOnlyList<(int Start, int Count)> collapsedRuns)
    {
        if (foldingManager != null)
        {
            FoldingManager.Uninstall(foldingManager);
            foldingManager = null;
        }

        editor.Document = new TextDocument(text);
        var manager = FoldingManager.Install(editor.TextArea);
        manager.UpdateFoldings(BuildFoldings(editor.Document, collapsedRuns), -1);
        foreach (var fold in manager.AllFoldings)
            fold.IsFolded = true;
        foldingManager = manager;
    }

    private static IEnumerable<NewFolding> BuildFoldings(TextDocument document, IReadOnlyList<(int Start, int Count)> runs)
    {
        foreach (var (start, count) in runs)
        {
            int startLineNumber = start + 1;
            int endLineNumber = start + count;
            if (startLineNumber < 1 || endLineNumber > document.LineCount || endLineNumber <= startLineNumber)
                continue;

            var startLine = document.GetLineByNumber(startLineNumber);
            var endLine = document.GetLineByNumber(endLineNumber);
            yield return new NewFolding(startLine.Offset, endLine.EndOffset) { Name = $" ⋯ {count} unchanged lines ⋯ " };
        }
    }

    // Mirrors the split panes' scroll position. The reentrancy WPF had to defer around (a
    // thumb drag losing capture when the other pane laid out inside its handler) doesn't
    // arise with Avalonia's pointer capture, so this is direct.
    private void SyncScroll(TextEditor source, TextEditor target)
    {
        if (_syncingScroll) return;
        if (Math.Abs(source.VerticalOffset - target.VerticalOffset) < 0.5) return;

        _syncingScroll = true;
        try { target.ScrollToVerticalOffset(source.VerticalOffset); }
        finally { _syncingScroll = false; }
    }

    #endregion
}
