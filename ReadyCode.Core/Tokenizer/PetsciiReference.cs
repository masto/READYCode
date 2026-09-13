// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// The data behind the PETSCII Reference and Quick Keys panels: which PETSCII codes are control
/// characters and what they're called, and the subset that gets a Quick Keys card and keyboard
/// shortcut. Shared by both front ends so the panels and their shortcuts can't drift apart.
/// </summary>
public static class PetsciiReference
{
    #region Public Fields

    /// <summary>
    /// The highest PETSCII code the reference table lists individually. 192-255 repeat earlier
    /// ranges - see <see cref="FooterNotes"/>.
    /// </summary>
    public const int LastListedCode = 191;

    /// <summary>The notes shown under the table explaining codes 192-255.</summary>
    public static readonly IReadOnlyList<string> FooterNotes =
    [
        "192–223: same as 96–127",
        "224–254: same as 160–190",
        "255: same as 126",
    ];

    /// <summary>The Quick Keys panel's sections, in display order.</summary>
    public static readonly IReadOnlyList<QuickKeySection> QuickKeySections =
    [
        new("SCREEN CONTROL", "CTRL+1–8", QuickKeyModifiers.Control,
        [
            new(19,  "HOME",  "Home",         1),
            new(147, "CLR",   "CLR",          2),
            new(18,  "RVS ON",  "Reverse On",  3),
            new(146, "RVS OFF", "Reverse Off", 4),
            new(157, "CUR ←", "Cursor Left",  5),
            new(29,  "CUR →", "Cursor Right", 6),
            new(145, "CUR ↑", "Cursor Up",    7),
            new(17,  "CUR ↓", "Cursor Down",  8),
        ]),
        new("PRINTING COLORS", "CTRL+SHIFT+1–8", QuickKeyModifiers.ControlShift,
        [
            new(144, "BLACK",  "Black",  1, pokeColor: 0),
            new(5,   "WHITE",  "White",  2, pokeColor: 1),
            new(28,  "RED",    "Red",    3, pokeColor: 2),
            new(159, "CYAN",   "Cyan",   4, pokeColor: 3),
            new(156, "PURPLE", "Purple", 5, pokeColor: 4),
            new(30,  "GREEN",  "Green",  6, pokeColor: 5),
            new(31,  "BLUE",   "Blue",   7, pokeColor: 6),
            new(158, "YELLOW", "Yellow", 8, pokeColor: 7),
        ]),
        new(null, "CTRL+SHIFT+ALT+1–8", QuickKeyModifiers.ControlShiftAlt,
        [
            new(129, "ORANGE",    "Orange",      1, pokeColor: 8),
            new(149, "BROWN",     "Brown",       2, pokeColor: 9),
            new(150, "LT. RED",   "Light Red",   3, pokeColor: 10),
            new(151, "GRAY 1",    "Gray 1",      4, pokeColor: 11),
            new(152, "GRAY 2",    "Gray 2",      5, pokeColor: 12),
            new(153, "LT. GREEN", "Light Green", 6, pokeColor: 13),
            new(154, "LT. BLUE",  "Light Blue",  7, pokeColor: 14),
            new(155, "GRAY 3",    "Gray 3",      8, pokeColor: 15),
        ]),
        new("FUNCTION KEYS", "SHIFT+F1–F8", QuickKeyModifiers.ShiftFunctionKey,
        [
            new(133, "FUNC 1", "Function 1", 1),
            new(137, "FUNC 2", "Function 2", 2),
            new(134, "FUNC 3", "Function 3", 3),
            new(138, "FUNC 4", "Function 4", 4),
            new(135, "FUNC 5", "Function 5", 5),
            new(139, "FUNC 6", "Function 6", 6),
            new(136, "FUNC 7", "Function 7", 7),
            new(140, "FUNC 8", "Function 8", 8),
        ]),
    ];

    #endregion

    #region Private Fields

