// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using ReadyCode.Assembler;
using ReadyCode.Models;
using ReadyCode.Tokenizer;
using ReadyCode.Avalonia.ViewModels;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The smaller editing commands ported from WPF: Go to Line, Comment / Uncomment Selection,
/// Make Uppercase / Lowercase, Code Statistics, Export / Import as text, and the File > Open
/// Recent submenu. Each matches the WPF implementation's behavior line for line.
/// </summary>
public partial class MainWindow
{
    #region Private Fields

    private static readonly FilePickerFileType _textFiles = new("Text Files") { Patterns = ["*.txt"] };

    #endregion

    #region Private Methods - Go to Line

    private async Task ExecuteGoToLineAsync()
    {
        var document = Editor.Document;
        if (document == null || document.LineCount == 0 || string.IsNullOrEmpty(document.Text)) return;

        int minBasicLine = int.MaxValue, maxBasicLine = int.MinValue;
        for (int i = 1; i <= document.LineCount; i++)
        {
            if (TryGetBasicLineNumber(document, i, out int n))
            {
                if (n < minBasicLine) minBasicLine = n;
                if (n > maxBasicLine) maxBasicLine = n;
            }
        }

        bool hasBasicLines = minBasicLine != int.MaxValue;
        var result = await GoToLineDialog.ShowAsync(this,
            hasBasicLines ? minBasicLine : 0, hasBasicLines ? maxBasicLine : 0, document.LineCount, hasBasicLines);
        if (result is not { } target) return;

        if (target.IsFileLine)
        {
            MoveCaretToDocumentLine(Math.Clamp(target.Line, 1, document.LineCount));
            return;
        }

        if (!JumpToBasicLine(target.Line))
            ViewModel.SetStatus($"BASIC line {target.Line} not found.", StatusType.Warning);
    }

    // Moves the caret to the start of the document line whose leading BASIC line number equals
    // `target`, scrolling it into view. Returns false if no such line exists.
    private bool JumpToBasicLine(int target)
    {
        var document = Editor.Document;
        for (int i = 1; i <= document.LineCount; i++)
        {
            if (TryGetBasicLineNumber(document, i, out int n) && n == target)
            {
                MoveCaretToDocumentLine(i);
                return true;
            }
        }
        return false;
    }

    private static bool TryGetBasicLineNumber(TextDocument document, int lineIndex, out int basicLineNumber)
    {
        basicLineNumber = 0;
        var line = document.GetLineByNumber(lineIndex);
        string text = document.GetText(line.Offset, line.Length).TrimStart();
        int j = 0;
        while (j < text.Length && char.IsDigit(text[j])) j++;
        return j > 0 && int.TryParse(text[0..j], out basicLineNumber);
    }

    #endregion

    #region Private Methods - Comment / Uncomment

    private (int start, int end) GetSelectedLineRange()
    {
        var doc = Editor.Document;
        if (Editor.SelectionLength == 0)
        {
            int n = doc.GetLineByOffset(Editor.CaretOffset).LineNumber;
            return (n, n);
        }

        int selStart = Editor.SelectionStart;
        int selEnd = selStart + Editor.SelectionLength;
        int startLine = doc.GetLineByOffset(selStart).LineNumber;
        var endDocLine = doc.GetLineByOffset(selEnd);
        // If selection ends exactly at a line's start, exclude that line
        int endLine = (endDocLine.Offset == selEnd && endDocLine.LineNumber > startLine)
            ? endDocLine.LineNumber - 1
            : endDocLine.LineNumber;
        return (startLine, endLine);
    }

    // Returns (index of first non-whitespace after optional BASIC line number,
    //          whether a space was already present before that position)
    private static (int cmdIndex, bool hadSpace) ParseBasicLinePrefix(string text)
    {
        int i = 0;
        while (i < text.Length && text[i] == ' ') i++;      // leading whitespace
        int numStart = i;
        while (i < text.Length && char.IsDigit(text[i])) i++; // line number digits
        bool hasDigits = i > numStart;
        int afterDigits = i;
        while (i < text.Length && text[i] == ' ') i++;      // space(s) after line number
        bool hadSpace = i > afterDigits;
        return (i, !hasDigits || hadSpace);                  // hadSpace=true means no extra space needed
    }

    internal void ExecuteCommentSelection()
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        var doc = Editor.Document;
        var (startLine, endLine) = GetSelectedLineRange();

        doc.BeginUpdate();
        try
        {
            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var docLine = doc.GetLineByNumber(lineNum);
                string text = doc.GetText(docLine.Offset, docLine.Length);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var (cmd, hadSpace) = ParseBasicLinePrefix(text);
                if (cmd >= text.Length) continue;

                // Skip lines already commented
                string rest = text[cmd..];
                if (rest.StartsWith("REM ", StringComparison.OrdinalIgnoreCase) ||
                    rest.Equals("REM", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Insert "REM " (prepend a space if the line number had none after it)
                string insertion = hadSpace ? "REM " : " REM ";
                doc.Insert(docLine.Offset + cmd, insertion);
            }
        }
        finally { doc.EndUpdate(); }
    }

