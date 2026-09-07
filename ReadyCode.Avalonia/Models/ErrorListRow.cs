// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Avalonia.Models;

/// <summary>
/// One row of the Problems panel: a diagnostic plus where it lives, so activating the row can
/// jump to it.
/// </summary>
public sealed class ErrorListRow
{
    #region Public Properties

    /// <summary>
    /// Gets the tab the diagnostic was reported for.
    /// </summary>
    public required EditorTab Tab { get; init; }

    /// <summary>
    /// Gets the diagnostic message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the character offset the diagnostic starts at.
    /// </summary>
    public required int Offset { get; init; }

    /// <summary>
    /// Gets the 1-based document line the diagnostic is on.
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Gets the BASIC line number the diagnostic is on, or null for assembly source (which has no
    /// BASIC line numbering).
    /// </summary>
    public int? BasicLineNumber { get; init; }

    /// <summary>
    /// Gets the display name of the file the diagnostic is in.
    /// </summary>
    public string FileName => Tab.FileName;

    /// <summary>
    /// Gets the location text: the BASIC line number when there is one, else the document line.
    /// </summary>
    public string Location => BasicLineNumber is { } basicLineNumber ? $"line {basicLineNumber}" : $"line {Line}";

    #endregion
}
