// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Tests for the menu. It is declared once as a <see cref="NativeMenu"/>, which macOS shows in the
/// system menu bar and every other platform renders inside the window through
/// <see cref="NativeMenuBar"/>; these cover the in-window path, which is what Windows and Linux use.
/// </summary>
public class MenuTests
{
    #region Public Methods

    [AvaloniaFact]
    public void MenuIsDeclaredOnce_AndCoversEveryTopLevelSection()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var menu = NativeMenu.GetMenu(window);
        Assert.NotNull(menu);

        var headers = menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList();
        Assert.Contains("File", headers);
        Assert.Contains("Edit", headers);
        Assert.Contains("View", headers);
        Assert.Contains("VICE", headers);
        Assert.Contains("Debug", headers);

        // On macOS, About and Preferences move to the application menu and Quit is the system's,
        // so the menu bar should carry neither a Settings menu nor File > Exit.
        var file = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "File");
        var fileItems = file.Menu!.Items.OfType<NativeMenuItem>()
            .Where(item => item is not NativeMenuItemSeparator)
            .Select(item => item.Header)
            .ToList();
        if (OperatingSystem.IsMacOS())
        {
            Assert.DoesNotContain("Settings", headers);
            Assert.DoesNotContain("Exit", fileItems);
        }
        else
        {
            Assert.Contains("Settings", headers);
            Assert.Contains("Exit", fileItems);
        }
    }

    [AvaloniaFact]
    public void EveryCommandItemCarriesAGesture_OrIsDeliberatelyWithout()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Items that are fine without a keyboard shortcut: rarely-used machine controls and the
        // toggles that have no established shortcut.
        var noGestureExpected = new HashSet<string>
        {
            "Close Folder", "Exit", "Delete", "Reset", "Reboot", "Pause", "Resume", "Power Off",
            "Column Guide", "Word Wrap", "Status Bar", "About READYCode",
        };

        var missing = new List<string>();
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>())
        {
            foreach (var item in top.Menu?.Items.OfType<NativeMenuItem>() ?? [])
            {
                // NativeMenuItemSeparator derives from NativeMenuItem and reports "-" as its header.
                if (item is NativeMenuItemSeparator) continue;

                if (item.Gesture == null && item.Header is { } header && !noGestureExpected.Contains(header))
                    missing.Add($"{top.Header} > {header}");
            }
        }

        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void InWindowMenuBar_RendersTheMenuOnPlatformsWithoutAGlobalOne()
    {
        var window = new MainWindow { DataContext = new MainViewModel(), Width = 900, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = window.GetVisualDescendants().OfType<NativeMenuBar>().SingleOrDefault();
        Assert.NotNull(bar);
    }

    [AvaloniaFact]
    public void ShortcutFallback_BindsEveryMenuGesture_ForPlatformsWithoutANativeMenuBar()
    {
        var window = new MainWindow { DataContext = new MainViewModel(), Width = 900, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Show() already ran this via Opened on a platform with no exported menu (headless), so
        // the bindings are in place; assert on what is actually bound.
        var bound = window.KeyBindings.Select(binding => binding.Gesture).ToList();

        var expected = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
            .SelectMany(top => top.Menu?.Items.OfType<NativeMenuItem>() ?? [])
            .Where(item => item is not NativeMenuItemSeparator && item.Gesture != null)
            .Select(item => item.Gesture!)
            .ToList();

        Assert.NotEmpty(expected);
        Assert.All(expected, gesture => Assert.Contains(gesture, bound));

        // The window also binds one shortcut that is not on the menu: an alternate for Find and
        // Replace, because Cmd+Alt+F is easy for another app to claim first.
        Assert.Equal(expected.Count + 1, bound.Count);
    }

    #endregion
}
