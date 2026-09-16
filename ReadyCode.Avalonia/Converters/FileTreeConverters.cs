// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
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

    #endregion

    #region Public Properties

    /// <summary>Maps a tree item to its icon geometry (folder, disk image, or file).</summary>
    public static readonly IValueConverter Icon = new FuncValueConverter<FileTreeItem?, Geometry?>(item =>
        item == null ? null : item.IsFolder ? _folder : item.IsDiskImage ? _disk : _file);

    /// <summary>Maps a C64U explorer item to its icon geometry (folder, disk image, or file).</summary>
    public static readonly IValueConverter C64UIcon = new FuncValueConverter<C64UFileItem?, Geometry?>(item =>
        item == null ? null : item.IsFolder ? _folder : item.IsDiskImage ? _disk : _file);

    /// <summary>
    /// Maps a file kind to the background brush of its type badge - the theme's
    /// <c>ThemeBadgeXxxBg</c> brush, which stays current across theme changes on its own (see
    /// <see cref="Themes.AppTheme.Apply"/>).
    /// </summary>
    public static readonly IValueConverter BadgeBrush = new FuncValueConverter<C64UFileKind, IBrush?>(kind => ThemeBrush(BadgeKey(kind, "Bg")));

    /// <summary>Maps a file kind to the text brush of its type badge (<c>ThemeBadgeXxxFg</c>).</summary>
    public static readonly IValueConverter BadgeForegroundBrush = new FuncValueConverter<C64UFileKind, IBrush?>(kind => ThemeBrush(BadgeKey(kind, "Fg")));

    /// <summary>Formats a debug variable's value.</summary>
    public static readonly IValueConverter VariableValue = new FuncValueConverter<BasicVariable?, string>(variable =>
        variable == null ? "" : MainViewModel.FormatVariableValue(variable));

    /// <summary>True when the item has a badge to show.</summary>
    public static readonly IValueConverter HasBadge = new FuncValueConverter<string?, bool>(badge => !string.IsNullOrEmpty(badge));

    /// <summary>The row background while the item is a drag's drop target - the amber WPF uses.</summary>
    public static readonly IValueConverter DropTargetBrush = new FuncValueConverter<bool, IBrush?>(isTarget =>
        isTarget ? new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC1, 0x07)) : Brushes.Transparent);

    /// <summary>
    /// The name's brush for the file pending as the left side of a File Compare: the accent
    /// color; unset (so the style's own color applies) otherwise. Values: the item, then
    /// <see cref="MainViewModel.PendingCompareFile"/>.
    /// </summary>
    public static readonly IMultiValueConverter PendingCompareBrush = new FuncMultiValueConverter<object?, object?>(values =>
        IsPendingCompare(values.ToList()) ? ThemeBrush("ThemeSettingsAccent") : AvaloniaProperty.UnsetValue);

    /// <summary>Bold for the pending File Compare file; unset otherwise. Same values as <see cref="PendingCompareBrush"/>.</summary>
    public static readonly IMultiValueConverter PendingCompareWeight = new FuncMultiValueConverter<object?, object?>(values =>
        IsPendingCompare(values.ToList()) ? FontWeight.Bold : AvaloniaProperty.UnsetValue);

    /// <summary>
    /// The name's brush for a disk image mounted in one of the C64 Ultimate's drives: the
    /// accent color; unset otherwise. Values: the item's path, then drive A's and drive B's
    /// mounted image path.
    /// </summary>
    public static readonly IMultiValueConverter MountedBrush = new FuncMultiValueConverter<object?, object?>(values =>
        IsMounted(values.ToList()) ? ThemeBrush("ThemeSettingsAccent") : AvaloniaProperty.UnsetValue);

    /// <summary>Bold for a mounted disk image; unset otherwise. Same values as <see cref="MountedBrush"/>.</summary>
    public static readonly IMultiValueConverter MountedWeight = new FuncMultiValueConverter<object?, object?>(values =>
        IsMounted(values.ToList()) ? FontWeight.Bold : AvaloniaProperty.UnsetValue);

    #endregion

    #region Private Methods

    // Same key names the WPF app's badges use. Kinds with no badge of their own (folders, plain
    // files) borrow the disk image's neutral gray.
    private static string BadgeKey(C64UFileKind kind, string suffix) => "ThemeBadge" + kind switch
    {
        C64UFileKind.D64 or C64UFileKind.D81 => "Disk",
        C64UFileKind.Ml => "Ml",
        C64UFileKind.Prg => "Prg",
        C64UFileKind.Asm => "Asm",
        C64UFileKind.Bas => "Bas",
        _ => "Disk",
    } + suffix;

    private static bool IsPendingCompare(List<object?> values)
    {
        if (values.Count < 2 || values[1] is not ComparableFileRef pending) return false;
        var candidate = values[0] switch
        {
            FileTreeItem local => ComparableFileRef.FromLocal(local),
            C64UFileItem remote => ComparableFileRef.FromC64U(remote),
            _ => null,
        };
        return candidate != null && pending.IsSameFile(candidate);
    }

    private static bool IsMounted(List<object?> values)
    {
        if (values.Count < 3 || values[0] is not string path || path.Length == 0) return false;
        return string.Equals(path, values[1] as string, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, values[2] as string, StringComparison.OrdinalIgnoreCase);
    }

    private static IBrush? ThemeBrush(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true ? value as IBrush : null;

    #endregion
}
