// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors 6502 mnemonics: three-letter words that match the opcode table, at word boundaries.
/// </summary>
public class AsmMnemonicColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw mnemonics.
    /// </summary>
    public IBrush MnemonicBrush { get; set; } = Brushes.Blue;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes every 6502 mnemonic on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
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
                        element => element.TextRunProperties.SetForegroundBrush(MnemonicBrush));
                    i += 3;
                    continue;
                }
            }

            // Not a mnemonic at this position - skip the rest of this identifier so its interior
            // letters aren't re-tested at a non-boundary position.
            while (i < text.Length && IsWordChar(text[i])) i++;
        }
    }

    #endregion

    #region Private Methods

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}

/// <summary>
/// Colors assembly numeric literals: immediate and absolute hex ($), binary (%), and decimal.
/// </summary>
public class AsmNumberLiteralColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _immHexPattern = new(@"\G#\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _immBinPattern = new(@"\G#%[01]+", RegexOptions.Compiled);
    private static readonly Regex _immDecPattern = new(@"\G#\d+", RegexOptions.Compiled);
    private static readonly Regex _hexPattern = new(@"\G\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _binPattern = new(@"\G%[01]+", RegexOptions.Compiled);
    private static readonly Regex _decPattern = new(@"\G\d+", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw numeric literals.
    /// </summary>
    public IBrush NumberBrush { get; set; } = Brushes.Teal;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes every immediate, hex, binary, and decimal literal on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            Match match = MatchAt(text, i);
            if (match.Success)
            {
                int start = line.Offset + i;
                int end = line.Offset + i + match.Length;
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(NumberBrush));
                i += match.Length;
                continue;
            }

            i++;
        }
    }

    #endregion

    #region Private Methods

    private static Match MatchAt(string text, int i)
    {
        Match match = _immHexPattern.Match(text, i);
        if (match.Success) return match;
        match = _immBinPattern.Match(text, i);
        if (match.Success) return match;
        match = _hexPattern.Match(text, i);
        if (match.Success) return match;
        match = _binPattern.Match(text, i);
        if (match.Success) return match;
        match = _immDecPattern.Match(text, i);
        if (match.Success) return match;

        if (char.IsDigit(text[i]) && (i == 0 || !IsWordChar(text[i - 1])))
        {
            match = _decPattern.Match(text, i);
            if (match.Success) return match;
        }

        return Match.Empty;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}

/// <summary>
/// Colors a label definition at the start of a line ("name:"), including its colon.
/// </summary>
public class AsmLabelColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _labelPattern = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*):", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw label definitions.
    /// </summary>
    public IBrush LabelBrush { get; set; } = Brushes.DarkCyan;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes a label definition at the start of the given line, if there is one.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        Match match = _labelPattern.Match(text);
        if (!match.Success) return;

        var nameGroup = match.Groups[1];
        int start = line.Offset + nameGroup.Index;
        int end = start + nameGroup.Length + 1; // include the trailing colon
        ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(LabelBrush));
    }

    #endregion
}

/// <summary>
/// Colors everything from a ";" to the end of the line as a comment.
/// </summary>
public class AsmCommentColorizer : DocumentColorizingTransformer
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw comments.
    /// </summary>
    public IBrush CommentBrush { get; set; } = Brushes.Green;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes a ";" comment on the given line, if there is one.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int index = text.IndexOf(';');
        if (index < 0) return;

        int start = line.Offset + index;
        int end = line.Offset + text.Length;
        ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(CommentBrush));
    }

    #endregion
}
