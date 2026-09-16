// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using ReadyCode.Avalonia.Editor;
using ReadyCode.Diagnostics;
using ReadyCode.Editor;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Keyword completion: the inline ghost-text suggestion that follows the caret as you type
/// (Tab accepts it) and the Ctrl+Space popup listing every match. The keyword tables and
/// matching are the shared <see cref="BasicCompletionProvider"/> and
/// <see cref="AsmCompletionProvider"/>; this is the editor wiring, ported from WPF.
/// </summary>
public partial class MainWindow
{
    #region Private Fields

    private GhostTextRenderer _ghostRenderer = null!;
    private CompletionWindow? _completionWindow;

    #endregion

    #region Internal Properties

    /// <summary>Gets the ghost-text suggestion currently shown after the caret, or "" if none.</summary>
    internal string GhostText => _ghostRenderer.GhostText;

    /// <summary>Gets whether the Ctrl+Space completion popup is open.</summary>
    internal bool IsCompletionPopupOpen => _completionWindow != null;

    /// <summary>Gets the entries the open completion popup is showing, in order; empty if it is closed.</summary>
    internal IReadOnlyList<string> CompletionPopupItems =>
        _completionWindow?.CompletionList.CompletionData.Select(d => d.Text).ToList() ?? [];

    #endregion

    #region Private Methods

