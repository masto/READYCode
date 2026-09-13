// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Diagnostics;
using ReadyCode.Formatting;
using ReadyCode.Minify;
using ReadyCode.Models;
using ReadyCode.Prettify;
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

    internal async Task ExecuteRenumberAsync()
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        int increment = ViewModel.Settings.AutoNumberIncrement;
        int padding = ViewModel.Settings.LineNumberPadding;

        string source = Editor.Document.Text;
        string renumbered = CodePrettifier.RenumberLines(source, increment, increment, padding);
        if (renumbered == source)
        {
            ViewModel.SetStatus("No changes — line numbers are already sequential.");
            return;
        }

        // Renumbering can't fix a reference to a line number that never existed - it's left
        // unchanged, so warn rather than silently applying a renumber with dangling references.
        int dangling = BasicDiagnostics.Analyze(renumbered).Count(d => d.Message.EndsWith("does not exist."));
        if (dangling > 0)
        {
            string? choice = await MessageDialog.ShowAsync(this, "Renumber Code",
                $"{dangling} GOTO/GOSUB/THEN reference(s) point to line numbers that don't exist and will be left unchanged. Apply the renumber anyway?",
                "Renumber", "Cancel");
            if (choice != "Renumber") return;
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
