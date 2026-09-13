// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReadyCode.Avalonia.Themes;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// The themes are read straight from the WPF app's theme files, so these mostly guard the
/// contract between the two: every theme exists, defines the same keys, and applying one
/// actually reaches the controls bound to it.
/// </summary>
public class ThemeTests
{
    #region Public Methods

    [Fact]
    public void EveryTheme_LoadsFromTheSharedWpfFiles_WithTheSameKeys()
    {
        var light = AppTheme.Load(AppTheme.Light);
        Assert.True(light.Count > 50, $"Light theme has only {light.Count} colors - is the WPF file still linked?");
        Assert.Contains("ThemeEditorBg", light.Keys);
        Assert.Contains("ThemeStatusBarBg", light.Keys);

        foreach (string name in AppTheme.Names)
        {
            var theme = AppTheme.Load(name);
            Assert.Equal(light.Keys.OrderBy(k => k), theme.Keys.OrderBy(k => k));
        }

        // Sanity-check the values are the WPF ones, not accidentally identical across themes.
        Assert.NotEqual(light["ThemeEditorBg"], AppTheme.Load(AppTheme.Dark)["ThemeEditorBg"]);
    }

    [Fact]
    public void Normalize_FallsBackToLight_LikeTheWpfApp()
    {
        Assert.Equal(AppTheme.Light, AppTheme.Normalize(null));
        Assert.Equal(AppTheme.Light, AppTheme.Normalize("Neon"));
        Assert.Equal(AppTheme.Dark, AppTheme.Normalize("Dark"));
        Assert.Equal(AppTheme.C64, AppTheme.Normalize("C64"));
    }

    [AvaloniaFact]
    public void Apply_UpdatesDynamicResourceConsumers_AndTheControlVariant()
    {
        var app = Application.Current!;
        try
        {
            AppTheme.Apply(app, AppTheme.Light);
            var window = new Window();
            var border = new Border();
            // The code-side equivalent of {DynamicResource ThemeEditorBg}.
            border.Bind(Border.BackgroundProperty, border.GetResourceObservable("ThemeEditorBg"));
            window.Content = border;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(AppTheme.Load(AppTheme.Light)["ThemeEditorBg"], ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);

            AppTheme.Apply(app, AppTheme.C64);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(AppTheme.Load(AppTheme.C64)["ThemeEditorBg"], ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            Assert.Equal(AppTheme.C64, AppTheme.Current);
        }
        finally
        {
            AppTheme.Apply(app, AppTheme.Light);
        }
    }

    [AvaloniaFact]
    public void MainWindow_RendersInEveryTheme()
    {
        var app = Application.Current!;
        try
        {
            foreach (string name in AppTheme.Names)
            {
                AppTheme.Apply(app, name);
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
                window.Show();
                vm.ActiveTab!.Document.Text = "10 PRINT \"HELLO\"\n20 REM A COMMENT\n30 GOTO 10";
                Dispatcher.UIThread.RunJobs();
                RenderCapture.Save(window, $"main-window-theme-{name.ToLowerInvariant()}.png");
                window.Close();
            }
        }
        finally
        {
            AppTheme.Apply(app, AppTheme.Light);
        }
    }

    #endregion
}
