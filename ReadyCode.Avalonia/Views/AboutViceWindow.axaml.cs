// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using ReadyCode.Vice;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// VICE > About VICE: the version reported by VICE's binary monitor and the emulator path in
/// use - the VICE counterpart of <see cref="AboutC64UWindow"/>.
/// </summary>
public partial class AboutViceWindow : Window
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViceWindow"/> class. Exists for the
    /// XAML designer; the app always uses the overload below.
    /// </summary>
    public AboutViceWindow() : this(new ViceInfo(), "") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViceWindow"/> class.
    /// </summary>
    /// <param name="info">The version information VICE reported.</param>
    /// <param name="emulatorPath">Full path to the VICE emulator executable.</param>
    public AboutViceWindow(ViceInfo info, string emulatorPath)
    {
        InitializeComponent();
        VersionText.Text = info.Version;
        EmulatorPathText.Text = emulatorPath;
    }

    #endregion

    #region Private Methods

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    #endregion
}
