// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The About box: version, credit to the original Windows application, and the third-party
/// notices this app is obliged to carry (the Pet Me 64 font's license in particular).
/// </summary>
public partial class AboutWindow : Window
{
    #region Private Fields

    private const string OriginalProjectUrl = "https://github.com/jbramwell/READYCode";
    private const string PortProjectUrl = "https://github.com/masto/READYCode";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "";
        // Strip the source-revision suffix the SDK appends to InformationalVersion ("2.3.0+abc123").
        int plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        VersionText.Text = $"Version {version}";
        CopyrightText.Text = $"Copyright © 2026 Moonspace Labs, LLC. Released under the MIT License.";
        PortProjectLink.Content = PortProjectUrl.Replace("https://", "");

        // The window icon doubles as the About box's artwork; it ships as an app asset.
        try
        {
            AppIcon.Source = new Bitmap(AssetLoader.Open(new Uri("avares://ReadyCode.Avalonia/Assets/READYCode.png")));
        }
        catch (Exception)
        {
            // No icon asset - the rest of the dialog is still useful, so show it without one.
            AppIcon.IsVisible = false;
        }
    }

    #endregion

    #region Private Methods

    private async void OriginalProject_Click(object? sender, RoutedEventArgs e) => await OpenUrlAsync(OriginalProjectUrl);

    private async void PortProject_Click(object? sender, RoutedEventArgs e) => await OpenUrlAsync(PortProjectUrl);

    private async void Licenses_Click(object? sender, RoutedEventArgs e)
    {
        string licenses = ReadResource("avares://ReadyCode.Avalonia/Assets/Fonts/LICENSE-PetMe64.txt");
        await MessageDialog.ShowAsync(this, "Third-Party Licenses", licenses);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception)
        {
            // No browser, or the launch was refused - nothing useful to recover with.
        }
    }

    private static string ReadResource(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return "The license text could not be loaded.";
        }
    }

    #endregion
}
