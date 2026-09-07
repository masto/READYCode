// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Data.Converters;
using Avalonia.Media;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Debugger;
using ReadyCode.Models;

namespace ReadyCode.Avalonia.Converters;

/// <summary>
/// Value converters used by the folder explorer's item template.
/// </summary>
public static class FileTreeConverters
{
    #region Private Fields

    private static readonly Geometry _folder = Geometry.Parse("M2 4a1 1 0 0 1 1-1h5l2 2h7a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z");
    private static readonly Geometry _file = Geometry.Parse("M5 2h7l4 4v12H5z");
    private static readonly Geometry _disk = Geometry.Parse("M3 3h14v14H3z");

    private static readonly IBrush _badgeDefault = new SolidColorBrush(Color.Parse("#D8D8D8"));
    private static readonly IBrush _badgeDisk = new SolidColorBrush(Color.Parse("#C0C0C0"));
    private static readonly IBrush _badgeMl = new SolidColorBrush(Color.Parse("#F7DACE"));
    private static readonly IBrush _badgePrg = new SolidColorBrush(Color.Parse("#28B08E"));
    private static readonly IBrush _badgeAsm = new SolidColorBrush(Color.Parse("#B9A0C9"));
    private static readonly IBrush _badgeBas = new SolidColorBrush(Color.Parse("#EB8865"));

    #endregion

    #region Public Properties

    /// <summary>Maps a tree item to its icon geometry (folder, disk image, or file).</summary>
    public static readonly IValueConverter Icon = new FuncValueConverter<FileTreeItem?, Geometry?>(item =>
        item == null ? null : item.IsFolder ? _folder : item.IsDiskImage ? _disk : _file);

    /// <summary>Maps a file kind to the background brush of its type badge.</summary>
    public static readonly IValueConverter BadgeBrush = new FuncValueConverter<C64UFileKind, IBrush>(kind => kind switch
    {
        C64UFileKind.D64 or C64UFileKind.D81 => _badgeDisk,
        C64UFileKind.Ml => _badgeMl,
        C64UFileKind.Prg => _badgePrg,
        C64UFileKind.Asm => _badgeAsm,
        C64UFileKind.Bas => _badgeBas,
        _ => _badgeDefault,
    });

    /// <summary>Formats a debug variable's value.</summary>
    public static readonly IValueConverter VariableValue = new FuncValueConverter<BasicVariable?, string>(variable =>
        variable == null ? "" : MainViewModel.FormatVariableValue(variable));

    /// <summary>True when the item has a badge to show.</summary>
    public static readonly IValueConverter HasBadge = new FuncValueConverter<string?, bool>(badge => !string.IsNullOrEmpty(badge));

    #endregion
}
