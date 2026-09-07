// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Colors numeric literals in BASIC code, skipping the leading line number, string contents,
/// and digits that are really part of a variable name (e.g. the "1" in "A1").
/// </summary>
public class NumberLiteralColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _leadingLineNumberPattern = new(@"^(\s*)(\d+)", RegexOptions.Compiled);

    // A period with no adjacent digits (e.g. the "." in "KP=.") is CBM BASIC shorthand for the
    // literal 0, so it's a numeric literal too - matched via the second alternative below (which
    // also covers a leading-decimal literal like ".5").
    private static readonly Regex _numberPattern =
        new(@"(\d+(\.\d+)?|\.\d*)([Ee][+-]?\d+)?", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw numeric literals.
    /// </summary>
    public IBrush NumberBrush { get; set; } = Brushes.Teal;

    #endregion

    #region Protected Methods

    /// <summary>
    /// Colorizes every numeric literal on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        Match leading = _leadingLineNumberPattern.Match(text);
        if (leading.Success)
            i = leading.Groups[2].Index + leading.Groups[2].Length;

        bool inString = false;
        bool afterRawLetter = false;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '"')
            {
                inString = !inString;
                afterRawLetter = false;
                i++;
                continue;
            }

            if (inString)
            {
                i++;
                continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                if (afterRawLetter)
                {
                    // Whatever follows an unmatched identifier letter isn't a fresh literal
                    // (e.g. the "1" in "A1") - skip it without coloring.
                    if (char.IsDigit(c))
                        while (i < text.Length && char.IsDigit(text[i])) i++;
                    else
                        i++;
                }
                else
                {
                    Match m = _numberPattern.Match(text, i);
                    int start = line.Offset + i;
                    int end   = line.Offset + i + m.Length;
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(NumberBrush));
                    i += m.Length;
                }

                afterRawLetter = false;
                continue;
            }

            if (char.IsLetter(c))
            {
                int kwLen = MatchKeywordLength(text, i);
                if (kwLen > 0)
                {
                    i += kwLen;
                    afterRawLetter = false;
                }
                else
                {
                    i++;
                    afterRawLetter = true;
                }
                continue;
            }

            afterRawLetter = false;
            i++;
        }
    }

    #endregion

    #region Private Methods

    // Returns the length of the longest keyword matching at position i, or 0 if none match.
    private static int MatchKeywordLength(string text, int i) =>
        BasicTokens.TryMatchKeyword(text, i, BasicTokens.WordKeywordsLongestFirst, out string keyword)
            ? keyword.Length
            : 0;

    #endregion
}
