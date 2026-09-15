// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Avalonia;
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

    /// <summary>
    /// Whether any pixel along a horizontal strip of <paramref name="frame"/> differs from the
    /// strip's last pixel - i.e. something is drawn there on an otherwise flat background.
    /// </summary>
    /// <param name="frame">The captured frame.</param>
    /// <param name="x">Left edge of the strip.</param>
    /// <param name="y">Row to sample.</param>
    /// <param name="width">Width of the strip; its right end must be plain background.</param>
    public static bool HasInk(Bitmap frame, int x, int y, int width)
    {
        width = Math.Min(width, frame.PixelSize.Width - x);
        if (width <= 0 || y < 0 || y >= frame.PixelSize.Height) return false;

        var pixels = new uint[width];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(x, y, width, 1), handle.AddrOfPinnedObject(), width * 4, width * 4);
        }
        finally { handle.Free(); }

        uint background = pixels[^1];
        return pixels.Any(p => p != background);
    }

    #endregion
}
