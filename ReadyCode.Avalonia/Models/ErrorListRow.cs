// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Avalonia.Models;

/// <summary>
/// One row of the Problems panel: a diagnostic plus where it lives, so clicking it can jump there.
/// </summary>
public sealed class ErrorListRow
{
    public required EditorTab Tab { get; init; }
    public required string Message { get; init; }
    public required int Offset { get; init; }
    public required int Line { get; init; }
    public int? BasicLineNumber { get; init; }

    public string FileName => Tab.FileName;

    /// <summary>Gets the location text: the BASIC line number when there is one, else the document line.</summary>
    public string Location => BasicLineNumber is { } n ? $"line {n}" : $"line {Line}";
}
