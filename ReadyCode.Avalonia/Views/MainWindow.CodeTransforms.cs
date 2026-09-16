// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Diagnostics;
using ReadyCode.Formatting;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Prettify;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Edit > Minify Code, Prettify Code, Renumber Code (BASIC) and Format Code (assembly): the
/// whole-document transforms. The transforms themselves are the shared ones in
/// <c>ReadyCode.Core</c>; this is the dialogs and the status messages around them, matching the
/// WPF app's. Minify/Prettify remember their last selections in the same settings keys WPF uses.
/// </summary>
public partial class MainWindow
{
    #region Private Methods

    private async void EditMinify_Click(object? sender, EventArgs e) => await ExecuteMinifyAsync();
    private async void EditPrettify_Click(object? sender, EventArgs e) => await ExecutePrettifyAsync();
    private async void EditRenumber_Click(object? sender, EventArgs e) => await ExecuteRenumberAsync();
    private void EditFormat_Click(object? sender, EventArgs e) => ExecuteFormatAsm();

    private async Task ExecuteMinifyAsync()
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        var settings = ViewModel.Settings;

        var choices = await OptionsDialog.ShowAsync(this, "Minify Code", "Select minification options to apply:",
        [
            ("Remove whitespace", settings.MinifyDialogRemoveWhitespace),
            ("Replace 0 with .", settings.MinifyDialogReplaceZeroWithDot),
            ("Use scientific notation", settings.MinifyDialogUseScientificNotation),
            ("Remove comments (REM statements)", settings.MinifyDialogRemoveComments),
            ("Simplify NEXT statements", settings.MinifyDialogSimplifyNext),
            ("Renumber line numbers and remove zero padding", settings.MinifyDialogRenumberLines),
        ], "Minify");
        if (choices == null) return;

        (settings.MinifyDialogRemoveWhitespace, settings.MinifyDialogReplaceZeroWithDot, settings.MinifyDialogUseScientificNotation,
         settings.MinifyDialogRemoveComments, settings.MinifyDialogSimplifyNext, settings.MinifyDialogRenumberLines) =
            (choices[0], choices[1], choices[2], choices[3], choices[4], choices[5]);
        ViewModel.SaveSettings();

        string source = Editor.Document.Text;
        string minified = CodeMinifier.Minify(source, choices[0], choices[1], choices[2], choices[3], choices[4], choices[5]);
        if (minified == source)
        {
            ViewModel.SetStatus("No changes — code is already minified.");
            return;
        }