    // Every control code in the reference table. "" marks a code with no PRINT effect (listed,
    // but not clickable); anything else is the label shown on its chip. Codes not in this table
    // are printable glyphs.
    private static readonly Dictionary<int, string> _controlLabels = new()
    {
        [0]  = "", [1]  = "", [2]  = "", [3]  = "", [4]  = "",
        [5]  = "WHT",
        [6]  = "DISABLE SHIFT C=",
        [7]  = "ENABLE SHIFT C=",
        [8]  = "", [9]  = "", [10] = "", [11] = "", [12] = "",
        [13] = "RETURN",
        [14] = "LOWER CASE",
        [15] = "", [16] = "",
        [17] = "CRSR↓",
        [18] = "RVS ON",
        [19] = "CLR HOME",
        [20] = "INST DEL",
        [21] = "", [22] = "", [23] = "", [24] = "", [25] = "", [26] = "", [27] = "",
        [28] = "RED",
        [29] = "CRSR→",
        [30] = "GRN",
        [31] = "BLU",
        [32] = "SPACE",
        [128] = "",
        [129] = "ORANGE",
        [130] = "", [131] = "",
        [132] = "F7/8",
        [133] = "F1",
        [134] = "F3",
        [135] = "F5",
        [136] = "F7",
        [137] = "F2",
        [138] = "F4",
        [139] = "F6",
        [140] = "F8",
        [141] = "SHIFT RETURN",
        [142] = "UPPER CASE",
        [143] = "",
        [144] = "BLK",
        [145] = "CRSR↑",
        [146] = "RVS OFF",
        [147] = "CLR HOME",
        [148] = "INST DEL",
        [149] = "BROWN",
        [150] = "LT RED",
        [151] = "GRAY 1",
        [152] = "GRAY 2",
        [153] = "LT GREEN",
        [154] = "LT BLUE",
        [155] = "GRAY 3",
        [156] = "PUR",
        [157] = "←CRSR",
        [158] = "YEL",
        [159] = "CYN",
        [160] = "SPACE",
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Describes how the reference table lists a PETSCII code.
    /// </summary>
    /// <param name="code">The PETSCII code, 0-255.</param>
    /// <returns>
    /// <see cref="PetsciiCodeKind.Glyph"/> for a printable character (shown as its screen glyph),
    /// <see cref="PetsciiCodeKind.Control"/> for a control character (shown as a labelled chip),
    /// or <see cref="PetsciiCodeKind.Undefined"/> for a code with no PRINT effect (listed, but not
    /// insertable).
    /// </returns>
    public static PetsciiCodeKind Classify(int code)
    {
        if (!_controlLabels.TryGetValue(code, out string? label)) return PetsciiCodeKind.Glyph;
        return label.Length == 0 ? PetsciiCodeKind.Undefined : PetsciiCodeKind.Control;
    }

    /// <summary>
    /// Gets the chip label for a control code, or null if <paramref name="code"/> isn't one.
    /// </summary>
    public static string? ControlLabel(int code) =>
        _controlLabels.TryGetValue(code, out string? label) && label.Length > 0 ? label : null;

    /// <summary>Gets every Quick Keys card, across all sections.</summary>
    public static IEnumerable<QuickKey> AllQuickKeys => QuickKeySections.SelectMany(s => s.Keys);

    #endregion
}

/// <summary>How the PETSCII Reference table presents a code - see <see cref="PetsciiReference.Classify"/>.</summary>
public enum PetsciiCodeKind
{
    /// <summary>A printable character, shown as its C64 screen glyph.</summary>
    Glyph,

    /// <summary>A control character, shown as a labelled chip.</summary>
    Control,

    /// <summary>A code with no PRINT effect: listed for completeness, not insertable.</summary>
    Undefined,
}

/// <summary>
/// The modifier keys a Quick Keys section's shortcuts use, combined with the card's
/// <see cref="QuickKey.KeyNumber"/>: a digit key for the first three, a function key for the last.
/// </summary>
public enum QuickKeyModifiers
{
    /// <summary>Ctrl + digit.</summary>
    Control,

    /// <summary>Ctrl+Shift + digit.</summary>
    ControlShift,

    /// <summary>Ctrl+Shift+Alt + digit.</summary>
    ControlShiftAlt,

    /// <summary>Shift + F-key.</summary>
    ShiftFunctionKey,
}

/// <summary>
/// One section of the Quick Keys panel.
/// </summary>
/// <param name="Title">The section header, or null to continue the previous section under a new shortcut hint.</param>
/// <param name="ShortcutHint">The shortcut range shown under the header, e.g. "CTRL+1–8".</param>
/// <param name="Modifiers">The modifiers every card in the section is bound with.</param>
/// <param name="Keys">The cards, in display order.</param>
public sealed record QuickKeySection(string? Title, string ShortcutHint, QuickKeyModifiers Modifiers, IReadOnlyList<QuickKey> Keys)
{
    /// <summary>Gets the shortcut for one of this section's cards as text, e.g. "Ctrl+Shift+3".</summary>
    public string ShortcutText(QuickKey key) => Modifiers switch
    {
        QuickKeyModifiers.Control => $"Ctrl+{key.KeyNumber}",
        QuickKeyModifiers.ControlShift => $"Ctrl+Shift+{key.KeyNumber}",
        QuickKeyModifiers.ControlShiftAlt => $"Ctrl+Shift+Alt+{key.KeyNumber}",
        _ => $"Shift+F{key.KeyNumber}",
    };

    /// <summary>Gets the tooltip for one of this section's cards, e.g. "Cyan: Ctrl+Shift+4; CHR$(159); POKE(3)".</summary>
    public string Tooltip(QuickKey key) =>
        $"{key.Name}: {ShortcutText(key)}; CHR$({key.Code})" + (key.PokeColor is { } poke ? $"; POKE({poke})" : "");
}

/// <summary>
/// One Quick Keys card: a PETSCII control character and the shortcut that inserts it.
/// </summary>
/// <param name="Code">The PETSCII code inserted.</param>
/// <param name="Label">The short label under the card's glyph, e.g. "RVS ON".</param>
/// <param name="Name">The full name for tooltips, e.g. "Reverse On".</param>
/// <param name="KeyNumber">The digit (1-8) or F-key number (1-8) of the card's shortcut.</param>
/// <param name="pokeColor">For printing colors, the value POKEd for the same color, shown in the tooltip.</param>
public sealed record QuickKey(int Code, string Label, string Name, int KeyNumber, int? pokeColor = null)
{
    /// <summary>For printing colors, the value POKEd for the same color; null otherwise.</summary>
    public int? PokeColor { get; } = pokeColor;
}
