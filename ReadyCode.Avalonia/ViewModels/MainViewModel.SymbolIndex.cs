// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using ReadyCode.Assembler;
using ReadyCode.Avalonia.Models;
using ReadyCode.Diagnostics;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The Variable Explorer (BASIC) / Symbol Explorer (assembly) under the folder explorer: every
/// variable, or every label and constant, in the active document with each occurrence as a
/// child. Static - it comes from the source, not a debug session, which is what the Debug
/// panel's Variables view is for. The analysis is the shared <see cref="VariableCrossReference"/>
/// and <see cref="AsmSymbolIndex"/>; the shape of the collections and the diff-in-place update
/// (so expanded nodes stay expanded across an edit) match the WPF app's.
/// </summary>
public partial class MainViewModel
{
    #region Public Properties

    /// <summary>Gets the active BASIC document's variables, alphabetically; empty for an assembly tab.</summary>
    public ObservableCollection<VariableInfo> Variables { get; } = new();

    /// <summary>Gets the active assembly document's "NAME = value" constants.</summary>
    public AsmSymbolGroupInfo ConstantSymbols { get; } = new("Constants");

    /// <summary>Gets the active assembly document's labels.</summary>
    public AsmSymbolGroupInfo LabelSymbols { get; } = new("Labels");

    /// <summary>Gets the Symbol Explorer's top-level groups: constants, then labels.</summary>
    public ObservableCollection<AsmSymbolGroupInfo> SymbolGroups { get; }

