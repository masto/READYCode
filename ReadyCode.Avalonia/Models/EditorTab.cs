// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaEdit.Document;
using ReadyCode.Diagnostics;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Models;

/// <summary>
/// One open document: its AvaloniaEdit <see cref="TextDocument"/> plus the file it came from and
/// how it should be treated (language, file kind). The Avalonia counterpart of the WPF app's
/// <c>EditorTab</c>, trimmed to what the cross-platform front end supports so far.
/// </summary>
public class EditorTab : INotifyPropertyChanged
{
    #region Private Fields

    private string? _filePath;
    private string? _displayName;
    private bool _isModified;
    private EditorLanguage _language = EditorLanguage.Basic;
    private C64UFileKind _kind = C64UFileKind.Prg;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="EditorTab"/> class.
    /// </summary>
    public EditorTab()
    {
        Document.TextChanged += (_, _) => IsModified = true;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the document backing this tab's text content.
    /// </summary>
    public TextDocument Document { get; } = new();

    /// <summary>
    /// Gets or sets the full path of the file this tab was opened from, or null for a new file.
    /// </summary>
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Header));
        }
    }

    /// <summary>
    /// Gets or sets the name shown for a tab that has no file path of its own (a program read
    /// from inside a disk image).
    /// </summary>
    public string? DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Header));
        }
    }

    /// <summary>
    /// Gets or sets the identity of a "virtual" entry read from a disk image, as
    /// "&lt;disk image path&gt;!&lt;entry name&gt;", or null for a real file. Saving such a tab
    /// writes the entry back into the image.
    /// </summary>
    public string? VirtualSourceId { get; set; }

    /// <summary>Gets whether this tab is a disk-image entry rather than a file on disk.</summary>
    public bool IsVirtual => VirtualSourceId != null;

    /// <summary>
    /// Gets or sets the diagnostics last computed for this tab's text, kept so the Problems
    /// panel can list every open tab's issues, not just the active one's.
    /// </summary>
    public IReadOnlyList<EditorDiagnostic> Diagnostics { get; set; } = Array.Empty<EditorDiagnostic>();

    /// <summary>
    /// Gets or sets the file kind (BASIC listing, tokenized PRG, assembly...), which decides how
    /// the text is rendered and saved.
    /// </summary>
    public C64UFileKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Creates an empty tab for a new file of the given language.
    /// </summary>
    public static EditorTab CreateNew(EditorLanguage language) => new()
    {
        Language = language,
        Kind = language == EditorLanguage.Asm ? C64UFileKind.Asm : C64UFileKind.Prg,
    };

    /// <summary>
    /// Gets or sets the language the editor treats this tab's text as.
    /// </summary>
    public EditorLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(Header));
        }
    }

    /// <summary>
    /// Gets the display file name. A new file is "Untitled.prg" or "Untitled.asm" - a new BASIC
    /// tab defaults to the tokenized .prg format so it renders with the C64 font and PETSCII
    /// glyphs from the start rather than only after it's saved.
    /// </summary>
    public string FileName => FilePath != null
        ? Path.GetFileName(FilePath)
        : DisplayName ?? (Language == EditorLanguage.Asm ? "Untitled.asm" : "Untitled.prg");

    /// <summary>
    /// Gets the tab header text: the file name plus a trailing marker when unsaved.
    /// </summary>
    public string Header => IsModified ? FileName + " •" : FileName;

    /// <summary>
    /// Gets or sets whether the document has unsaved changes.
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Header));
        }
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when a bindable property of this tab changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
