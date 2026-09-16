// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;

namespace ReadyCode.Avalonia.ViewModels;

/// <summary>
/// The right-hand panel: the reference panels (Quick Keys, PETSCII Reference, BASIC Keywords,
/// ASM Mnemonics, Music Notes) and the activity bar that switches between them. Which one is
/// showing and whether the panel is open persist in the same settings keys the WPF app uses.
/// </summary>
public partial class MainViewModel
{
    #region Public Fields

    /// <summary>Settings keys of the right panels, in activity-bar order.</summary>
    public static readonly IReadOnlyList<string> RightPanels = ["QuickKeys", "Petscii", "BasicKeywords", "AsmKeywords", "MusicNotes"];

    #endregion

    #region Public Properties

    /// <summary>Gets or sets whether the right panel is open. Persisted in settings.</summary>
    public bool IsRightPanelOpen
    {
        get => Settings.IsRightPanelOpen;
        set
        {
            if (Settings.IsRightPanelOpen == value) return;
            Settings.IsRightPanelOpen = value;
            OnPropertyChanged();
            NotifyRightPanelChanged();
        }
    }

    /// <summary>
    /// Gets or sets which reference panel the right panel shows - one of <see cref="RightPanels"/>.
    /// Persisted in settings.
    /// </summary>
    public string ActiveRightPanel
    {
        get => RightPanels.Contains(Settings.ActiveRightPanel) ? Settings.ActiveRightPanel : RightPanels[0];
        set
        {
            if (Settings.ActiveRightPanel == value) return;
            Settings.ActiveRightPanel = value;
            OnPropertyChanged();
            NotifyRightPanelChanged();
        }
    }

    /// <summary>Gets the right panel's header text for the active panel.</summary>
    public string RightPanelTitle => ActiveRightPanel switch
    {
        "Petscii" => "PETSCII REFERENCE",
        "BasicKeywords" => "BASIC KEYWORDS",
        "AsmKeywords" => "ASM MNEMONICS",
        "MusicNotes" => "MUSIC NOTES",
        _ => "QUICK KEYS",
    };

    /// <summary>Gets whether the Quick Keys panel is the one showing.</summary>
    public bool IsQuickKeysActive => ActiveRightPanel == "QuickKeys";

    /// <summary>Gets whether the PETSCII Reference panel is the one showing.</summary>
    public bool IsPetsciiActive => ActiveRightPanel == "Petscii";

    /// <summary>Gets whether the BASIC Keywords panel is the one showing.</summary>
    public bool IsBasicKeywordsActive => ActiveRightPanel == "BasicKeywords";

    /// <summary>Gets whether the ASM Mnemonics panel is the one showing.</summary>
    public bool IsAsmKeywordsActive => ActiveRightPanel == "AsmKeywords";

    /// <summary>Gets whether the Music Notes panel is the one showing.</summary>
    public bool IsMusicNotesActive => ActiveRightPanel == "MusicNotes";

    /// <summary>Gets whether the activity bar's Quick Keys icon shows as active.</summary>
    public bool IsQuickKeysToggleChecked => IsRightPanelOpen && IsQuickKeysActive;

    /// <summary>Gets whether the activity bar's PETSCII Reference icon shows as active.</summary>
    public bool IsPetsciiToggleChecked => IsRightPanelOpen && IsPetsciiActive;

    /// <summary>Gets whether the activity bar's BASIC Keywords icon shows as active.</summary>
    public bool IsBasicKeywordsToggleChecked => IsRightPanelOpen && IsBasicKeywordsActive;

    /// <summary>Gets whether the activity bar's ASM Mnemonics icon shows as active.</summary>
    public bool IsAsmKeywordsToggleChecked => IsRightPanelOpen && IsAsmKeywordsActive;

    /// <summary>Gets whether the activity bar's Music Notes icon shows as active.</summary>
    public bool IsMusicNotesToggleChecked => IsRightPanelOpen && IsMusicNotesActive;

    /// <summary>
    /// Gets whether the active tab is BASIC - what decides whether the BASIC Keywords icon and the
    /// BASIC-only Edit menu commands (Minify, Prettify, Renumber) are offered.
    /// </summary>
    public bool IsBasicTabActive => ActiveTab is { IsHexMode: false, IsCompareMode: false } tab && tab.Language != EditorLanguage.Asm;

    /// <summary>
    /// Gets whether the active tab is assembly - what decides whether the ASM Mnemonics icon and
    /// Edit > Format Code are offered.
    /// </summary>
    public bool IsAsmTabActive => ActiveTab is { IsHexMode: false, IsCompareMode: false, Language: EditorLanguage.Asm };

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows <paramref name="panel"/> in the right panel, opening it if it was collapsed; or, if
    /// that panel is the one already showing, collapses the right panel - the activity bar's
    /// click behavior, the same as the left bar's.
    /// </summary>
    public void ToggleRightPanel(string panel)
    {
        if (IsRightPanelOpen && ActiveRightPanel == panel)
        {
            IsRightPanelOpen = false;
            return;
        }
        ActiveRightPanel = panel;
        IsRightPanelOpen = true;
    }

    /// <summary>
    /// View > Secondary Side Bar: collapses the right panel if it's open, otherwise opens it on
    /// Quick Keys.
    /// </summary>
    public void ToggleSecondarySideBar()
    {
        if (IsRightPanelOpen) IsRightPanelOpen = false;
        else ToggleRightPanel("QuickKeys");
    }

    /// <summary>
    /// Keeps the language-specific panels honest when the active tab's language changes: the
    /// BASIC Keywords / ASM Mnemonics icons swap, and if the one being hidden had its panel
    /// open, the panel closes.
    /// </summary>
    public void ApplyLanguageToRightPanel()
    {
        OnPropertyChanged(nameof(IsBasicTabActive));
        OnPropertyChanged(nameof(IsAsmTabActive));

        bool isAsm = ActiveTab?.Language == EditorLanguage.Asm;
        if (IsRightPanelOpen && ((isAsm && IsBasicKeywordsActive) || (!isAsm && IsAsmKeywordsActive)))
            IsRightPanelOpen = false;
    }

    #endregion

    #region Private Methods

    private void NotifyRightPanelChanged()
    {
        OnPropertyChanged(nameof(RightPanelTitle));
        OnPropertyChanged(nameof(IsQuickKeysActive));
        OnPropertyChanged(nameof(IsPetsciiActive));
        OnPropertyChanged(nameof(IsBasicKeywordsActive));
        OnPropertyChanged(nameof(IsAsmKeywordsActive));
        OnPropertyChanged(nameof(IsMusicNotesActive));
        OnPropertyChanged(nameof(IsQuickKeysToggleChecked));
        OnPropertyChanged(nameof(IsPetsciiToggleChecked));
        OnPropertyChanged(nameof(IsBasicKeywordsToggleChecked));
        OnPropertyChanged(nameof(IsAsmKeywordsToggleChecked));
        OnPropertyChanged(nameof(IsMusicNotesToggleChecked));
    }

    #endregion
}
