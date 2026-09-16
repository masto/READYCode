// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media;

namespace ReadyCode.Avalonia.Editor;

/// <summary>The two editor fonts: the C64 one for BASIC/PETSCII text, a monospace one for assembly and plain text.</summary>
public static class EditorFonts
{
    /// <summary>Gets the embedded Pet Me 64 font.</summary>
    public static FontFamily Petscii { get; } = new("avares://ReadyCode.Avalonia/Assets/Fonts#Pet Me 64");

    /// <summary>Gets the plain monospace font, whichever of the usual ones the platform has.</summary>
    public static FontFamily Ascii { get; } = new("Menlo,Consolas,DejaVu Sans Mono,monospace");
}
