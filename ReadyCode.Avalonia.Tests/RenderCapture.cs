// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Saves the frames the headless UI tests render, so font, highlighting, and layout changes can
/// be eyeballed without a screen. Set READYCODE_RENDER_DIR to a directory to enable it; with the
/// variable unset the tests still render (proving the window lays out and draws) but write nothing.
/// </summary>
internal static class RenderCapture
{
    #region Private Fields

    private static readonly string? _renderDirectory = Environment.GetEnvironmentVariable("READYCODE_RENDER_DIR");

    #endregion

    #region Public Methods

    /// <summary>
    /// Renders <paramref name="window"/> and writes the frame as a PNG, if capturing is enabled.
    /// </summary>
    /// <param name="window">The window to capture.</param>
    /// <param name="fileName">File name to write within the capture directory.</param>
    /// <returns>The captured frame, so callers can assert on it.</returns>
    public static Bitmap Save(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"{window.GetType().Name} rendered no frame.");

        if (!string.IsNullOrEmpty(_renderDirectory))
        {
            Directory.CreateDirectory(_renderDirectory);
            frame.Save(Path.Combine(_renderDirectory, fileName), new PngBitmapEncoderOptions());
        }

        return frame;
    }

    #endregion
}