    internal void ExecuteUncommentSelection()
    {
        if (!HasNonEmptyBasicActiveTab()) return;
        var doc = Editor.Document;
        var (startLine, endLine) = GetSelectedLineRange();

        doc.BeginUpdate();
        try
        {
            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var docLine = doc.GetLineByNumber(lineNum);
                string text = doc.GetText(docLine.Offset, docLine.Length);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var (cmd, _) = ParseBasicLinePrefix(text);
                if (cmd >= text.Length) continue;

                string rest = text[cmd..];
                if (rest.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                    doc.Remove(docLine.Offset + cmd, 4);
                else if (rest.Equals("REM", StringComparison.OrdinalIgnoreCase))
                    doc.Remove(docLine.Offset + cmd, 3);
            }
        }
        finally { doc.EndUpdate(); }
    }

    #endregion

    #region Private Methods - Make Uppercase / Lowercase

    // Converts the highlighted text to upper or lower case, leaving it selected afterward.
    // Does nothing if no text is highlighted.
    internal void ExecuteChangeSelectionCase(bool upper)
    {
        if (Editor.SelectionLength == 0) return;

        int start = Editor.SelectionStart;
        string newText = upper ? Editor.SelectedText.ToUpperInvariant() : Editor.SelectedText.ToLowerInvariant();

        Editor.Document.Replace(start, Editor.SelectionLength, newText);
        Editor.Select(start, newText.Length);
    }

    #endregion

    #region Private Methods - Code Statistics

    private async Task ShowCodeStatisticsAsync()
    {
        if (ViewModel.ActiveTab is not { } tab || string.IsNullOrEmpty(tab.Document.Text)) return;

        string text = tab.Document.Text;
        int charCount = text.Length;
        int lineCount = tab.Document.LineCount;
        int wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        string byteLabel, byteValue, byteDescription;

        if (tab.Language == EditorLanguage.Asm)
        {
            var asmResult = new Asm6502Assembler().Assemble(
                text, ViewModel.Settings.AsmOutputMode == "Standalone", (ushort)ViewModel.Settings.AsmDefaultOriginAddress);
            byteLabel = "Assembled bytes";
            if (asmResult.Success)
            {
                byteValue = $"{asmResult.PrgBytes!.Length - 2:N0}";
                byteDescription = "Assembled bytes is the size of the machine code, excluding the 2-byte load address header.";
            }
            else
            {
                byteValue = "Assembly errors";
                byteDescription = "Fix the assembly errors in this file to see its assembled byte count.";
            }
        }
        else
        {
            var prgData = new PrgConverter().ConvertToPrg(text);
            int tokenBytes = prgData.Length - 2;  // subtract 2-byte load address header
            byteLabel = "Tokenized bytes";
            byteValue = $"{tokenBytes:N0} / 38,911";
            byteDescription = "Tokenized bytes is the C64 BASIC memory footprint (of 38,911 bytes available).";
        }

        await new CodeStatisticsWindow(charCount, wordCount, lineCount, byteLabel, byteValue, byteDescription).ShowDialog(this);
    }

    #endregion

    #region Private Methods - Export / Import

    private async Task ExportTextAsync()
    {
        if (ViewModel.ActiveTab is not { } tab) return;
        if (string.IsNullOrWhiteSpace(tab.Document.Text))
        {
            await MessageDialog.ShowAsync(this, "Export", "There is no code to export.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as Text File",
            SuggestedFileName = tab.FilePath != null ? Path.GetFileNameWithoutExtension(tab.FilePath) + ".txt" : "program.txt",
            DefaultExtension = "txt",
            SuggestedStartLocation = await SuggestedFolderAsync(),
            FileTypeChoices = [_textFiles, FilePickerFileTypes.All],
        });

        if (file?.TryGetLocalPath() is { } path)
            ViewModel.ExportText(path);
    }

    private async Task ImportTextAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Text File",
            AllowMultiple = false,
            SuggestedStartLocation = await SuggestedFolderAsync(),
            FileTypeFilter = [_textFiles, FilePickerFileTypes.All],
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            ViewModel.ImportText(path);
    }

    #endregion

    #region Private Methods - Open Recent

    // Rebuilds File > Open Recent from the view model's list. The submenu is a NativeMenu, so
    // the same items feed both the system menu bar (macOS) and the in-window one.
    private void RefreshRecentFilesMenu()
    {
        if (NativeMenu.GetMenu(this) is not { } menu || FindItem(menu, "File")?.Menu is not { } fileMenu
            || FindItem(fileMenu, "Open Recent")?.Menu is not { } recentMenu)
            return;

        recentMenu.Items.Clear();
        var files = ViewModel.RecentFiles;
        if (files.Count == 0)
        {
            recentMenu.Items.Add(new NativeMenuItem("(none)") { IsEnabled = false });
            return;
        }

        foreach (string path in files)
        {
            string capturedPath = path;
            var item = new NativeMenuItem(Path.GetFileName(path)) { ToolTip = path };
            item.Click += (_, _) => OpenRecentFile(capturedPath);
            recentMenu.Items.Add(item);
        }
    }

    private async void OpenRecentFile(string path)
    {
        if (ViewModel.FindOpenTab(path) is { IsModified: true } dirty)
        {
            string? choice = await MessageDialog.ShowAsync(this, "Reload File",
                $"{dirty.FileName} has unsaved changes. Reload it from disk and discard them?", "Reload", "Cancel");
            if (choice != "Reload") return;
        }

        ViewModel.OpenFile(path);
    }

    #endregion
}