    /// <summary>
    /// Gets or sets whether the Variable/Symbol Explorer shows under the folder explorer.
    /// Persisted in the same setting the WPF app uses.
    /// </summary>
    public bool ShowVariableExplorer
    {
        get => Settings.ShowVariableExplorer;
        set
        {
            if (Settings.ShowVariableExplorer == value) return;
            Settings.ShowVariableExplorer = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    /// <summary>Gets the panel's header: "VARIABLES (n)" for BASIC, "SYMBOLS (n)" for assembly.</summary>
    public string SymbolPanelTitle => ActiveTab?.Language == EditorLanguage.Asm
        ? $"SYMBOLS ({ConstantSymbols.Symbols.Count + LabelSymbols.Symbols.Count})"
        : $"VARIABLES ({Variables.Count})";

    #endregion

    #region Public Methods

    /// <summary>
    /// Rebuilds the variable or symbol index for the active tab, updating the existing entries
    /// in place so their expanded state survives. Called after every document analysis.
    /// </summary>
    /// <param name="asmResult">The assembly the diagnostics pass already ran, to avoid assembling twice; null to assemble here.</param>
    public void RefreshSymbolIndex(AssemblyResult? asmResult = null)
    {
        var tab = ActiveTab;
        if (tab is { IsHexMode: true } or { IsCompareMode: true }) tab = null;
        if (tab?.Language == EditorLanguage.Asm)
        {
            Variables.Clear();
            RefreshAsmSymbols(tab, asmResult);
        }
        else
        {
            ConstantSymbols.Symbols.Clear();
            LabelSymbols.Symbols.Clear();
            if (tab != null) RefreshBasicVariables(tab);
            else Variables.Clear();
        }
        OnPropertyChanged(nameof(SymbolPanelTitle));
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be a BASIC variable name: a letter, then letters or
    /// digits, an optional trailing $ or %, and not itself a keyword (the tokenizer would crunch
    /// "FOR" as the keyword; a name that merely contains one, like "SCORE", is fine).
    /// </summary>
    public static bool IsValidVariableName(string name)
    {
        int end = name.Length;
        if (end > 0 && (name[end - 1] == '$' || name[end - 1] == '%')) end--;
        if (end == 0 || !char.IsLetter(name[0])) return false;

        for (int i = 1; i < end; i++)
            if (!char.IsLetterOrDigit(name[i])) return false;

        return !BasicTokens.TryMatchKeyword(name, 0, BasicTokens.WordKeywordsLongestFirst, out string keyword)
            || keyword.Length != end;
    }

    /// <summary>
    /// Renames every occurrence of <paramref name="oldName"/> in the active document to
    /// <paramref name="newName"/> as one undo step, and refreshes the index. Returns the number
    /// of occurrences changed, or -1 if the new name isn't a valid variable name.
    /// </summary>
    public int RenameVariable(string oldName, string newName)
    {
        newName = newName.Trim().ToUpperInvariant();
        if (ActiveTab is not { } tab || string.IsNullOrEmpty(newName) || newName == oldName) return 0;
        if (!IsValidVariableName(newName))
        {
            SetStatus($"\"{newName}\" isn't a valid BASIC variable name.", StatusType.Warning);
            return -1;
        }

        var document = tab.Document;
        var occurrences = VariableCrossReference.Analyze(document.Text)
            .Where(r => r.Name == oldName)
            .OrderByDescending(r => r.Offset) // back-to-front so earlier offsets stay valid
            .ToList();
        if (occurrences.Count == 0) return 0;

        document.BeginUpdate();
        try
        {
            foreach (var occurrence in occurrences)
                document.Replace(occurrence.Offset, occurrence.Length, newName);
        }
        finally { document.EndUpdate(); }

        RefreshSymbolIndex();
        SetStatus($"Renamed {oldName} to {newName} ({occurrences.Count} occurrence{(occurrences.Count == 1 ? "" : "s")}).");
        return occurrences.Count;
    }

    #endregion

    #region Private Methods

    private void RefreshBasicVariables(EditorTab tab)
    {
        var document = tab.Document;
        var byName = VariableCrossReference.Analyze(document.Text).GroupBy(r => r.Name, StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            seenNames.Add(group.Key);

            var existing = Variables.FirstOrDefault(v => v.Name == group.Key);
            if (existing == null)
            {
                existing = new VariableInfo(group.Key);
                int insertAt = 0;
                while (insertAt < Variables.Count && string.CompareOrdinal(Variables[insertAt].Name, group.Key) < 0)
                    insertAt++;
                Variables.Insert(insertAt, existing);
            }

            existing.Occurrences.Clear();
            foreach (var reference in group.OrderBy(r => r.Offset))
            {
                var line = document.GetLineByOffset(Math.Min(reference.Offset, document.TextLength));
                string lineText = document.GetText(line.Offset, line.Length).TrimStart();
                int digits = 0;
                while (digits < lineText.Length && char.IsDigit(lineText[digits])) digits++;
                int.TryParse(lineText[..digits], out int basicLineNumber);
                existing.Occurrences.Add(new VariableOccurrenceInfo(line.LineNumber, basicLineNumber, reference.IsWrite));
            }
        }

        for (int i = Variables.Count - 1; i >= 0; i--)
            if (!seenNames.Contains(Variables[i].Name)) Variables.RemoveAt(i);
    }

    private void RefreshAsmSymbols(EditorTab tab, AssemblyResult? asmResult)
    {
        var constants = ConstantSymbols.Symbols;
        var labels = LabelSymbols.Symbols;

        asmResult ??= new Asm6502Assembler().Assemble(
            tab.Document.Text, Settings.AsmOutputMode == "Standalone", (ushort)Settings.AsmDefaultOriginAddress);

        var byName = AsmSymbolIndex.Analyze(asmResult.ParsedLines).GroupBy(o => o.Name, StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byName)
        {
            seenNames.Add(group.Key);

            bool isConstant = group.Any(o => o.Kind == AsmSymbolKind.ConstantDefinition);
            var targetGroup = isConstant ? constants : labels;
            var otherGroup = isConstant ? labels : constants;

            // A symbol reclassified since the last pass (a constant redefined further down as a
            // label) moves to its new group, keeping its instance and so its expanded state.
            var existing = targetGroup.FirstOrDefault(s => s.Name == group.Key);
            if (existing == null)
            {
                var stray = otherGroup.FirstOrDefault(s => s.Name == group.Key);
                if (stray != null) otherGroup.Remove(stray);

                existing = stray ?? new AsmSymbolInfo(group.Key);
                int insertAt = 0;
                while (insertAt < targetGroup.Count && string.CompareOrdinal(targetGroup[insertAt].Name, group.Key) < 0)
                    insertAt++;
                targetGroup.Insert(insertAt, existing);
            }

            existing.TypeBadge = isConstant ? "CONST" : "LABEL";
            existing.ValueText = isConstant
                ? (asmResult.Constants.TryGetValue(group.Key, out int constValue) ? $"${constValue:X4}" : null)
                : (asmResult.Labels.TryGetValue(group.Key, out ushort labelAddress) ? $"${labelAddress:X4}" : null);

            existing.Occurrences.Clear();
            foreach (var occurrence in group.OrderBy(o => o.LineNumber))
                existing.Occurrences.Add(new AsmSymbolOccurrenceInfo(occurrence.LineNumber, occurrence.Kind));
        }

        RemoveStaleSymbols(constants, seenNames);
        RemoveStaleSymbols(labels, seenNames);
    }

    private static void RemoveStaleSymbols(ObservableCollection<AsmSymbolInfo> symbols, HashSet<string> seenNames)
    {
        for (int i = symbols.Count - 1; i >= 0; i--)
            if (!seenNames.Contains(symbols[i].Name)) symbols.RemoveAt(i);
    }

    #endregion
}
