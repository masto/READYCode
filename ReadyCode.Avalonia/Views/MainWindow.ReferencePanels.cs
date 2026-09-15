// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ReadyCode.Editor;
using ReadyCode.Sid;
using ReadyCode.Tokenizer;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The right-hand reference panels (Quick Keys, PETSCII Reference, BASIC Keywords, ASM
/// Mnemonics, Music Notes), the activity bar that switches between them, and the Quick Keys
/// keyboard shortcuts. Everything here is generated from the shared tables in
/// <c>ReadyCode.Core</c> - <see cref="PetsciiReference"/>, the completion providers, and
/// <see cref="SidNoteProvider"/> - the same data behind the WPF app's panels.
/// </summary>
public partial class MainWindow
{
    #region Internal Properties

    /// <summary>
    /// The Quick Keys shortcuts, so the tests can tell them apart from the menu-derived ones.
    /// </summary>
    internal IReadOnlyList<KeyGesture> QuickKeyGestures => _quickKeyGestures;

    #endregion

    #region Private Fields

    private readonly List<KeyGesture> _quickKeyGestures = new();

    #endregion

    #region Private Methods - Panel Switching

    private void ActivityQuickKeys_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleRightPanel("QuickKeys");
    private void ActivityPetscii_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleRightPanel("Petscii");
    private void ActivityBasicKeywords_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleRightPanel("BasicKeywords");
    private void ActivityAsmKeywords_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleRightPanel("AsmKeywords");
    private void ActivityMusicNotes_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleRightPanel("MusicNotes");

    // Columns 4 and 5 are the right splitter and panel; 6 is the activity bar, which stays.
    private void ApplyRightPanelLayout()
    {
        var column = MainGrid.ColumnDefinitions[5];
        if (ViewModel.IsRightPanelOpen)
        {
            column.Width = new GridLength(_rightPanelWidth);
            column.MinWidth = 120;
            MainGrid.ColumnDefinitions[4].Width = new GridLength(4);
        }
        else
        {
            if (column.Width.IsAbsolute && column.Width.Value > 0) _rightPanelWidth = column.Width.Value;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
            MainGrid.ColumnDefinitions[4].Width = new GridLength(0);
        }
    }

    #endregion

    #region Private Methods - Quick Keys Shortcuts

    // Ctrl+1-8, Ctrl+Shift+1-8, Ctrl+Shift+Alt+1-8, and Shift+F1-F8 insert the PETSCII control
    // character on the matching Quick Keys card, exactly as in the WPF app. These stay on the
    // Control key on macOS too, rather than following the Ctrl -> Cmd convention the menu
    // shortcuts use: Cmd+Shift+3/4/5 are the system's screenshot shortcuts and never reach the
    // app, and Ctrl+digit is free there - which also keeps the cards' "CTRL+1–8" labels true.
    private void AddQuickKeyBindings()
    {
        foreach (var section in PetsciiReference.QuickKeySections)
        {
            foreach (var key in section.Keys)
            {
                var gesture = section.Modifiers switch
                {
                    QuickKeyModifiers.Control => new KeyGesture(Key.D0 + key.KeyNumber, KeyModifiers.Control),
                    QuickKeyModifiers.ControlShift => new KeyGesture(Key.D0 + key.KeyNumber, KeyModifiers.Control | KeyModifiers.Shift),
                    QuickKeyModifiers.ControlShiftAlt => new KeyGesture(Key.D0 + key.KeyNumber, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt),
                    _ => new KeyGesture(Key.F1 + key.KeyNumber - 1, KeyModifiers.Shift),
                };
                char ch = (char)key.Code;
                _quickKeyGestures.Add(gesture);
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = gesture,
                    Command = new AsyncCommand(() => { InsertQuickKey(gesture, ch); return Task.CompletedTask; }),
                });
            }
        }
    }

    // Shift+F3 is both the F3 quick key and, where the menu isn't native (Windows/Linux), Find
    // Previous - the same collision the WPF app has, resolved the same way: while the Find bar is
    // showing, Shift+F3 is Find Previous; otherwise it's the quick key. This binding is added
    // before the menu's fallback bindings, so it has to make that call itself.
    private void InsertQuickKey(KeyGesture gesture, char ch)
    {
        if (FindBar.IsVisible && gesture.Key == Key.F3 && gesture.KeyModifiers == KeyModifiers.Shift)
        {
            FindPrev();
            return;
        }
        InsertSpecialChar(ch);
    }

    // Inserts a PETSCII control character at the caret, replacing any selection, and puts the
    // focus back in the editor - the Quick Keys cards, the PETSCII Reference rows, and their
    // shortcuts all come through here.
    private void InsertSpecialChar(char ch)
    {
        if (ViewModel.ActiveTab == null) return;

        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, ch.ToString());
        int caret = start + 1;
        Editor.CaretOffset = caret;
        Editor.Select(caret, 0);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();

        // Raw PETSCII bytes can satisfy char.IsLetterOrDigit and get swept into the "word" before
        // the caret, coincidentally matching a keyword - a suggestion after a picker insert is
        // never wanted.
        ClearGhostText();
    }

    #endregion

    #region Private Methods - Building The Panels

    private void BuildReferencePanels()
    {
        BuildQuickKeys();
        BuildPetsciiTable();
        BuildKeywordsList(BasicKeywordsListPanel, BasicCompletionProvider.AllItems, BasicCompletionProvider.CategoryOrder);
        BuildKeywordsList(AsmKeywordsListPanel, AsmCompletionProvider.AllItems, AsmCompletionProvider.CategoryOrder);
        BuildMusicNotesTable();
    }

    private TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeight.Bold,
        Background = ThemeBrush("ThemePanelHeaderBg"),
        Foreground = ThemeBrush("ThemePanelHeaderFg"),
        Padding = new Thickness(8, 4, 0, 4),
        Margin = new Thickness(8, 8, 8, 4),
        MinHeight = 22,
    };

    // The PETSCII glyph for a code, through the same private-use mapping the editor uses.
    private static string GlyphFor(int code) => ((char)(0xE000 + PetsciiScreenCodeMap.ToScreenCode((byte)code))).ToString();

    private void BuildQuickKeys()
    {
        QuickKeysPanel.Children.Clear();

        foreach (var section in PetsciiReference.QuickKeySections)
        {
            if (section.Title != null)
                QuickKeysPanel.Children.Add(SectionHeader(section.Title));

            QuickKeysPanel.Children.Add(new TextBlock
            {
                Text = section.ShortcutHint,
                FontSize = 9,
                Foreground = ThemeBrush("ThemeSpecialCharShortcutFg"),
                Margin = new Thickness(8, 0, 8, 4),
            });

            var cards = new WrapPanel { Margin = new Thickness(6, 0) };
            foreach (var key in section.Keys)
            {
                var content = new Grid { Width = 58, Height = 54 };
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = GlyphFor(key.Code),
                    FontFamily = _petsciiFont,
                    FontSize = 20,
                    Foreground = ThemeBrush("ThemeSpecialCharIconFg"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                stack.Children.Add(new TextBlock
                {
                    Text = key.Label,
                    FontSize = 9,
                    Foreground = ThemeBrush("ThemeSpecialCharLabelFg"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                });
                content.Children.Add(stack);
                content.Children.Add(new TextBlock
                {
                    Text = section.Modifiers == QuickKeyModifiers.ShiftFunctionKey ? $"F{key.KeyNumber}" : key.KeyNumber.ToString(),
                    FontSize = 8,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemeBrush("ThemeSpecialCharShortcutFg"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(3, 0, 0, 2),
                });

                var button = new Button { Classes = { "quickKey" }, Content = content };
                ToolTip.SetTip(button, section.Tooltip(key));
                char ch = (char)key.Code;
                button.Click += (_, _) => InsertSpecialChar(ch);
                cards.Children.Add(button);
            }
            QuickKeysPanel.Children.Add(cards);
        }
    }

    private void BuildPetsciiTable()
    {
        PetsciiTablePanel.Children.Clear();

        var labelBg = ThemeBrush("ThemePanelHeaderBg");
        var labelFg = ThemeBrush("ThemePanelHeaderFg");
        var glyphFg = ThemeBrush("ThemePetsciiGlyphFg");
        var hoverBg = ThemeBrush("ThemePetsciiRowHoverBg");
        var rowBg = ThemeBrush("ThemePetsciiRowEvenBg");
        var separator = ThemeBrush("ThemePetsciiSeparator");
        var headerBg = ThemeBrush("ThemePetsciiHeaderBg");
        var codeFg = ThemeBrush("ThemePetsciiCodeFg");
        var noteBg = ThemeBrush("ThemePetsciiNoteBg");

        Border Row(Control? printElement, string codeText, IBrush background, int? insertCode)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,42") };
            if (printElement != null)
                grid.Children.Add(printElement);
            var codeBlock = new TextBlock
            {
                Text = codeText,
                FontSize = 10,
                Foreground = codeFg,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(codeBlock, 1);
            grid.Children.Add(codeBlock);

            var row = new Border
            {
                Background = background,
                BorderBrush = separator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 24,
                Child = grid,
            };
            if (insertCode is { } code)
            {
                char ch = (char)code;
                row.Cursor = new Cursor(StandardCursorType.Hand);
                row.PointerPressed += (_, _) => InsertSpecialChar(ch);
                row.PointerEntered += (_, _) => row.Background = hoverBg;
                row.PointerExited += (_, _) => row.Background = background;
            }
            return row;
        }

        // Header row.
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,42") };
        header.Children.Add(new TextBlock
        {
            Text = "PRINT", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = labelFg,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        });
        var chrs = new TextBlock
        {
            Text = "CHR$", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = labelFg,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(chrs, 1);
        header.Children.Add(chrs);
        PetsciiTablePanel.Children.Add(new Border
        {
            Background = headerBg, BorderBrush = separator, BorderThickness = new Thickness(0, 0, 0, 1), MinHeight = 22, Child = header,
        });

        for (int code = 0; code <= PetsciiReference.LastListedCode; code++)
        {
            Control? printElement;
            int? insertCode = code;
            switch (PetsciiReference.Classify(code))
            {
                case PetsciiCodeKind.Undefined:
                    printElement = null;
                    insertCode = null;
                    break;
                case PetsciiCodeKind.Control:
                    printElement = new Border
                    {
                        Background = labelBg,
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 2, 4, 2),
                        Child = new TextBlock { Text = PetsciiReference.ControlLabel(code), Foreground = labelFg, FontSize = 9, FontWeight = FontWeight.SemiBold },
                    };
                    break;
                default:
                    printElement = new TextBlock
                    {
                        Text = GlyphFor(code),
                        FontFamily = _petsciiFont,
                        FontSize = 14,
                        Foreground = glyphFg,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 2, 4, 2),
                    };
                    break;
            }
            PetsciiTablePanel.Children.Add(Row(printElement, code.ToString(), rowBg, insertCode));
        }

        foreach (string note in PetsciiReference.FooterNotes)
        {
            PetsciiTablePanel.Children.Add(new Border
            {
                Background = noteBg,
                BorderBrush = separator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock { Text = note, FontSize = 9, Foreground = codeFg, Margin = new Thickness(6, 3, 4, 3), TextWrapping = TextWrapping.Wrap },
            });
        }
    }

    // A reference list (BASIC Keywords or ASM Mnemonics): category headers, then name and
    // description rows, from the same completion tables the editor's completion uses.
    private void BuildKeywordsList(StackPanel target, IReadOnlyList<KeywordCompletionItem> items, IReadOnlyList<string> categoryOrder)
    {
        target.Children.Clear();
        var nameFg = ThemeBrush("ThemeFileFg");
        var descriptionFg = ThemeBrush("ThemeSpecialCharShortcutFg");
        var byCategory = items.ToLookup(i => i.Category);

        foreach (string category in categoryOrder)
        {
            target.Children.Add(SectionHeader(category.ToUpperInvariant()));
            foreach (var item in byCategory[category].OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase))
            {
                var row = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };
                row.Children.Add(new TextBlock { Text = item.Text, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = nameFg });
                row.Children.Add(new TextBlock { Text = item.Description, FontSize = 9, TextWrapping = TextWrapping.Wrap, Foreground = descriptionFg, Margin = new Thickness(0, 1, 0, 0) });
                target.Children.Add(row);
            }
        }
    }

    private void BuildMusicNotesTable()
    {
        MusicNotesGrid.RowDefinitions.Clear();
        MusicNotesGrid.Children.Clear();

        var separator = ThemeBrush("ThemePetsciiSeparator");
        var rowBg = ThemeBrush("ThemePetsciiRowEvenBg");
        var cellFg = ThemeBrush("ThemePetsciiCodeFg");
        var labelBg = ThemeBrush("ThemePanelHeaderBg");
        var labelFg = ThemeBrush("ThemePanelHeaderFg");

        MusicNotesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        MusicNotesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void AddHeaderCell(string text, int column, int row, int columnSpan)
        {
            var cell = new Border
            {
                Background = labelBg,
                BorderBrush = separator,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = text, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = labelFg,
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4),
                },
            };
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            Grid.SetColumnSpan(cell, columnSpan);
            MusicNotesGrid.Children.Add(cell);
        }

        AddHeaderCell("MUSICAL NOTE", 0, 0, 2);
        AddHeaderCell("NOTE", 0, 1, 1);
        AddHeaderCell("OCTAVE", 1, 1, 1);
        AddHeaderCell("OSCILLATOR FREQ (NTSC)", 2, 0, 3);
        AddHeaderCell("OSCILLATOR FREQ (PAL)", 5, 0, 3);
        AddHeaderCell("DECIMAL", 2, 1, 1);
        AddHeaderCell("HI", 3, 1, 1);
        AddHeaderCell("LOW", 4, 1, 1);
        AddHeaderCell("DECIMAL", 5, 1, 1);
        AddHeaderCell("HI", 6, 1, 1);
        AddHeaderCell("LOW", 7, 1, 1);

        int gridRow = 2;
        foreach (var note in SidNoteProvider.AllNotes)
        {
            MusicNotesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            void AddCell(string text, int column)
            {
                var cell = new Border
                {
                    Background = rowBg,
                    BorderBrush = separator,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock { Text = text, FontSize = 9, Foreground = cellFg, TextAlignment = TextAlignment.Center, Margin = new Thickness(4, 2) },
                };
                Grid.SetColumn(cell, column);
                Grid.SetRow(cell, gridRow);
                MusicNotesGrid.Children.Add(cell);
            }

            AddCell(note.Note.ToString(), 0);
            AddCell(note.Octave, 1);
            AddCell(note.DecimalNtsc.ToString(), 2);
            AddCell(note.HiNtsc.ToString(), 3);
            AddCell(note.LowNtsc.ToString(), 4);
            AddCell(note.DecimalPal?.ToString() ?? "", 5);
            AddCell(note.HiPal?.ToString() ?? "", 6);
            AddCell(note.LowPal?.ToString() ?? "", 7);
            gridRow++;
        }
    }

    #endregion
}
