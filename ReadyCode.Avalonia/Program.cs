// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;

namespace ReadyCode.Avalonia;

/// <summary>
/// Application entry point.
/// </summary>
internal static class Program
{
    #region Public Methods

    /// <summary>
    /// Starts the application. Don't use any Avalonia, third-party, or SynchronizationContext-
    /// reliant code before <see cref="BuildAvaloniaApp"/> runs - nothing is initialized yet.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Builds the Avalonia application. Also used by the visual designer, so it must stay a
    /// public parameterless method with this exact name.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    #endregion
}
