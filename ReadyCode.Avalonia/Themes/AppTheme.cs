// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ReadyCode.Avalonia.Themes;

/// <summary>
/// The application's color themes - Light, Dark, and Commodore 64 - read from the WPF app's own
/// theme files (<c>ReadyCode/Resources/Themes/*Theme.xaml</c>, linked into this assembly as
/// embedded resources) so both front ends share one definition of every color. Those files are
/// plain resource dictionaries of <c>x:Key</c> to hex color and nothing else, so they are read
/// as XML here rather than as XAML: no WPF dependency, and no translated copy to keep in sync.
/// Every brush lands in <see cref="Application.Resources"/> under its WPF key, so the XAML on
/// this side uses the same <c>{DynamicResource ThemeXxx}</c> references the WPF XAML does.
/// </summary>
public static class AppTheme
{
    #region Public Fields

    /// <summary>The default theme, and what an unrecognized <see cref="ReadyCode.Settings.AppSettings.Theme"/> falls back to.</summary>
    public const string Light = "Light";

    /// <summary>The dark theme.</summary>
    public const string Dark = "Dark";

    /// <summary>The Commodore 64 theme: the machine's own blue-on-blue palette.</summary>
    public const string C64 = "C64";

    /// <summary>Every theme, in the order the Preferences dialog lists them.</summary>
    public static readonly IReadOnlyList<string> Names = [Light, Dark, C64];

    #endregion

    #region Private Fields

    private static readonly XNamespace _xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    #endregion

    #region Public Properties

    /// <summary>Gets the name of the theme most recently applied, or <see cref="Light"/> before any was.</summary>
    public static string Current { get; private set; } = Light;

    #endregion

    #region Public Methods

    /// <summary>
    /// Maps a persisted theme name to one of <see cref="Names"/>, the same way the WPF app does:
    /// anything unrecognized is <see cref="Light"/>.
    /// </summary>
    public static string Normalize(string? theme) => theme switch
    {
        Dark => Dark,
        C64 => C64,
        _ => Light,
    };

    /// <summary>
    /// Reads a theme's colors, keyed by their WPF resource key (e.g. <c>ThemeEditorBg</c>).
    /// </summary>
    /// <param name="theme">One of <see cref="Names"/>.</param>
    public static IReadOnlyDictionary<string, Color> Load(string theme)
    {
        string name = $"Themes/{Normalize(theme)}Theme.xaml";
        using var stream = typeof(AppTheme).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Theme resource '{name}' is not embedded in this assembly.");

        var document = XDocument.Load(stream);
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var element in document.Root?.Elements() ?? [])
        {
            if (element.Name.LocalName != "SolidColorBrush") continue;
            string? key = element.Attribute(_xamlNamespace + "Key")?.Value;
            string? value = element.Attribute("Color")?.Value;
            if (key == null || value == null) continue;
            colors[key] = Color.Parse(value);
        }

        return colors;
    }

    /// <summary>
    /// Makes <paramref name="theme"/> the active theme: switches the built-in control styles to
    /// their light or dark variant (Dark and C64 both sit on the dark variant, so text boxes,
    /// scroll bars, and the like match the chrome), then puts every one of the theme's colors
    /// into <see cref="Application.Resources"/> under its WPF key. Each key holds one mutable
    /// <see cref="SolidColorBrush"/> for the life of the app and a later theme just changes its
    /// color, so <c>{DynamicResource}</c> references and code that fetched a brush once (the
    /// editor's colorizers, the explorer's badge converter) both follow along with nothing to
    /// re-wire.
    /// </summary>
    public static void Apply(Application app, string? theme)
    {
        string name = Normalize(theme);
        app.RequestedThemeVariant = name == Light ? ThemeVariant.Light : ThemeVariant.Dark;

        foreach (var (key, color) in Load(name))
        {
            if (app.Resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
                brush.Color = color;
            else
                app.Resources[key] = new SolidColorBrush(color);
        }

        Current = name;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs after a theme has been applied, for consumers that cache derived values (a pen
    /// made from a theme color, say) rather than holding the brush itself.
    /// </summary>
    public static event EventHandler? Changed;

    #endregion
}
