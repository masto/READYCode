// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// The Start/End address bar above a live-memory disassembly tab. Reports the request through
/// <see cref="DisassembleRequested"/>; the window does the reading and disassembling.
/// </summary>
public partial class DisassemblyToolbarControl : UserControl
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="DisassemblyToolbarControl"/> class.
    /// </summary>
    public DisassemblyToolbarControl()
    {
        InitializeComponent();
        StartAddressBox.KeyDown += AddressBox_KeyDown;
        EndAddressBox.KeyDown += AddressBox_KeyDown;
    }

    #endregion

    #region Public Events

    /// <summary>Occurs when the user asks for the range to be disassembled.</summary>
    public event EventHandler? DisassembleRequested;

    #endregion

    #region Public Properties

    /// <summary>Gets or sets the start address text, e.g. "$0810".</summary>
    public string StartAddressText
    {
        get => StartAddressBox.Text ?? "";
        set => StartAddressBox.Text = value;
    }

    /// <summary>Gets or sets the end address text.</summary>
    public string EndAddressText
    {
        get => EndAddressBox.Text ?? "";
        set => EndAddressBox.Text = value;
    }

    #endregion

    #region Public Methods

    /// <summary>Puts the caret in the start address box with its text selected.</summary>
    public void FocusStartAddress()
    {
        StartAddressBox.Focus();
        StartAddressBox.SelectAll();
    }

    /// <summary>
    /// Parses the two addresses. Hex, with or without a leading '$'.
    /// </summary>
    /// <returns>Whether both parsed and end is not before start; <paramref name="error"/> says why not.</returns>
    public bool TryGetAddressRange(out ushort start, out ushort end, out string? error)
    {
        end = 0;

        if (!TryParseAddress(StartAddressText, out start))
        {
            error = "Invalid start address - use hex, e.g. $0810.";
            return false;
        }

        if (!TryParseAddress(EndAddressText, out end))
        {
            error = "Invalid end address - use hex, e.g. $0810.";
            return false;
        }

        if (end < start)
        {
            error = "End address must be at or after the start address.";
            return false;
        }

        error = null;
        return true;
    }

    #endregion

    #region Private Methods

    private static bool TryParseAddress(string text, out ushort value)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('$')) trimmed = trimmed[1..];
        return ushort.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private void DisassembleButton_Click(object? sender, RoutedEventArgs e) => DisassembleRequested?.Invoke(this, EventArgs.Empty);

    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            DisassembleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion
}