    private void InstallCompletion()
    {
        _ghostRenderer = new GhostTextRenderer(Editor);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_ghostRenderer);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateGhostText();
    }

    // Runs first in the editor's tunnel KeyDown, ahead of the C64 keyboard emulation and the
    // BASIC line-numbering handled there. Returns true when the key was consumed.
    private bool HandleCompletionKey(KeyEventArgs e)
    {
        // While the popup is open it owns Enter/Tab/arrows (through the text area's own tunnel
        // handler, which runs after this one) - keep the line-numbering logic from also acting.
        if (_completionWindow != null)
        {
            if (e.Key == Key.Back)
            {
                // Re-filter once the character is actually gone.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_completionWindow == null) return;
                    var (_, word) = GetWordBeforeCaret();
                    if (string.IsNullOrEmpty(word)) _completionWindow.Hide();
                    else _completionWindow.CompletionList.SelectItem(word);
                });
            }
            return e.Key is Key.Enter or Key.Tab or Key.Space;
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            OpenCompletionPopup();
            return true;
        }

        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && _ghostRenderer.GhostText.Length > 0)
        {
            AcceptGhostCompletion();
            return true;
        }

        return false;
    }

    // The identifier being typed: the run of letters, digits, '$' and '#' ending at the caret,
    // upper-cased since the providers compare case-insensitively against upper-case keywords.
    private (int offset, string word) GetWordBeforeCaret()
    {
        int caretOffset = Editor.CaretOffset;
        var doc = Editor.Document;
        int pos = caretOffset - 1;

        while (pos >= 0)
        {
            char c = doc.GetCharAt(pos);
            if (char.IsLetterOrDigit(c) || c == '$' || c == '#')
                pos--;
            else
                break;
        }

        int wordStart = pos + 1;
        if (wordStart >= caretOffset) return (caretOffset, string.Empty);

        string word = doc.GetText(wordStart, caretOffset - wordStart).ToUpperInvariant();
        return (wordStart, word);
    }

    // Updates (or clears) the inline suggestion from what is before the caret. Called on every
    // caret move, so it tracks typing, and cleared explicitly by the places that move the caret
    // without typing (Find, the PETSCII pickers), where a suggestion would be noise.
    private void UpdateGhostText()
    {
        if (Editor.IsReadOnly || _completionWindow != null || ViewModel.ActiveTab == null)
        {
            ClearGhostText();
            return;
        }

        // Past a REM the rest of the line is a comment - no keyword makes sense there, the same
        // "everything after REM isn't code" rule the analyses follow. (Upstream f8b2d3e.)
        if (ViewModel.ActiveTab.Language == EditorLanguage.Basic && IsCaretPastRem())
        {
            ClearGhostText();
            return;
        }

        var (_, word) = GetWordBeforeCaret();
        if (string.IsNullOrEmpty(word) || word.All(char.IsDigit))
        {
            ClearGhostText();
            return;
        }

        // Only suggest at the true end of the identifier - if a word character immediately
        // follows (the caret clicked between "PR" and "INT"), the suggestion would overlap it.
        int caretOffset = Editor.CaretOffset;
        if (caretOffset < Editor.Document.TextLength)
        {
            char next = Editor.Document.GetCharAt(caretOffset);
            if (char.IsLetterOrDigit(next) || next == '$' || next == '#')
            {
                ClearGhostText();
                return;
            }
        }

        var matches = GetCompletionMatches(word);
        if (matches.Count == 0)
        {
            ClearGhostText();
            return;
        }

        // matches is sorted alphabetically; the first is the suggestion, and the ghost text is
        // the part of its snippet the user hasn't typed yet.
        string snippet = matches[0].Snippet;
        _ghostRenderer.GhostText = snippet.Length > word.Length ? snippet[word.Length..] : string.Empty;
    }

    private bool IsCaretPastRem()
    {
        var caretLine = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        string lineText = Editor.Document.GetText(caretLine);
        if (!BasicDiagnostics.TryParseLineNumber(lineText, out _, out _, out _, out int codeStart)) return false;

        string code = lineText[codeStart..];
        int remStart = BasicDiagnostics.FindTopLevelRemStart(code);
        if (remStart >= code.Length) return false;
        if (!BasicTokens.TryMatchKeyword(code, remStart, BasicTokens.WordKeywordsLongestFirst, out string remKeyword)) return false;

        int caretCol = Editor.CaretOffset - caretLine.Offset - codeStart;
        return caretCol >= remStart + remKeyword.Length;
    }

    private void ClearGhostText() => _ghostRenderer.GhostText = string.Empty;

    private void AcceptGhostCompletion()
    {
        var (wordStart, word) = GetWordBeforeCaret();
        if (string.IsNullOrEmpty(word)) return;

        var matches = GetCompletionMatches(word);
        if (matches.Count == 0) return;

        ClearGhostText();
        matches[0].Complete(Editor.TextArea, new TextSegment { StartOffset = wordStart, Length = Editor.CaretOffset - wordStart }, EventArgs.Empty);
    }

    // Ctrl+Space: every keyword matching the current prefix, or all of them if nothing has
    // been typed yet.
    private void OpenCompletionPopup()
    {
        if (Editor.IsReadOnly || ViewModel.ActiveTab == null) return;

        _completionWindow?.Hide();
        ClearGhostText();

        var (wordStart, word) = GetWordBeforeCaret();
        var matches = string.IsNullOrEmpty(word) || word.All(char.IsDigit)
            ? GetAllCompletionItems()
            : GetCompletionMatches(word);
        if (matches.Count == 0) return;

        var popup = new CompletionWindow(Editor.TextArea) { StartOffset = wordStart };
        foreach (var item in matches)
            popup.CompletionList.CompletionData.Add(item);
        if (!string.IsNullOrEmpty(word))
            popup.CompletionList.SelectItem(word);
        popup.Closed += (_, _) => { if (ReferenceEquals(_completionWindow, popup)) _completionWindow = null; };
        _completionWindow = popup;
        popup.Show();
    }

    // Matches from whichever provider fits the active tab's language, so BASIC keywords and
    // assembly mnemonics never mix.
    private List<KeywordCompletionData> GetCompletionMatches(string prefix) =>
        (ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? AsmCompletionProvider.GetMatches(prefix)
            : BasicCompletionProvider.GetMatches(prefix))
            .Select(i => new KeywordCompletionData(i)).ToList();

    private List<KeywordCompletionData> GetAllCompletionItems() =>
        (ViewModel.ActiveTab?.Language == EditorLanguage.Asm
            ? AsmCompletionProvider.AllItems
            : BasicCompletionProvider.AllItems)
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)
            .Select(i => new KeywordCompletionData(i)).ToList();

    #endregion
}
