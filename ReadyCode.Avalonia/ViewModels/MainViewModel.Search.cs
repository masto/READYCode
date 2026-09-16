// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Text;
using ReadyCode.Models;
using ReadyCode.Search;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The Search panel: find (and replace) across every source file in the open folder - .bas,
/// .asm, .s, .txt, and .prg (detokenized, as opening one would). The matching and file reading
/// is the shared <see cref="ProjectSearcher"/>; this is the results and the replace, matching
/// WPF: files are searched as they are on disk, and Replace All edits an open tab through its
/// document (so undo works) and rewrites a file that isn't open.
/// </summary>
public partial class MainViewModel
{
    #region Private Fields

    private string _searchStatusText = "";

    #endregion

    #region Public Properties

    /// <summary>Gets the last search's results, one entry per file with matches.</summary>
    public ObservableCollection<ProjectSearchFileResult> SearchResults { get; } = new();

    /// <summary>Gets the line under the search box: the result count, or why there is none.</summary>
    public string SearchStatusText
    {
        get => _searchStatusText;
        private set { if (_searchStatusText == value) return; _searchStatusText = value; OnPropertyChanged(); }
    }

    /// <summary>Gets the number of matches in <see cref="SearchResults"/>.</summary>
    public int SearchMatchCount => SearchResults.Sum(f => f.Matches.Count);

    #endregion

    #region Public Methods

    /// <summary>
    /// Searches every source file under the open folder for <paramref name="query"/>,
    /// replacing <see cref="SearchResults"/>. Synchronous: project folders at this scale don't
    /// need more.
    /// </summary>
    /// <returns>The number of matches found.</returns>
    public int RunProjectSearch(string query, bool matchCase, bool wholeWord, bool useRegex)
    {
        SearchResults.Clear();

        string root = RootFolderPath;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(query))
        {
            SearchStatusText = string.IsNullOrEmpty(root) ? "Open a folder to search across its files." : "";
            return 0;
        }

        int totalMatches = 0;
        foreach (string path in ProjectSearcher.EnumerateSearchableFiles(root))
        {
            string? text = ReadSearchableTextAsShown(path);
            if (text == null) continue;

            var matches = ProjectSearcher.FindMatches(text, query, matchCase, wholeWord, useRegex);
            if (matches.Count == 0) continue;

            var lineStarts = ComputeLineStartOffsets(text);
            var fileResult = new ProjectSearchFileResult(path, Path.GetRelativePath(root, path));
            foreach (var (offset, length) in matches)
            {
                var (lineNumber, columnOffset, preview) = LocateMatch(text, lineStarts, offset);
                fileResult.Matches.Add(new ProjectSearchMatchInfo(fileResult, lineNumber, columnOffset, length, preview));
            }
            SearchResults.Add(fileResult);
            totalMatches += matches.Count;
        }

        SearchStatusText = totalMatches == 0
            ? "No results found."
            : $"{totalMatches} result{(totalMatches == 1 ? "" : "s")} in {SearchResults.Count} file{(SearchResults.Count == 1 ? "" : "s")}.";
        return totalMatches;
    }

    /// <summary>
    /// Replaces every current result with <paramref name="replacement"/>: through the document
    /// of a file that is open (one undo step, and the tab becomes modified), directly on disk
    /// otherwise. The caller confirms first; the results are stale afterwards and should be
    /// re-run.
    /// </summary>
    /// <returns>The number of files changed.</returns>
    public int ReplaceAllInProject(string replacement)
    {
        int filesChanged = 0;
        foreach (var fileResult in SearchResults.ToList())
        {
            if (fileResult.Matches.Count == 0) continue;
            var openTab = FindOpenTab(fileResult.FilePath);

            if (openTab != null)
            {
                var document = openTab.Document;
                document.BeginUpdate();
                try
                {
                    // Backwards, so each replacement leaves the earlier matches' offsets valid.
                    for (int i = fileResult.Matches.Count - 1; i >= 0; i--)
                    {
                        var m = fileResult.Matches[i];
                        if (m.LineNumber > document.LineCount) continue;
                        int offset = document.GetOffset(m.LineNumber, m.ColumnOffset + 1);
                        document.Replace(offset, Math.Min(m.MatchLength, document.TextLength - offset), replacement);
                    }
                }
                finally { document.EndUpdate(); }
                openTab.IsModified = true;
                filesChanged++;
                continue;
            }

            try
            {
                string? text = ReadSearchableTextAsShown(fileResult.FilePath);
                if (text == null) continue;

                var lineStarts = ComputeLineStartOffsets(text);
                var sb = new StringBuilder(text);
                for (int i = fileResult.Matches.Count - 1; i >= 0; i--)
                {
                    var m = fileResult.Matches[i];
                    int offset = lineStarts[m.LineNumber - 1] + m.ColumnOffset;
                    sb.Remove(offset, m.MatchLength);
                    sb.Insert(offset, replacement);
                }
                ProjectSearcher.WriteSearchableText(fileResult.FilePath, sb.ToString());
                filesChanged++;
            }
            catch (Exception ex)
            {
                ErrorRaised?.Invoke("Replace All Error", $"Error updating {fileResult.RelativeDisplayPath}: {ex.Message}");
            }
        }

        return filesChanged;
    }

    #endregion

    #region Private Methods

    // A .prg's text gets the same line-number padding it gets when opened, so a result's line
    // and column line up with the opened tab.
    private string? ReadSearchableTextAsShown(string path)
    {
        string? text = ProjectSearcher.ReadSearchableText(path);
        if (text != null && path.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
            text = PadLineNumbers(text);
        return text;
    }

    private static List<int> ComputeLineStartOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') offsets.Add(i + 1);
        return offsets;
    }

    private static (int LineNumber, int ColumnOffset, string LinePreview) LocateMatch(string text, List<int> lineStartOffsets, int offset)
    {
        int idx = lineStartOffsets.BinarySearch(offset);
        if (idx < 0) idx = ~idx - 1;
        int lineStart = lineStartOffsets[idx];
        int lineEnd = idx + 1 < lineStartOffsets.Count ? lineStartOffsets[idx + 1] - 1 : text.Length;
        if (lineEnd < lineStart) lineEnd = lineStart;
        string lineText = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
        return (idx + 1, offset - lineStart, lineText.Trim());
    }

    #endregion
}
