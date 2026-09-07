// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReadyCode.Settings;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The subset of Preferences the cross-platform front end supports so far. Edits are applied to
/// the shared <see cref="AppSettings"/> only when OK is pressed.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow() : this(new AppSettings()) { }

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        VicePathBox.Text = settings.ViceEmulatorPath;
        MonitorHostBox.Text = settings.ViceMonitorHost;
        MonitorPortBox.Value = settings.ViceMonitorPort;
        BringToForegroundBox.IsChecked = settings.ViceBringToForeground;
        MinifyOnTransferBox.IsChecked = settings.MinifyOnTransfer;
        FontSizeBox.Value = settings.EditorFontSize;
        LineNumberPaddingBox.Value = settings.LineNumberPadding;
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
        _settings.ViceEmulatorPath = VicePathBox.Text?.Trim() ?? "";
        _settings.ViceMonitorHost = string.IsNullOrWhiteSpace(MonitorHostBox.Text) ? "127.0.0.1" : MonitorHostBox.Text.Trim();
        _settings.ViceMonitorPort = (int)(MonitorPortBox.Value ?? 6502);
        _settings.ViceBringToForeground = BringToForegroundBox.IsChecked == true;
        _settings.MinifyOnTransfer = MinifyOnTransferBox.IsChecked == true;
        _settings.EditorFontSize = (int)(FontSizeBox.Value ?? 12);
        _settings.LineNumberPadding = (int)(LineNumberPaddingBox.Value ?? 4);
        Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
