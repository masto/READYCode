// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors 6502 mnemonics (three-letter words that match the opcode table, at word boundaries).
/// </summary>
public class AsmMnemonicColorizer : DocumentColorizingTransformer
{
    public IBrush MnemonicBrush { get; set; } = Brushes.Blue;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            bool leftBoundary = i == 0 || !IsWordChar(text[i - 1]);
            if (leftBoundary && i + 3 <= text.Length)
            {
                bool rightBoundary = i + 3 == text.Length || !IsWordChar(text[i + 3]);
                if (rightBoundary && AsmTokens.IsMnemonic(text.Substring(i, 3)))
                {
                    int absoluteStart = line.Offset + i;
                    ChangeLinePart(absoluteStart, absoluteStart + 3,
                        e => e.TextRunProperties.SetForegroundBrush(MnemonicBrush));
                    i += 3;
                    continue;
                }
            }

            // Not a mnemonic at this position - skip the rest of this identifier so its
            // interior letters aren't re-tested at a non-boundary position.
            while (i < text.Length && IsWordChar(text[i])) i++;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>
/// Colors assembly numeric literals: immediate and absolute hex ($), binary (%), and decimal.
/// </summary>
public class AsmNumberLiteralColorizer : DocumentColorizingTransformer
{
    private static readonly Regex _immHexPattern = new(@"\G#\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _immBinPattern = new(@"\G#%[01]+", RegexOptions.Compiled);
    private static readonly Regex _immDecPattern = new(@"\G#\d+", RegexOptions.Compiled);
    private static readonly Regex _hexPattern = new(@"\G\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _binPattern = new(@"\G%[01]+", RegexOptions.Compiled);
    private static readonly Regex _decPattern = new(@"\G\d+", RegexOptions.Compiled);

    public IBrush NumberBrush { get; set; } = Brushes.Teal;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            Match m = MatchAt(text, i);
            if (m.Success)
            {
                int start = line.Offset + i;
                int end = line.Offset + i + m.Length;
                ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(NumberBrush));
                i += m.Length;
                continue;
            }
            i++;
        }
    }

    private static Match MatchAt(string text, int i)
    {
        Match m = _immHexPattern.Match(text, i);
        if (m.Success) return m;
        m = _immBinPattern.Match(text, i);
        if (m.Success) return m;
        m = _hexPattern.Match(text, i);
        if (m.Success) return m;
        m = _binPattern.Match(text, i);
        if (m.Success) return m;
        m = _immDecPattern.Match(text, i);
        if (m.Success) return m;

        if (char.IsDigit(text[i]) && (i == 0 || !IsWordChar(text[i - 1])))
        {
            m = _decPattern.Match(text, i);
            if (m.Success) return m;
        }

        return Match.Empty;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>
/// Colors a label definition at the start of a line ("name:"), including its colon.
/// </summary>
public class AsmLabelColorizer : DocumentColorizingTransformer
{
    private static readonly Regex _labelPattern = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*):", RegexOptions.Compiled);

    public IBrush LabelBrush { get; set; } = Brushes.DarkCyan;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match m = _labelPattern.Match(text);
        if (!m.Success) return;

        var nameGroup = m.Groups[1];
        int start = line.Offset + nameGroup.Index;
        int end = start + nameGroup.Length + 1; // include the trailing colon
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(LabelBrush));
    }
}

/// <summary>
/// Colors everything from a ";" to the end of the line as a comment.
/// </summary>
public class AsmCommentColorizer : DocumentColorizingTransformer
{
    public IBrush CommentBrush { get; set; } = Brushes.Green;

    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int idx = text.IndexOf(';');
        if (idx < 0) return;

        int start = line.Offset + idx;
        int end = line.Offset + text.Length;
        ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(CommentBrush));
    }
}
