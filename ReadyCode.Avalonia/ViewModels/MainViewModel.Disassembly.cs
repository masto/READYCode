// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Assembler;
using ReadyCode.Avalonia.Models;
using ReadyCode.C64U;
using ReadyCode.Models;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The disassembler: "Disassemble at…" reads a range of live memory from VICE or the C64
/// Ultimate into a read-only listing tab with an address gutter; "Disassemble file" does the
/// same for a machine-language program's bytes, opening an ordinary editable assembly tab.
/// The disassembly itself is the shared <see cref="Asm6502Disassembler"/> and
/// <see cref="PrgFileDisassembler"/>; this is the tab handling around them, matching WPF.
/// </summary>
public partial class MainViewModel
{
    #region Public Methods

    /// <summary>
    /// Opens a new, empty live-memory disassembly tab for <paramref name="source"/>, ready for
    /// an address range. Read-only until Save As turns it into an ordinary assembly file.
    /// </summary>
    public EditorTab OpenDisassemblyTab(DisassemblySource source)
    {
        var tab = new EditorTab
        {
            Language = EditorLanguage.Asm,
            Kind = C64UFileKind.Asm,
            DisplayName = source == DisassemblySource.Vice ? "Disassembly (VICE).asm" : "Disassembly (C64U).asm",
            IsDisassemblyMode = true,
            DisassemblySource = source,
        };
        tab.Document.Text = "; Enter a Start and End address above, then click Disassemble.";
        tab.IsModified = false;

        AddTab(tab);
        ActiveTab = tab;
        return tab;
    }

    /// <summary>
    /// Reads <paramref name="start"/>..<paramref name="end"/> from <paramref name="tab"/>'s
    /// machine and replaces the tab's text with the disassembly. The tab is captured up front so
    /// a slow read that finishes after the user switched tabs still lands in the right one.
    /// </summary>
    public async Task<bool> DisassembleMemoryAsync(EditorTab tab, ushort start, ushort end)
    {
        if (!tab.IsDisassemblyMode) return false;

        bool useVice = tab.DisassemblySource == DisassemblySource.Vice;
        if (useVice && !EnsureVicePathConfigured()) return false;
        if (!useVice && string.IsNullOrWhiteSpace(Settings.C64UUrl))
        {
            SetStatus("C64U URL not set. Go to Preferences > Settings to configure it.", StatusType.Error);
            return false;
        }

        int length = end - start + 1;
        try
        {
            SetStatus(useVice ? $"Reading {length:N0} bytes from VICE…" : $"Reading {length:N0} bytes from the C64 Ultimate…");

            byte[] bytes = useVice
                ? await new ViceClient(Settings.ViceMonitorHost, Settings.ViceMonitorPort).ReadMemoryAsync(start, length)
                : await new C64UltimateClient().ReadMemoryAsync(Settings.C64UUrl, start, length);

            ApplyDisassembly(tab, new Asm6502Disassembler().Disassemble(bytes, start, Settings.AsmMnemonicIndentColumn, Settings.AsmCommentAlignColumn));
            SetStatus($"Disassembled {length:N0} bytes from ${start:X4}-${end:X4}.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Disassembly failed: {ex.Message}", StatusType.Error);
            return false;
        }
    }

    /// <summary>
    /// Disassembles a machine-language program (a .prg with its 2-byte load address) into a
    /// new assembly tab named after it. A BASIC loader stub at the front, if any, becomes a
    /// comment describing it, and the gutter shows each line's real address.
    /// </summary>
    public EditorTab? DisassembleFileBytes(byte[] prgBytes, string displayName)
    {
        if (prgBytes.Length < 2)
        {
            ErrorRaised?.Invoke("Disassemble File", $"'{displayName}' is too small to disassemble.");
            return null;
        }

        var result = PrgFileDisassembler.Disassemble(prgBytes, Settings.AsmMnemonicIndentColumn, Settings.AsmCommentAlignColumn);
        var tab = new EditorTab
        {
            Language = EditorLanguage.Asm,
            Kind = C64UFileKind.Asm,
            DisplayName = $"{Path.GetFileNameWithoutExtension(displayName)} (Disassembled).asm",
        };

        // The stub comment's own lines have no address, so the addresses shift past them.
        if (result.StubCommentLines != null)
        {
            tab.Document.Text = string.Join(Environment.NewLine, result.StubCommentLines) + Environment.NewLine + result.Source;
            tab.DisassemblyLineAddresses = result.LineAddresses.ToDictionary(kvp => kvp.Key + result.StubCommentLines.Count, kvp => kvp.Value);
        }
        else
        {
            tab.Document.Text = result.Source;
            tab.DisassemblyLineAddresses = result.LineAddresses;
        }
        tab.IsModified = false;

        AddTab(tab);
        ActiveTab = tab;
        SetStatus($"Disassembled {displayName}.");
        return tab;
    }

    /// <summary>"Disassemble file" for a local explorer item: a file on disk or an entry in a disk image.</summary>
    public EditorTab? DisassembleFile(FileTreeItem item)
    {
        try
        {
            byte[] bytes = item.Content ?? File.ReadAllBytes(item.FullPath);
            return DisassembleFileBytes(bytes, item.Name);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Disassemble File", $"Error disassembling file: {ex.Message}");
            return null;
        }
    }

    /// <summary>"Disassemble file" for a C64U explorer item: a remote file (downloaded) or an entry in a mounted image.</summary>
    public async Task<EditorTab?> DisassembleC64UFileAsync(C64UFileItem item)
    {
        try
        {
            byte[] bytes = item.Content ?? (C64UFtp != null ? await C64UFtp.DownloadBytesAsync(item.FullPath) : throw new InvalidOperationException("Not connected to the C64 Ultimate."));
            return DisassembleFileBytes(bytes, item.Name);
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke("Disassemble File", $"Error disassembling file: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Private Methods

    private static void ApplyDisassembly(EditorTab tab, DisassemblyResult result)
    {
        tab.Document.Text = result.Source;
        tab.DisassemblyLineAddresses = result.LineAddresses;
        tab.IsModified = false;
    }

    #endregion
}
