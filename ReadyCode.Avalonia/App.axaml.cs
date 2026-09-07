// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
}
