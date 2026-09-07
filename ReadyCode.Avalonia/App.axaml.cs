// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;

namespace ReadyCode.Avalonia;

/// <summary>
/// The Avalonia application: loads the app-wide styles and opens the main window.
/// </summary>
public partial class App : Application
{
    #region Public Methods

    /// <summary>
    /// Loads the application's XAML.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ConfigureMacOSApplicationMenu();
    }

    /// <summary>
    /// Installs the UI-thread hooks the shared models need, then opens the main window.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // Shared models raise work from background threads (the debug session's read loop) or need
        // to defer until after a layout pass (disk-image expansion in the explorer tree). Both are
        // installed here so ReadyCode.Core stays free of any UI-framework reference.
        MainViewModel.RunOnUiThread = action =>
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Invoke(action);
        };
        ReadyCode.Models.FileTreeItem.DeferToUiThread = action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }

    #endregion

    #region Private Methods

    // On macOS, About and Preferences belong in the application menu rather than the menu bar.
    // Setting a NativeMenu on the Application is what puts them there; it also replaces the
    // default "About Avalonia" item that would otherwise be the only thing in that menu.
    private void ConfigureMacOSApplicationMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var about = new NativeMenuItem { Header = "About READYCode" };
        about.Click += async (_, _) =>
        {
            if (MainWindow is { } window) await window.ShowAboutAsync();
        };

        var preferences = new NativeMenuItem
        {
            Header = "Preferences…",
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
        };
        preferences.Click += async (_, _) =>
        {
            if (MainWindow is { } window) await window.ShowPreferencesAsync();
        };

        var applicationMenu = new NativeMenu { about, new NativeMenuItemSeparator(), preferences };
        NativeMenu.SetMenu(this, applicationMenu);
    }

    // The window is created after Initialize runs, so the menu handlers resolve it on each click
    // rather than capturing it up front.
    private MainWindow? MainWindow =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    #endregion
}
