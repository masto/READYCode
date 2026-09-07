// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Rendering;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Renders bytes outside the normal printable ASCII range using the glyph the C64 character ROM
/// shows for that screen code (e.g. CHR$(147)/CLR renders as the reverse-video heart), without
/// altering the underlying document text. This keeps round-tripping and existing text-based
/// features (keyword highlighting, line-number padding, tokenizing) working unchanged, since they
/// only ever see the original characters.
/// </summary>
public class PetsciiGlyphGenerator : VisualLineElementGenerator
{
    /// <summary>
    /// Gets or sets whether the active document is plain ASCII source (assembly, or a .bas
    /// listing) rather than a PETSCII-styled listing. Plain source must never be reinterpreted as
    /// PETSCII bytes, so substitution is skipped entirely.
    /// </summary>
    public bool IsAsmMode { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (IsAsmMode)
            return -1;

        var document = CurrentContext.Document;
        int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

        for (int i = startOffset; i < endOffset; i++)
        {
            char c = document.GetCharAt(i);
            if (c <= 0xFF && PetsciiScreenCodeMap.NeedsGlyphSubstitution((byte)c))
                return i;
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        char ch = CurrentContext.Document.GetCharAt(offset);
        if (ch > 0xFF)
            return null;

        byte screenCode = PetsciiScreenCodeMap.ToScreenCode((byte)ch);
        string glyph = ((char)(0xE000 + screenCode)).ToString();
        return new PetsciiGlyphElement(glyph);
    }

    private sealed class PetsciiGlyphElement : VisualLineElement
    {
        private readonly string _glyph;

        public PetsciiGlyphElement(string glyph) : base(1, 1)
        {
            _glyph = glyph;
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            return new TextCharacters(_glyph, TextRunProperties);
        }
    }
}
