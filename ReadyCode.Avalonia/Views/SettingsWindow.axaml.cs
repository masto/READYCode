// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReadyCode.Settings;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The Preferences dialog. Edits are copied into the shared <see cref="AppSettings"/> only when
/// OK is pressed; the caller saves and applies them.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow() : this(new AppSettings()) { }

    public SettingsWindow(AppSettings s)
    {
        _settings = s;
        InitializeComponent();

        RestoreTabsYes.IsChecked = s.RestoreOpenTabsOnStartup;
        RestoreTabsNo.IsChecked = !s.RestoreOpenTabsOnStartup;
        ShowStatusBarBox.IsChecked = s.ShowStatusBar;

        FontSizeBox.Value = s.EditorFontSize;
        WordWrapBox.IsChecked = s.WordWrap;
        ShowColumnGuideBox.IsChecked = s.ShowColumnGuide;

        LineNumberPaddingBox.Value = s.LineNumberPadding;
        AutoNumberBox.IsChecked = s.AutoNumberLines;
        AutoNumberIncrementBox.Value = s.AutoNumberIncrement;
        BasicColumnGuideBox.Value = s.BasicColumnGuideColumn;
        EnableLintingBox.IsChecked = s.EnableLinting;
        EnableCodeFoldingBox.IsChecked = s.EnableCodeFolding;
        MinifyOnTransferBox.IsChecked = s.MinifyOnTransfer;
        MinifyRemoveWhitespaceBox.IsChecked = s.MinifyRemoveWhitespace;
        MinifyReplaceZeroWithDotBox.IsChecked = s.MinifyReplaceZeroWithDot;
        MinifyUseScientificNotationBox.IsChecked = s.MinifyUseScientificNotation;
        MinifyRemoveCommentsBox.IsChecked = s.MinifyRemoveComments;
        MinifySimplifyNextBox.IsChecked = s.MinifySimplifyNext;
        MinifyRenumberLinesBox.IsChecked = s.MinifyRenumberLines;
        MinifyOptions.IsEnabled = s.MinifyOnTransfer;
        MinifyOnTransferBox.IsCheckedChanged += (_, _) => MinifyOptions.IsEnabled = MinifyOnTransferBox.IsChecked == true;

        AsmMnemonicIndentBox.Value = s.AsmMnemonicIndentColumn;
        AsmCommentAlignBox.Value = s.AsmCommentAlignColumn;
        AsmColumnGuideBox.Value = s.AsmColumnGuideColumn;
        AsmAutoIndentBox.IsChecked = s.AsmAutoIndent;
        AsmEnableCodeFoldingBox.IsChecked = s.AsmEnableCodeFolding;
        AsmOutputAuto.IsChecked = s.AsmOutputMode != "Standalone";
        AsmOutputStandalone.IsChecked = s.AsmOutputMode == "Standalone";
        AsmOriginBox.Text = s.AsmDefaultOriginAddress.ToString("X4");

        VicePathBox.Text = s.ViceEmulatorPath;
        MonitorHostBox.Text = s.ViceMonitorHost;
        MonitorPortBox.Value = s.ViceMonitorPort;
        BringToForegroundBox.IsChecked = s.ViceBringToForeground;
    }

    /// <summary>Gets whether the user pressed OK (settings were applied).</summary>
    public bool Accepted { get; private set; }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the VICE emulator executable",
            AllowMultiple = false,
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            VicePathBox.Text = path;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var s = _settings;

        s.RestoreOpenTabsOnStartup = RestoreTabsYes.IsChecked == true;
        s.ShowStatusBar = ShowStatusBarBox.IsChecked == true;

        s.EditorFontSize = Int(FontSizeBox, s.EditorFontSize);
        s.WordWrap = WordWrapBox.IsChecked == true;
        s.ShowColumnGuide = ShowColumnGuideBox.IsChecked == true;

        s.LineNumberPadding = Int(LineNumberPaddingBox, s.LineNumberPadding);
        s.AutoNumberLines = AutoNumberBox.IsChecked == true;
        s.AutoNumberIncrement = Int(AutoNumberIncrementBox, s.AutoNumberIncrement);
        s.BasicColumnGuideColumn = Int(BasicColumnGuideBox, s.BasicColumnGuideColumn);
        s.EnableLinting = EnableLintingBox.IsChecked == true;
        s.EnableCodeFolding = EnableCodeFoldingBox.IsChecked == true;
        s.MinifyOnTransfer = MinifyOnTransferBox.IsChecked == true;
        s.MinifyRemoveWhitespace = MinifyRemoveWhitespaceBox.IsChecked == true;
        s.MinifyReplaceZeroWithDot = MinifyReplaceZeroWithDotBox.IsChecked == true;
        s.MinifyUseScientificNotation = MinifyUseScientificNotationBox.IsChecked == true;
        s.MinifyRemoveComments = MinifyRemoveCommentsBox.IsChecked == true;
        s.MinifySimplifyNext = MinifySimplifyNextBox.IsChecked == true;
        s.MinifyRenumberLines = MinifyRenumberLinesBox.IsChecked == true;

        s.AsmMnemonicIndentColumn = Int(AsmMnemonicIndentBox, s.AsmMnemonicIndentColumn);
        s.AsmCommentAlignColumn = Int(AsmCommentAlignBox, s.AsmCommentAlignColumn);
        s.AsmColumnGuideColumn = Int(AsmColumnGuideBox, s.AsmColumnGuideColumn);
        s.AsmAutoIndent = AsmAutoIndentBox.IsChecked == true;
        s.AsmEnableCodeFolding = AsmEnableCodeFoldingBox.IsChecked == true;
        s.AsmOutputMode = AsmOutputStandalone.IsChecked == true ? "Standalone" : "Auto";
        string origin = (AsmOriginBox.Text ?? "").Trim().TrimStart('$');
        if (int.TryParse(origin, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int originValue) && originValue is >= 0 and <= 0xFFFF)
            s.AsmDefaultOriginAddress = originValue;

        s.ViceEmulatorPath = VicePathBox.Text?.Trim() ?? "";
        s.ViceMonitorHost = string.IsNullOrWhiteSpace(MonitorHostBox.Text) ? "127.0.0.1" : MonitorHostBox.Text.Trim();
        s.ViceMonitorPort = Int(MonitorPortBox, s.ViceMonitorPort);
        s.ViceBringToForeground = BringToForegroundBox.IsChecked == true;

        Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private static int Int(NumericUpDown box, int fallback) => box.Value is { } v ? (int)v : fallback;
}
