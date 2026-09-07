// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using ReadyCode.Editor;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// AvaloniaEdit adapter over <see cref="BasicFoldRegionFinder"/>: FOR...NEXT blocks and runs of
/// REM lines.
/// </summary>
public class BasicFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateNewFoldings(document), -1);

    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document) =>
        BasicFoldRegionFinder.Find(document.ToSourceLines())
            .Select(r => new NewFolding(r.StartOffset, r.EndOffset));
}

/// <summary>
/// AvaloniaEdit adapter over <see cref="AsmFoldRegionFinder"/>: runs of ";" comment lines.
/// </summary>
public class AsmFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateNewFoldings(document), -1);

    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document) =>
        AsmFoldRegionFinder.Find(document.ToSourceLines())
            .Select(r => new NewFolding(r.StartOffset, r.EndOffset));
}