        int bytesBefore = new PrgConverter().ConvertToPrg(source).Length - 2;
        int bytesAfter = new PrgConverter().ConvertToPrg(minified).Length - 2;
        ReplaceDocumentText(minified);
        ViewModel.SetStatus($"Code minified: {bytesBefore:N0} → {bytesAfter:N0} bytes ({bytesBefore - bytesAfter:N0} saved).");
    }

    private async Task ExecutePrettifyAsync()
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        var settings = ViewModel.Settings;
        int increment = settings.AutoNumberIncrement;
        int padding = settings.LineNumberPadding;

        var choices = await OptionsDialog.ShowAsync(this, "Prettify Code", "Select prettification options to apply:",
        [
            ("Add whitespace", settings.PrettifyDialogAddWhitespace),
            ("Replace . with 0", settings.PrettifyDialogReplacePeriodWithZero),
            ("Use standard notation", settings.PrettifyDialogUseStandardNotation),
            ("Add variables to NEXT statements", settings.PrettifyDialogAddNextVariables),
            ("Renumber code lines", settings.PrettifyDialogRenumberLines),
        ], "Prettify",
        padding > 0
            ? $"Renumbering starts at {increment}, step {increment}, {padding}-digit padding (Text Editor settings)"
            : $"Renumbering starts at {increment}, step {increment}, no padding (Text Editor settings)");
        if (choices == null) return;

        (settings.PrettifyDialogAddWhitespace, settings.PrettifyDialogReplacePeriodWithZero, settings.PrettifyDialogUseStandardNotation,
         settings.PrettifyDialogAddNextVariables, settings.PrettifyDialogRenumberLines) =
            (choices[0], choices[1], choices[2], choices[3], choices[4]);
        ViewModel.SaveSettings();

        string source = Editor.Document.Text;
        string prettified = CodePrettifier.Prettify(source, choices[0], choices[1], choices[2], choices[3], choices[4], increment, padding);
        if (prettified == source)
        {
            ViewModel.SetStatus("No changes — code is already prettified.");
            return;
        }

        ReplaceDocumentText(prettified);
        ViewModel.SetStatus("Code prettified.");
    }

    internal Task ExecuteRenumberAsync() => ExecuteRenumberAsync(null);

    // The dialog asks for the start, increment, and scope; pass a choice to skip it (tests).
    internal async Task ExecuteRenumberAsync(RenumberDialog.Choice? choice)
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        var document = Editor.Document;

        // The lowest selected line number seeds the dialog's start and, if the user keeps
        // "Selected lines only", is the renumber's scope.
        bool hasSelection = Editor.SelectionLength > 0;
        var selectedLineNumbers = new HashSet<int>();
        if (hasSelection)
        {
            var (selStartLine, selEndLine) = GetSelectedLineRange();
            for (int i = selStartLine; i <= selEndLine; i++)
                if (TryGetBasicLineNumber(document, i, out int n))
                    selectedLineNumbers.Add(n);
        }

        choice ??= await RenumberDialog.ShowAsync(this,
            selectedLineNumbers.Count > 0 ? selectedLineNumbers.Min() : 10, ViewModel.Settings.AutoNumberIncrement, hasSelection);
        if (choice == null) return;

        HashSet<int>? onlyLineNumbers = null;
        if (choice.SelectedOnly)
        {
            onlyLineNumbers = selectedLineNumbers;
            if (onlyLineNumbers.Count == 0)
            {
                ViewModel.SetStatus("No BASIC lines in the current selection to renumber.", StatusType.Warning);
                return;
            }
        }

        string source = document.Text;
        // References are rewritten across the whole document, so one outside the selection
        // pointing into it (or vice versa) stays right.
        string renumbered = CodePrettifier.RenumberLines(source, choice.StartLineNumber, choice.Increment, ViewModel.Settings.LineNumberPadding, onlyLineNumbers);
        if (renumbered == source)
        {
            ViewModel.SetStatus("No changes — line numbers are already sequential.");
            return;
        }

        // Renumbering can't fix a reference to a line that never existed, and renumbering only
        // a selection can land a new number on an untouched line outside it - both are left
        // as-is, so warn rather than silently applying either.
        var problems = BasicDiagnostics.Analyze(renumbered)
            .Where(d => d.Message.EndsWith("does not exist.") || d.Message.StartsWith("Duplicate line number"))
            .ToList();
        if (problems.Count > 0)
        {
            int dangling = problems.Count(d => d.Message.EndsWith("does not exist."));
            int duplicates = problems.Count(d => d.Message.StartsWith("Duplicate line number"));
            var parts = new List<string>();
            if (dangling > 0) parts.Add($"{dangling} GOTO/GOSUB/THEN reference(s) would point to line numbers that don't exist.");
            if (duplicates > 0) parts.Add($"{duplicates} line number(s) would end up duplicated.");
            parts.Add("Apply the renumber anyway?");

            string? answer = await MessageDialog.ShowAsync(this, "Renumber Code", string.Join("\n\n", parts), "Renumber", "Cancel");
            if (answer != "Renumber") return;
        }

        ReplaceDocumentText(renumbered);
        ViewModel.SetStatus("Code renumbered.");
    }

    // The assembly counterpart of the three above: reformats the whole document per the
    // Assembly Formatting settings, rather than only as-you-type via Enter.
    internal void ExecuteFormatAsm()
    {
        if (ViewModel.ActiveTab?.Language != EditorLanguage.Asm || string.IsNullOrWhiteSpace(Editor.Document.Text)) return;

        string source = Editor.Document.Text;
        string formatted = AsmCodeFormatter.Format(source, ViewModel.Settings.AsmMnemonicIndentColumn, ViewModel.Settings.AsmCommentAlignColumn);
        if (formatted == source)
        {
            ViewModel.SetStatus("No changes — code is already formatted.");
            return;
        }

        ReplaceDocumentText(formatted);
        ViewModel.SetStatus("Code formatted.");
    }

    private bool HasNonEmptyBasicActiveTab() =>
        ViewModel.ActiveTab?.Language == EditorLanguage.Basic && !string.IsNullOrWhiteSpace(Editor.Document.Text);

    // One undo step for the whole rewrite, and the caret kept somewhere sensible.
    private void ReplaceDocumentText(string text)
    {
        var document = Editor.Document;
        int caret = Math.Min(Editor.CaretOffset, text.Length);
        document.BeginUpdate();
        try { document.Text = text; }
        finally { document.EndUpdate(); }
        Editor.CaretOffset = caret;
        Editor.Focus();
    }

    #endregion
}
