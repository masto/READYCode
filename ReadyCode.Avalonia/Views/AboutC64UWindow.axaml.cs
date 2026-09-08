// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using ReadyCode.C64U;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Shows the information reported by a Commodore 64 Ultimate's GET /v1/info: product,
/// firmware/FPGA/core versions, hostname, unique id, and any errors the device itself reported.
/// </summary>
public partial class AboutC64UWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutC64UWindow"/> class. Exists for the
    /// XAML designer; the app always uses the overload below.
    /// </summary>
    public AboutC64UWindow() : this(new C64UInfo()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutC64UWindow"/> class.
    /// </summary>
    /// <param name="info">The device information to show.</param>
    public AboutC64UWindow(C64UInfo info)
    {
        InitializeComponent();

        ProductText.Text = string.IsNullOrEmpty(info.Product) ? "Commodore 64 Ultimate" : info.Product;
        FirmwareVersionText.Text = info.FirmwareVersion ?? "";
        FpgaVersionText.Text = info.FpgaVersion ?? "";
        HostnameText.Text = info.Hostname ?? "";

        bool hasCoreVersion = !string.IsNullOrEmpty(info.CoreVersion);
        CoreVersionLabel.IsVisible = hasCoreVersion;
        CoreVersionText.IsVisible = hasCoreVersion;
        CoreVersionText.Text = info.CoreVersion ?? "";

        bool hasUniqueId = !string.IsNullOrEmpty(info.UniqueId);
        UniqueIdLabel.IsVisible = hasUniqueId;
        UniqueIdText.IsVisible = hasUniqueId;
        UniqueIdText.Text = info.UniqueId ?? "";

        string errorText = info.Errors is { Count: > 0 } errors ? string.Join(Environment.NewLine, errors) : "";
        ErrorText.Text = errorText;
        ErrorText.IsVisible = errorText.Length > 0;
    }

    #endregion

    #region Private Methods

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    #endregion
}
